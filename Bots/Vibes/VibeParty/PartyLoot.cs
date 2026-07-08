using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using PartyBot.IPC;
using Styx;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace VibeParty
{
	// Phase 5 — the world-object LEASE BROKER (design doc §6b/§6c/§7). Ground spawns / chests / quest objects
	// are SINGLE-INSTANCE (whoever clicks first, it despawns for everyone), so mirror-mode followers would
	// stampede the same object — one wins, the rest waste the trip. The broker arbitrates one holder per
	// object so followers SPREAD across different ones. Star topology: leader = the sole broker (§2.1); the
	// follower announces interest, the leader grants exclusively, the holder releases on success/give-up.
	//
	// Generic-ish: keyed by resourceGuid (v1 single purpose "collect"; §6c's (guid,purpose) key comes later).
	// Wedge guards (§9): leader TTL sweep (crashed holder), object-gone → release (grant≠success), max-hold
	// give-up + blacklist (bag-full/can't-loot), and bus-unreachable → un-gated FFA (§2.3, never fail closed).
	//
	// THREADING: OnClaim/OnGrant/OnRelease fire on hub threads; FilterCollectibles/Tick run on the bot thread.
	// Shared state is ConcurrentDictionary + a lock on the follower scalars.
	public sealed class PartyLoot
	{
		// --- tuning (seconds) ---
		private const double LeaseTtl = 20;         // leader revokes a lease not renewed within this
		private const double Heartbeat = 5;         // holder re-claims (renews) its lease this often
		private const double ClaimTimeout = 6;      // a claim with no grant back → give up, pick another
		private const double HeldByOtherTtl = 25;   // a "held by X" record self-expires (> LeaseTtl) if we miss a Release
		private const double MaxHold = 30;          // holder give-up: held this long without looting → release + blacklist
		private const double BlacklistTtl = 120;    // local blacklist after a give-up
		private const float TetherYards = 80f;      // don't pursue an object farther than this from the leader (§7.2)

		public PartyLoot(PartyBus bus, bool isLeader)
		{
			_bus = bus;
			_isLeader = isLeader;
			_self = StyxWoW.Me != null ? StyxWoW.Me.Guid : 0;   // set up-front: OnGrant (hub thread) compares against it
			if (isLeader)
			{
				bus.Subscribe("Claim", OnClaim);
				bus.Subscribe("Release", OnReleaseInbound);
			}
			else
			{
				bus.Subscribe("Grant", OnGrant);
				bus.Subscribe("Release", OnReleaseBroadcast);
			}
		}

		// Called from Pulse (bot thread).
		public void Tick()
		{
			if (_isLeader) TickLeader();
			else TickFollower();
		}

		// ──────────────────────────────────────────────────────────────────────
		// Leader: the broker
		// ──────────────────────────────────────────────────────────────────────

		private void OnClaim(PartyMessage msg)
		{
			if (!ulong.TryParse(msg.Payload, out ulong res) || res == 0) return;
			ulong claimant = msg.SenderGuid;
			long now = DateTime.UtcNow.Ticks;
			long ttl = TimeSpan.FromSeconds(LeaseTtl).Ticks;

			bool newGrant;
			ulong holder;
			lock (_leaseLock)
			{
				if (!_leases.TryGetValue(res, out Lease cur) || cur.Holder == claimant || now - cur.GrantedTicks > ttl)
				{
					newGrant = _leases.TryGetValue(res, out Lease prev) ? prev.Holder != claimant : true;
					_leases[res] = new Lease { Holder = claimant, GrantedTicks = now };
					holder = claimant;
				}
				else { newGrant = false; holder = cur.Holder; }
			}

			if (newGrant)
				_bus.Publish("Grant", res + ":" + holder);                 // new holder — tell everyone
			else if (holder != claimant)
				_bus.Publish("Grant", res + ":" + holder, target: claimant); // denied — inform just the loser
			// else: same-holder heartbeat renew → no message
		}

		private void OnReleaseInbound(PartyMessage msg)   // a follower released
		{
			if (!ulong.TryParse(msg.Payload, out ulong res) || res == 0) return;
			bool freed;
			lock (_leaseLock)
				freed = _leases.TryGetValue(res, out Lease l) && l.Holder == msg.SenderGuid && _leases.Remove(res);
			if (freed) _bus.Publish("Release", res.ToString());            // broadcast: it's free again
		}

		private void TickLeader()
		{
			long now = DateTime.UtcNow.Ticks;
			long ttl = TimeSpan.FromSeconds(LeaseTtl).Ticks;
			List<ulong> expired = null;
			lock (_leaseLock)
				foreach (KeyValuePair<ulong, Lease> kv in _leases)
					if (now - kv.Value.GrantedTicks > ttl) (expired ??= new List<ulong>()).Add(kv.Key);
			if (expired == null) return;
			foreach (ulong res in expired)
			{
				lock (_leaseLock) _leases.Remove(res);
				_bus.Publish("Release", res.ToString());
				Logging.Write("[PartyLoot] lease {0} expired (holder silent > {1}s) — reassignable.", res, LeaseTtl);
			}
		}

		// ──────────────────────────────────────────────────────────────────────
		// Follower: the lease client + collect-loop gate
		// ──────────────────────────────────────────────────────────────────────

		private void OnGrant(PartyMessage msg)
		{
			string[] p = (msg.Payload ?? "").Split(':');
			if (p.Length != 2 || !ulong.TryParse(p[0], out ulong res) || !ulong.TryParse(p[1], out ulong holder)) return;
			long now = DateTime.UtcNow.Ticks;
			lock (_fLock)
			{
				if (holder == _self)
				{
					_myLease = res;
					_myLeaseSince = now;
					if (_pendingClaim == res) _pendingClaim = 0;
					_heldByOther.TryRemove(res, out _);
				}
				else
				{
					_heldByOther[res] = now + TimeSpan.FromSeconds(HeldByOtherTtl).Ticks;
					if (_pendingClaim == res) _pendingClaim = 0;   // lost the race → free to claim another
					if (_myLease == res) _myLease = 0;
				}
			}
		}

		private void OnReleaseBroadcast(PartyMessage msg)   // leader says an object is free
		{
			if (!ulong.TryParse(msg.Payload, out ulong res) || res == 0) return;
			lock (_fLock)
			{
				_heldByOther.TryRemove(res, out _);
				if (_myLease == res) _myLease = 0;   // leader revoked ours (TTL) — drop it
			}
		}

		private void TickFollower()
		{
			long now = DateTime.UtcNow.Ticks;

			// Expire stale "held by other" records (holder crashed and we missed the Release).
			foreach (KeyValuePair<ulong, long> kv in _heldByOther)
				if (kv.Value < now) _heldByOther.TryRemove(kv.Key, out _);
			foreach (KeyValuePair<ulong, long> kv in _blacklist)
				if (kv.Value < now) _blacklist.TryRemove(kv.Key, out _);

			ulong lease, pending;
			long leaseSince, pendingSince;
			lock (_fLock) { lease = _myLease; pending = _pendingClaim; leaseSince = _myLeaseSince; pendingSince = _pendingSince; }

			// Pending claim never answered (leader busy/down) → give up so the filter can retry.
			if (pending != 0 && now - pendingSince > TimeSpan.FromSeconds(ClaimTimeout).Ticks)
				lock (_fLock) { if (_pendingClaim == pending) _pendingClaim = 0; }

			if (lease != 0)
			{
				// Object gone (looted by us or despawned) → release, done (grant≠success + loot-success).
				if (ObjectManager.GetObjectByGuid<WoWGameObject>(lease) == null)
				{
					ReleaseMine(lease);
					return;
				}
				// Held too long without clearing it (bag full / can't loot) → give up + blacklist (§9 redo-loop cap).
				if (now - leaseSince > TimeSpan.FromSeconds(MaxHold).Ticks)
				{
					_blacklist[lease] = now + TimeSpan.FromSeconds(BlacklistTtl).Ticks;
					Logging.Write("[PartyLoot] giving up object {0} after {1}s (blacklisted locally).", lease, MaxHold);
					ReleaseMine(lease);
					return;
				}
				// Heartbeat: re-claim to renew the lease while we're still working it.
				if (now - _lastHeartbeat > TimeSpan.FromSeconds(Heartbeat).Ticks)
				{
					_lastHeartbeat = now;
					_bus.Publish("Claim", lease.ToString());
				}
			}
		}

		private void ReleaseMine(ulong res)
		{
			lock (_fLock) { if (_myLease == res) _myLease = 0; }
			_bus.Publish("Release", res.ToString());
		}

		// Loot-filter hook (bot thread): keep only the object WE hold a lease for; prune others' objects; claim
		// the nearest free candidate. Corpses (WoWUnit loot) are left untouched — those are per-player (Phase 4),
		// no competition. Degraded when the hub is unreachable: leave collection un-gated (never freeze).
		public void FilterCollectibles(HashSet<WoWObject> outgoing)
		{
			if (!_bus.Connected) return;   // no broker → un-coordinated FFA (safety gate stays; see PruneDangerousCollectibles)

			ulong lease;
			lock (_fLock) lease = _myLease;
			WoWPoint leaderLoc = VibeParty.LeaderLocation;

			WoWGameObject best = null;
			double bestDist = double.MaxValue;

			foreach (WoWObject o in outgoing.ToList())
			{
				if (o is not WoWGameObject go) continue;   // corpses etc. — not lease-gated
				ulong g = go.Guid;
				if (g == lease) continue;                  // ours — pursue it
				if (_blacklist.ContainsKey(g)) { outgoing.Remove(go); continue; }
				if (_heldByOther.ContainsKey(g)) { outgoing.Remove(go); continue; }
				// Unclaimed candidate — don't pursue until leased. Track the nearest within the leader tether.
				outgoing.Remove(go);
				// Rogue-self-select (§6b): never CLAIM a locked chest we can't open — CanLoot already checks our
				// own Lockpicking vs the lock's RequiredSkill, so a non-rogue/under-skilled member skips it and a
				// capable rogue is the only one that claims. Defensive: LevelbotIncludeLootsFilter already gates on
				// CanLoot upstream, but re-checking here keeps the rogue-only rule true regardless of filter order.
				if (!go.CanLoot) continue;
				if (leaderLoc != WoWPoint.Zero && go.Location.Distance(leaderLoc) > TetherYards) continue;
				if (go.Distance < bestDist) { best = go; bestDist = go.Distance; }
			}

			// One active lease/claim at a time: claim the nearest free candidate when we hold nothing pending.
			bool claim = false;
			lock (_fLock)
			{
				if (_myLease == 0 && _pendingClaim == 0 && best != null)
				{
					_pendingClaim = best.Guid;
					_pendingSince = DateTime.UtcNow.Ticks;
					claim = true;
				}
			}
			if (claim) _bus.Publish("Claim", best!.Guid.ToString());
		}

		private struct Lease { public ulong Holder; public long GrantedTicks; }

		private readonly PartyBus _bus;
		private readonly bool _isLeader;

		// leader state
		private readonly Dictionary<ulong, Lease> _leases = new Dictionary<ulong, Lease>();
		private readonly object _leaseLock = new object();

		// follower state
		private readonly object _fLock = new object();
		private ulong _self;
		private ulong _myLease;
		private long _myLeaseSince;
		private ulong _pendingClaim;
		private long _pendingSince;
		private long _lastHeartbeat;
		private readonly ConcurrentDictionary<ulong, long> _heldByOther = new ConcurrentDictionary<ulong, long>();
		private readonly ConcurrentDictionary<ulong, long> _blacklist = new ConcurrentDictionary<ulong, long>();
	}
}
