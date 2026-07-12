using System;
using System.Collections.Concurrent;
using PartyBot.IPC;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace VibeParty
{
	// Party water service — a PartyBus client. A mage bot conjures water (free/native, consistent with the
	// zero-BUY design) and hands it to party mana-users who ask; the mage can't SEE who's out of water (private
	// bag state), so requesters announce it. Downtime-only, top-up-on-request (user decision 2026-07-08).
	//
	// "Current" water = highest item-ID conjured drink (higher rank = higher id). The mage advertises its best id
	// (`WaterKind`, leader-relayed); everyone DELETES conjured drinks below the current id (stale lower ranks),
	// and requesters ask when low on the CURRENT water — so a mage learning a better Conjure Water makes the party
	// want the new one. The mage keeps a 5-water RESERVE for itself so it never trades down to regen like a dummy.
	//
	// This class holds the DATA + bus wiring; the conjure/trade behavior lives in VibeParty (TreeSharp + trade Lua).
	// THREADING: On* handlers fire on hub threads → pure data only.
	public sealed class PartyWater
	{
		public const int ReserveForSelf = 5;     // the mage never trades below this many drinks
		private const int LowWater = 2;          // refill-on-empty: ask only when down to the last drink or two
		                                         // (the buffer covers one more rest while the mage walks over)
		private const double RequestEvery = 30;  // seconds between our water requests
		private const double ServeCooldown = 20; // per-requester cooldown after a delivery attempt
		private const double RequestStale = 90;  // drop a request we haven't re-heard in this long
		private const double OfferHold = 15;     // requester holds still this long after a mage's offer
		private const double TickEvery = 3;      // advertise / clean-stale cadence

		public PartyWater(PartyBus bus)
		{
			_bus = bus;
			_self = StyxWoW.Me != null ? StyxWoW.Me.Guid : 0;
			bus.Subscribe("WaterRequest", OnWaterRequest);   // mage collects these
			bus.Subscribe("WaterOffer", OnWaterOffer);       // requester: "hold still, water inbound"
			bus.Subscribe("WaterKind", OnWaterKind);         // requester: the current (best) water id
		}

		public bool AwaitingWater => DateTime.UtcNow.Ticks < _awaitUntil;
		public bool HasRequests => !_requests.IsEmpty;

		// From Pulse (bot thread) — role-branched upkeep, throttled.
		public void WaterTick()
		{
			if ((DateTime.UtcNow - _lastTick).TotalSeconds < TickEvery) return;
			_lastTick = DateTime.UtcNow;
			if (StyxWoW.Me.Class == WoWClass.Mage) MageAdvertise();
			else RequesterUpkeep();
		}

		// ── requester side ─────────────────────────────────────────────────────

		private void OnWaterOffer(PartyMessage msg) => _awaitUntil = DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(OfferHold).Ticks;

		private void OnWaterKind(PartyMessage msg)
		{
			if (int.TryParse(msg.Payload, out int id) && id > 0) _currentWaterId = id;   // pure data
		}

		private void RequesterUpkeep()
		{
			LocalPlayer me = StyxWoW.Me;
			if (me.MaxMana <= 0 || !me.IsInParty || me.Combat) return;

			// Drop stale lower-rank drinks only once we hold a serviceable amount of the CURRENT rank — the
			// mage's fresh rank arrives 2/cast and deleting on announcement would leave the whole party dry.
			if (_currentWaterId > 0 && CountOfItem(_currentWaterId) >= LowWater) DeleteStaleWater(_currentWaterId);

			if ((DateTime.UtcNow - _lastRequest).TotalSeconds < RequestEvery) return;
			if (!MageInParty()) return;
			// Low on ANY conjured drink — old-rank stock still restores mana, so we don't nag the mage for the
			// new rank while we can drink what we have; the purge above converges us to the current rank.
			int have = ConjuredWaterCount();
			if (have >= LowWater) return;
			_lastRequest = DateTime.UtcNow;
			_bus.Publish("WaterRequest", _self.ToString());
		}

		// ── mage side ──────────────────────────────────────────────────────────

		private void OnWaterRequest(PartyMessage msg)
		{
			if (!ulong.TryParse(msg.Payload, out ulong req) || req == 0 || req == _self) return;
			_requests[req] = DateTime.UtcNow.Ticks;   // pure data
		}

		// The mage: advertise the best conjured water id it holds, and delete its own out-ranked drinks.
		// A fresh rank trickles in at 2/cast (+2 per level) — switching the party over (and purging old stock)
		// the moment the first new stack appears would spike demand exactly when production is slowest. Hold the
		// announcement until we actually hold a serviceable amount of the new rank; until then everyone (us
		// included) keeps drinking the old one.
		private void MageAdvertise()
		{
			int best = BestConjuredId();
			if (best <= 0) return;
			if (best != _currentWaterId && CountOfItem(best) < ReserveForSelf) return;
			_currentWaterId = best;
			DeleteStaleWater(best);
			if (best != _advertised)
			{
				_advertised = best;
				_bus.Publish("WaterKind", best.ToString());
			}
		}

		public WoWPlayer NextRequester()
		{
			long now = DateTime.UtcNow.Ticks;
			long stale = now - TimeSpan.FromSeconds(RequestStale).Ticks;
			WoWPlayer best = null;
			double bd = double.MaxValue;
			foreach (System.Collections.Generic.KeyValuePair<ulong, long> kv in _requests)
			{
				if (kv.Value < stale) { _requests.TryRemove(kv.Key, out _); continue; }
				if (_serveCd.TryGetValue(kv.Key, out long until) && now < until) continue;
				WoWPlayer p = ObjectManager.GetObjectByGuid<WoWPlayer>(kv.Key);
				if (p == null || p.Dead) continue;
				if (p.Distance < bd) { best = p; bd = p.Distance; }
			}
			return best;
		}

		public void SendOffer(ulong guid) => _bus.Publish("WaterOffer", _self.ToString(), target: guid);

		public void Served(ulong guid)
		{
			_requests.TryRemove(guid, out _);
			_serveCd[guid] = DateTime.UtcNow.Ticks + TimeSpan.FromSeconds(ServeCooldown).Ticks;
		}

		// ── shared helpers ─────────────────────────────────────────────────────

		// Conjured WATER only (restores MANA) — NOT conjured food (bread/muffin, HP, no use here — user 2026-07-08).
		// On 3.3.5a every water rank is "Conjured … Water" (Conjured Water, Conjured Spring Water, Conjured Crystal
		// Water, …); food never contains "Water". Higher item id = higher rank.
		private static bool IsConjuredWater(WoWItem i)
			=> i != null && i.Name != null
			   && i.Name.StartsWith("Conjured ", StringComparison.Ordinal)
			   && i.Name.IndexOf("Water", StringComparison.Ordinal) >= 0;

		public static int ConjuredWaterCount()
		{
			int n = 0;
			foreach (WoWItem i in StyxWoW.Me.BagItems) if (IsConjuredWater(i)) n += (int)i.StackCount;
			return n;
		}

		private static int CountOfItem(int entry)
		{
			int n = 0;
			foreach (WoWItem i in StyxWoW.Me.BagItems) if (IsConjuredWater(i) && (int)i.Entry == entry) n += (int)i.StackCount;
			return n;
		}

		private static int BestConjuredId()
		{
			int best = 0;
			foreach (WoWItem i in StyxWoW.Me.BagItems) if (IsConjuredWater(i) && (int)i.Entry > best) best = (int)i.Entry;
			return best;
		}

		// Delete conjured drinks below the current rank (item id) — worthless, temporary, and drinking a lower
		// rank restores less mana. Only ever touches "Conjured " items with id < keepId. (Destructive by request.)
		private static void DeleteStaleWater(int keepId)
		{
			Lua.DoString(
				"for b=0,4 do for s=1,GetContainerNumSlots(b) do " +
				"local l=GetContainerItemLink(b,s) local id=GetContainerItemID(b,s) " +
				"if l and id and id<" + keepId + " and string.find(l,'Conjured') and string.find(l,'Water') then PickupContainerItem(b,s) DeleteCursorItem() end " +
				"end end");
		}

		private static bool MageInParty()
		{
			foreach (WoWPlayer p in StyxWoW.Me.PartyMembers)
				if (p != null && p.Class == WoWClass.Mage) return true;
			return false;
		}

		private readonly PartyBus _bus;
		private readonly ulong _self;
		private int _currentWaterId;
		private int _advertised;
		private DateTime _lastTick = DateTime.MinValue;
		private DateTime _lastRequest = DateTime.MinValue;
		private long _awaitUntil;
		private readonly ConcurrentDictionary<ulong, long> _requests = new ConcurrentDictionary<ulong, long>();
		private readonly ConcurrentDictionary<ulong, long> _serveCd = new ConcurrentDictionary<ulong, long>();
	}
}
