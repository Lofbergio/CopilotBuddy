using System;
using System.Collections.Generic;
using System.Threading;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Combat;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.World;

namespace Styx.Logic
{
	public static class Mount
	{
		private static readonly WaitTimer _mountTimer = WaitTimer.TenSeconds;
		private static readonly WaitTimer _combatTimer = WaitTimer.TenSeconds;
		private static readonly List<WoWPoint> _cantMountSpots = new List<WoWPoint>();
		private static CanMountDelegate? _defaultCanMount;
		private static bool _wasMounted;
		private static LocationRetriever? _currentDestinationRetriever;

		/// <summary>
		/// Fired when the player mounts up (HB 4.3.4 compatibility).
		/// </summary>
		public static event EventHandler<MountUpEventArgs>? OnMountUp;

		/// <summary>
		/// Fired when the player dismounts (HB 4.3.4 compatibility).
		/// </summary>
		public static event EventHandler<EventArgs>? OnDismount;

		private static LocalPlayer? Me => ObjectManager.Me;

		static Mount()
		{
			BotEvents.Player.OnMobKilled += OnMobKilled;
		}

		private static void OnMobKilled(BotEvents.Player.MobKilledEventArgs args)
		{
			_combatTimer.Reset();
		}

	/// <summary>
	/// True when both the post-combat and post-mount cooldown timers are ready.
	/// Used by Flightor.MountHelper.CanMount so it respects the same cooldowns as
	/// Mount.CanMount() without routing through the LevelBot mount path.
	/// </summary>
	internal static bool AreMountTimersReady => _combatTimer.IsFinished && _mountTimer.IsFinished;

	/// <summary>
	/// Resets the mount timer after a Flightor-initiated mount attempt, preventing
	/// immediate retry spam when the mount is cancelled (e.g. by water or GCD).
	/// </summary>
	internal static void ResetMountTimer() => _mountTimer.Reset();

	public static void Dismount() => Dismount(string.Empty);

	public static void ClearShapeshift()
	{
		LocalPlayer? me = Me;
		if (me == null)
				return;

			if (me.Shapeshift != ShapeshiftForm.Normal)
			{
				Logging.WriteDebug("Canceling Shapeshift form: {0}", me.Shapeshift);
				Lua.DoString("CancelShapeshiftForm()");
			}
		}

		// Dismount-in-flight latch. me.Mounted is a memory read that stays TRUE for ~2-4 ticks after the
		// Dismount() Lua executes (server ack lag), so the not-mounted guard below can't catch re-entry:
		// per-tick callers (ShouldDismount → ActionSetTarget/ActionPull, Navigator, StuckHandler…) re-issued
		// Dismount() into that window — the first call dismounts, every repeat draws the red "You are not
		// mounted so you can't dismount" chat error (observed 3× per pull, i.e. all session long). While a
		// dismount is pending, both Dismount and ShouldDismount stand down. Time-based is safe: you cannot
		// be mounted again within the window (mounting itself is a 3s cast).
		private static DateTime _dismountIssuedAt = DateTime.MinValue;
		private static bool DismountPending => (DateTime.UtcNow - _dismountIssuedAt).TotalMilliseconds < 1500;

		// One-step-lookahead tuning (see MountUp): a target this close means the next action is a pull.
		private const float ImminentFightRange = 45f;
		// Walk 7yd/s vs ~3.5s mount cast + ~11yd/s ride: break-even ≈ 60yd. Shorter trips: stay on foot.
		private const float MinMountTravelDistance = 60f;
		// Mount race handling: throttle for the defer log, and a SHORT retry cooldown after a failed attempt
		// (1s race-loss / 5s unknown) — deliberately not the 10s _mountTimer, which only arms on SUCCESS now.
		private static DateTime _nextMountDeferLogAt = DateTime.MinValue;
		private static DateTime _nextMountAttemptAt = DateTime.MinValue;

		public static void Dismount(string reason)
		{
			LocalPlayer? me = Me;
			if (me == null) return;

			if (DismountPending)
				return;

			// Nothing to dismount (not mounted, not in flight form) → return WITHOUT logging or issuing the Lua.
			// The log + Lua used to run regardless: callers fire Dismount on a stale me.Mounted (true for a tick
			// after dismounting), so a second call hit the client when already on foot → the "You are not mounted"
			// red error in chat, plus pointless "Stop and dismount" log spam during the vendor-run POI thrash.
			ShapeshiftForm shapeshift = me.Shapeshift;
			if (!(me.Mounted || shapeshift == ShapeshiftForm.FlightForm || shapeshift == ShapeshiftForm.EpicFlightForm))
				return;

			_dismountIssuedAt = DateTime.UtcNow;
			Logging.WriteDebug(string.IsNullOrEmpty(reason) ? "Stop and dismount." : $"Stop and dismount. Reason: {reason}");

			// HB 4.3.4: no descent loop — just stop and dismount.
			// Descent before dismount is the caller's responsibility
			// (gather [4] already handles it with WoWMovement.Move(Descend) + WaitContinue(!IsFlying)).
			WoWMovement.MoveStop();

			if (shapeshift == ShapeshiftForm.FlightForm || shapeshift == ShapeshiftForm.EpicFlightForm)
				Lua.DoString("CancelShapeshiftForm()");
			else
				Lua.DoString("Dismount()");

			// HB 6.2.3: Fire OnDismount event after dismounting
			RaiseOnDismount(reason);
		}

		/// <summary>
		/// HB 6.2.3 Mount.smethod_1: Safely raises OnDismount event,
		/// catching exceptions from individual subscribers.
		/// </summary>
		internal static void RaiseOnDismount(string? reason)
		{
			reason ??= string.Empty;
			_rollFreshMount = true;   // dismounted — next mount-up picks a fresh random mount (re-arm before the early-out)
			EventHandler<EventArgs>? handler = OnDismount;
			if (handler == null)
				return;

			foreach (Delegate d in handler.GetInvocationList())
			{
				try
				{
					d.DynamicInvoke(reason, EventArgs.Empty);
				}
				catch (Exception ex)
				{
					Logging.WriteException(ex);
				}
			}
		}

		private static readonly Random _random = new Random();
		// Re-roll a fresh random mount only ONCE per mount-up (re-armed on dismount), not on every MountUp call.
		// MountUp runs each travel tick and a stalled attempt (post-death flee / can't-mount-here) would otherwise
		// re-roll + log every frame — the "Auto-detected random ground mount: White Kodo" spam flood.
		private static bool _rollFreshMount = true;

		/// <summary>
		/// Auto-detects and sets mount name if FindMountAutomatically is enabled.
		/// Ported from HB 4.3.4.
		/// </summary>
		public static void AutoDetectMount()
		{
			if (!CharacterSettings.Instance.UseMount || !CharacterSettings.Instance.FindMountAutomatically)
				return;

			if (CharacterSettings.Instance.UseRandomMount)
			{
				if (!_rollFreshMount)
					return;   // already rolled for this mount-up; don't re-roll/log every tick

				// Random mount selection. Only consume the flag once we actually pick something — so if mounts
				// aren't in the companion list yet, we retry next tick instead of locking in an empty name.
				bool rolled = false;
				var groundMounts = MountHelper.GroundMounts;
				if (groundMounts != null && groundMounts.Count > 0)
				{
					var mount = groundMounts[_random.Next(0, groundMounts.Count)];
					CharacterSettings.Instance.MountName = mount.CreatureSpellId.ToString();
					Logging.WriteDebug("Auto-detected random ground mount: {0}", mount.Name);
					rolled = true;
				}

				var flyingMounts = MountHelper.FlyingMounts;
				if (flyingMounts != null && flyingMounts.Count > 0)
				{
					var mount = flyingMounts[_random.Next(0, flyingMounts.Count)];
					CharacterSettings.Instance.FlyingMountName = mount.CreatureSpellId.ToString();
					Logging.WriteDebug("Auto-detected random flying mount: {0}", mount.Name);
					rolled = true;
				}

				if (rolled)
					_rollFreshMount = false;
			}
			else
			{
				// Use first available mount if not set
				string mountName = CharacterSettings.Instance.MountName;
				if (string.IsNullOrEmpty(mountName) || mountName == "Mount Name Here" || mountName.Contains("Automatically detected"))
				{
					var groundMounts = MountHelper.GroundMounts;
					if (groundMounts != null && groundMounts.Count > 0)
					{
						var mount = groundMounts[0];
						CharacterSettings.Instance.MountName = mount.CreatureSpellId.ToString();
						Logging.WriteDebug("Auto-detected ground mount: {0}", mount.Name);
					}
				}

				string flyingMount = CharacterSettings.Instance.FlyingMountName;
				if (string.IsNullOrEmpty(flyingMount) || flyingMount.Contains("Automatically detected"))
				{
					var flyingMounts = MountHelper.FlyingMounts;
					if (flyingMounts != null && flyingMounts.Count > 0)
					{
						var mount = flyingMounts[0];
						CharacterSettings.Instance.FlyingMountName = mount.CreatureSpellId.ToString();
						Logging.WriteDebug("Auto-detected flying mount: {0}", mount.Name);
					}
				}
			}
		}

		public static void MountUp()
		{
			if (_defaultCanMount == null)
			{
				_defaultCanMount = DefaultCanMount;
			}
			MountUp(_defaultCanMount);
		}

		private static bool DefaultCanMount()
		{
			return true;
		}

		/// <summary>
		/// Mounts up with a custom can-mount check and destination (HB 4.3.4).
		/// Returns true if mount was attempted.
		/// </summary>
		public static bool MountUp(CanMountDelegate extra, LocationRetriever travelingTo)
		{
			_currentDestinationRetriever = travelingTo;
			return MountUp(extra);
		}

		[Obsolete("Use MountUp(CanMountDelegate, LocationRetriever) instead.")]
		public static bool MountUp(CanMountDelegate extra)
		{
			if (!extra())
				return false;

			if (!LevelbotSettings.Instance.UseMount)
				return false;

			LocalPlayer? me = Me;
			if (me == null)
				return false;

			// Already mounted / dead / ghost → nothing to do. Check BEFORE AutoDetectMount so we don't re-roll
			// the random mount + log "Auto-detected ..." every travel tick while mounted (the [D] spam flood).
			if (me.Mounted || me.Dead || me.IsGhost)
				return false;

			// --- One-step lookahead: don't start a ~3s commitment the very next action will cancel ---
			// ENGAGE vs EVADE classifier (the trek-safety story): a target we CAN fight → stay on foot and
			// take it deliberately; a hostile we can NEVER beat (red ≥ our level+3, or a relevant elite) near
			// its SERVER aggro bubble → the OPPOSITE: mount NOW — speed is the only protection, and evade
			// overrides both the stay-afoot gate and the short-trip gate. TrekSafety bends routes around
			// STATIC red/pack spawns; this live check covers roamers and anything the DB marks missed.
			bool evadeDanger = false;
			if (!me.Combat)
			{
				foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
				{
					if (u == null || !u.IsAlive || u.IsPlayer || u.IsTotem) continue;
					bool red = u.Level >= me.Level + 3;
					bool scaryElite = u.Elite && u.Level >= me.Level - 2;
					if (!red && !scaryElite) continue;
					float bubble = u.GetAggroRange(me);      // 0 when not hostile to us (reaction gate inside)
					if (bubble <= 0f) continue;
					if (u.Distance < bubble + 10f) { evadeDanger = true; break; }
				}
			}

			if (!evadeDanger)
			{
				// (1) Fight imminent: a Kill POI, or an engageable target already inside ImminentFightRange —
				//     the first pull cast auto-dismounts us, so mounting here is mount → ride 10yd → knocked
				//     off. Also keeps a vendor-run bot on foot while TransitPeel still has a hostile to clear.
				//     Out-of-combat only: in combat the regular mount is blocked anyway and the travel-form
				//     flee path must stay available.
				if (!me.Combat)
				{
					WoWUnit? fu = Targeting.Instance.FirstUnit;
					if (BotPoi.Current.Type == PoiType.Kill
						|| (fu != null && fu.IsAlive && fu.Distance < ImminentFightRange))
						return false;
				}
				// (2) Trip too short to pay back the mount cast: walking ~7yd/s vs ~3.5s cast + ~11yd/s ride
				//     breaks even near 60yd — below that, mounting LOSES time (and looks bot-dumb doing it).
				if (_currentDestinationRetriever != null)
				{
					WoWPoint dest = _currentDestinationRetriever();
					if (dest != WoWPoint.Empty && me.Location.Distance(dest) < MinMountTravelDistance)
						return false;
				}
			}
			else if (DateTime.UtcNow >= _nextMountDeferLogAt)
			{
				Logging.WriteDebug("[Mount] danger nearby (red/elite in aggro range) — mounting to evade.");
				_nextMountDeferLogAt = DateTime.UtcNow.AddSeconds(2);
			}

			// Race guard: a spell that JUST fired (e.g. the routine's pre-buff shield, 133ms before the mount
			// decision in log 2026-07-02_1057) holds the GCD; CallCompanion issued into that window silently
			// never starts — the old code then blind-waited 6.5s and the 10s mount timer blocked any retry, so
			// the bot walked off unmounted. DEFER instead: cheap, and the next travel tick lands past the GCD.
			// Logged with the blocker's name so cast/GCD races are visible in the log, per-episode throttled.
			if (me.IsCasting || me.IsChanneling || SpellManager.GlobalCooldown)
			{
				if (DateTime.UtcNow >= _nextMountDeferLogAt)
				{
					Logging.WriteDebug("[Mount] deferring mount-up: {0} — retry next tick.",
						me.IsCasting ? "casting " + (me.CastingSpell?.Name ?? "?") : "GCD active (a spell just fired)");
					_nextMountDeferLogAt = DateTime.UtcNow.AddSeconds(2);
				}
				return false;
			}

			// Short race-retry cooldown (set by a failed DoMount) — NOT the 10s _mountTimer.
			if (DateTime.UtcNow < _nextMountAttemptAt)
				return false;

			// Auto-detect mount if enabled
			AutoDetectMount();

			// Ghost Wolf (Shaman) / Travel Form (Druid): fallback when regular mounts can't be used.
			// Both spells are outdoors-only in WotLK 3.3.5a (Wowhead verified).
			// In combat → Travel Form only (instant). Ghost Wolf is 2s cast, useless in combat.
			// No ground mounts yet → Ghost Wolf or Travel Form out-of-combat.
			// Already in a speed form → skip (nothing to do).
			bool regularMountBlocked = !me.IsOutdoors || me.Combat
				|| (MountHelper.GroundMounts?.Count ?? 0) == 0;
			if (regularMountBlocked)
			{
				if (me.HasAura("Ghost Wolf") || me.HasAura("Travel Form"))
					return false;
				return TryUseShapeshiftSpeedBuff(me);
			}

			// --- Regular mount path ---
			if (me.Level < 20)
				return false;

            //bool canFly = me.MovementInfo.CanFly;
			// Choose the right mount for this zone up front so the guard and log are accurate.
			// Flightor.CanFly checks IsFlyableArea() + riding skill — NOT the raw movement flag
			// (me.MovementInfo.CanFly is only set while airborne, so it was always false on the ground).
			bool canFly = Flightor.CanFly;
			string flyingMountName = CharacterSettings.Instance.FlyingMountName;
			string effectiveMountName = (canFly && !string.IsNullOrEmpty(flyingMountName))
				? flyingMountName
				: LevelbotSettings.Instance.MountName;

			if (string.IsNullOrEmpty(effectiveMountName))
				return false;

			if (!CanMount())
				return false;

			// Wait for the character to actually STOP, not a fixed sleep — MoveStop isn't instant (the char
			// glides a tick or two) and CallCompanion issued mid-glide fails with "Can't do that while moving"
			// (caught live by the event-driven wait, log 2026-07-02_1158 12:01:44). Bounded so a forced-move
			// state can't wedge us; on timeout we bail and retry next tick rather than cast into a known fail.
			WoWMovement.MoveStop();
			int stopWait = Environment.TickCount;
			while (me.IsMoving && Environment.TickCount - stopWait < 1000)
				StyxWoW.Sleep(50);
			if (me.IsMoving)
			{
				Logging.WriteDebug("[Mount] still moving 1s after MoveStop — deferring mount to next tick.");
				return false;
			}
			Logging.Write("Mounting: {0}{1}", effectiveMountName, canFly ? " [flying]" : "");

			// The 10s anti-spam timer arms on SUCCESS only. A race-lost/failed attempt sets the short
			// _nextMountAttemptAt cooldown inside DoMount instead, so we retry in ~1s, not 10.
			if (!DoMount())
				return false;
			_mountTimer.Reset();
			return true;
		}

		private static bool DoMount()
		{
			LocalPlayer? me = Me;
			if (me == null) return false;

			// Use flying mount in fly zones, ground mount everywhere else.
			bool canFly = Flightor.CanFly;
			string flyingMountName = CharacterSettings.Instance.FlyingMountName;
			string mountName = (canFly && !string.IsNullOrEmpty(flyingMountName))
				? flyingMountName
				: LevelbotSettings.Instance.MountName;
			if (string.IsNullOrEmpty(mountName)) return false;

			// Handle Blood Elf Paladin mount name differences
			if (me.Race == WoWRace.BloodElf && me.Class == WoWClass.Paladin)
			{
				string lowerMount = mountName.ToLowerInvariant();
				if (lowerMount == "warhorse" || lowerMount == "summon warhorse")
				{
					mountName = "Summon Charger";
				}
				else if (lowerMount == "charger" || lowerMount == "summon charger")
				{
					mountName = "Summon Charger";
				}
			}

			// EVENT-DRIVEN mount wait (the LuaEventWait pattern — attach, pump ProcessPendingEvents, detach).
			// The client TELLS us the cast lifecycle the instant it happens: START (in flight — wait
			// confidently), FAILED/INTERRUPTED (resolve immediately with the real reason — no grace-window
			// guessing), UI_ERROR_MESSAGE (the exact red error, e.g. "Spell is not ready yet" = a GCD race).
			// Memory reads still answer the STATE questions (Mounted, Combat); events answer the EDGES.
			bool castStarted = false, castEnded = false;
			string failReason = null, redError = null;

			LuaEventHandlerDelegate onStart = (s, e) =>
			{ if (e.Args.Length > 0 && (string)e.Args[0] == "player") { castStarted = true; } };
			LuaEventHandlerDelegate onFail = (s, e) =>
			{ if (e.Args.Length > 0 && (string)e.Args[0] == "player") { failReason = e.EventName; castEnded = true; } };
			LuaEventHandlerDelegate onStop = (s, e) =>
			{ if (e.Args.Length > 0 && (string)e.Args[0] == "player") { castEnded = true; } };
			LuaEventHandlerDelegate onError = (s, e) =>
			{ if (e.Args.Length > 0) redError = e.Args[0] as string; };

			Lua.Events.AttachEvent("UNIT_SPELLCAST_START", onStart);
			Lua.Events.AttachEvent("UNIT_SPELLCAST_FAILED", onFail);
			Lua.Events.AttachEvent("UNIT_SPELLCAST_INTERRUPTED", onFail);
			Lua.Events.AttachEvent("UNIT_SPELLCAST_STOP", onStop);
			Lua.Events.AttachEvent("UI_ERROR_MESSAGE", onError);
			try
			{
				Lua.DoString(string.Format("CallCompanion('MOUNT', {0})", GetMountIndex(mountName)));

				int startTime = Environment.TickCount;
				int endedAt = 0;
				while (!me.Mounted && Environment.TickCount - startTime < 6500)
				{
					LuaEvents.ProcessPendingEvents();   // pump — handlers above fire on THIS thread, now

					if (me.Combat)
					{
						Logging.WriteDebug("[Mount] mount attempt aborted — entered combat.");
						_nextMountAttemptAt = DateTime.UtcNow.AddSeconds(1);
						return false;
					}

					if (failReason != null)
					{
						// The client told us exactly what happened — no inference. A red error naming a
						// location problem also poisons the spot like the old heuristic did.
						Logging.Write("[Mount] mount cast {0}{1} — retrying in 1s.",
							failReason == "UNIT_SPELLCAST_INTERRUPTED" ? "INTERRUPTED (hit/moved)" : "FAILED",
							string.IsNullOrEmpty(redError) ? "" : $" ('{redError}')");
						if (redError != null && redError.IndexOf("mount", StringComparison.OrdinalIgnoreCase) >= 0
							&& redError.IndexOf("here", StringComparison.OrdinalIgnoreCase) >= 0)
							AddCantMountSpot(me.Location);
						_nextMountAttemptAt = DateTime.UtcNow.AddSeconds(1);
						return false;
					}

					if (!castStarted && Environment.TickCount - startTime > 600)
					{
						// RACE MARKER: CallCompanion went out but the client never began a cast and never
						// errored. The MountUp defer-guard should pre-empt GCD races; anything here is new.
						Logging.Write("[Mount] mount cast never STARTED ({0}ms) — GCD={1}, casting={2}, redError='{3}'. Retrying in 1s.",
							Environment.TickCount - startTime, SpellManager.GlobalCooldown, me.IsCasting, redError ?? "");
						_nextMountAttemptAt = DateTime.UtcNow.AddSeconds(1);
						return false;
					}

					if (castEnded && !me.Mounted)
					{
						// STOP fired (cast completed or was cut) — give me.Mounted a beat to reflect.
						if (endedAt == 0) endedAt = Environment.TickCount;
						else if (Environment.TickCount - endedAt > 800)
						{
							Logging.Write("[Mount] mount cast ended without mounting — retrying in 1s.");
							_nextMountAttemptAt = DateTime.UtcNow.AddSeconds(1);
							return false;
						}
					}

					StyxWoW.Sleep(30);
				}

				if (!me.Mounted)
				{
					Logging.Write("[Mount] not mounted after 6.5s — retrying in 5s.");
					_nextMountAttemptAt = DateTime.UtcNow.AddSeconds(5);
					return false;
				}

				// Mount succeeded — any stale cant-mount spots near this location are now invalid.
				// (e.g. spots recorded during a previous combat pass at this exact location)
				RemoveCantMountSpotsNear(me.Location, 10f);
				return true;
			}
			finally
			{
				Lua.Events.DetachEvent("UNIT_SPELLCAST_START", onStart);
				Lua.Events.DetachEvent("UNIT_SPELLCAST_FAILED", onFail);
				Lua.Events.DetachEvent("UNIT_SPELLCAST_INTERRUPTED", onFail);
				Lua.Events.DetachEvent("UNIT_SPELLCAST_STOP", onStop);
				Lua.Events.DetachEvent("UI_ERROR_MESSAGE", onError);
			}
		}

		/// <summary>
		/// Attempts to use Ghost Wolf (Shaman) or Travel Form (Druid) as a speed buff
		/// when regular mounts can't be used (in combat, or no ground mounts yet).
		/// WotLK 3.3.5a (Wowhead verified):
		/// - Both Ghost Wolf and Travel Form have the "Can only be used outdoors" flag.
		///   Neither works indoors — returns false immediately.
		/// - Ghost Wolf: 2s cast, no explicit combat ban but interrupted by damage in combat.
		/// - Travel Form: instant cast, usable in combat (no cast to interrupt).
		/// </summary>
		// A 2s Ghost Wolf is wasted when a fight is seconds away (see the gate in TryUseShapeshiftSpeedBuff).
		private const float GhostWolfSkipNearHostile = 45f;
		private static bool NearbyHostile(LocalPlayer me, float range)
		{
			float r2 = range * range;
			foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
				if (u != null && !u.Dead && u.IsHostile && me.Location.DistanceSqr(u.Location) <= r2)
					return true;
			return false;
		}

		private static bool TryUseShapeshiftSpeedBuff(LocalPlayer me)
		{
			// Both spells are outdoors-only in WotLK 3.3.5a.
			if (!me.IsOutdoors)
				return false;

			// In combat: Travel Form only (instant, won't be interrupted).
			// Ghost Wolf is a 2s cast — interrupted by combat damage, not viable.
			if (me.Combat)
			{
				if (SpellManager.HasSpell("Travel Form"))
				{
					Logging.Write("Mounting: Using Travel Form since we are in combat.");
					SpellManager.Cast("Travel Form");
					return true;
				}
				return false;
			}

			// Out of combat, no ground mounts — use class speed buff as fallback.
			if (SpellManager.HasSpell("Ghost Wolf"))
			{
				Logging.Write("Mounting: Using Ghost Wolf since we don't have any mounts yet.");

				// Ghost Wolf's cast time varies by talent (Enhancement's Ancestral Swiftness:
				// -0.5s/pt → instant at 2pts). A cast-time Ghost Wolf is interrupted by movement, so
				// MountUp (called every travel tick) spam-cast it forever while running. Stop+wait
				// only when it actually has a cast time; an instant one can be cast on the move.
				var ghostWolf = SpellManager.GetSpellByName("Ghost Wolf");
				bool hasCastTime = ghostWolf == null || ghostWolf.CastTime > 0;
				if (hasCastTime)
				{
					if (me.HasAura("Ghost Wolf"))
						return true;            // already up — don't re-cast it every travel tick

					// Imminent-combat gate: a hostile this close means we will be in combat before a 2s Ghost
					// Wolf pays off (it just gets cancelled) - a wasted cast that looks bot-like. Run on foot;
					// we engage shortly. (Instant Ghost Wolf / Travel Form skip this.)
					if (NearbyHostile(me, GhostWolfSkipNearHostile))
						return false;

					// A cast-time Ghost Wolf silently fails if started while moving OR on GCD, and MountUp
					// re-tries it every travel tick (the 3x-retry spam). Two failure modes seen:
					//  (1) the IsMoving flag clears before the char physically stops, so an IsMoving poll exits
					//      while still gliding -> "can't cast while moving". Settle with a fixed delay instead.
					//  (2) a pre-buff (Lightning Shield) leaves the GCD up -> the cast is swallowed. Wait it out.
					WoWMovement.MoveStop();
					StyxWoW.Sleep(350);
					for (int t0 = Environment.TickCount;
					     SpellManager.GlobalCooldown && Environment.TickCount - t0 < 1600; )
						StyxWoW.Sleep(50);
				}

				SpellManager.Cast("Ghost Wolf");

				if (hasCastTime)
				{
					int ghostWolfStart = Environment.TickCount;
					while (!me.HasAura("Ghost Wolf") && !me.Combat && Environment.TickCount - ghostWolfStart < 3500)
						StyxWoW.Sleep(150);
				}
				return true;
			}
			if (SpellManager.HasSpell("Travel Form"))
			{
				Logging.Write("Mounting: Using Travel Form since we don't have any mounts yet.");
				SpellManager.Cast("Travel Form");
				return true;
			}
			return false;
		}

		private static int GetMountIndex(string mountName)
		{
			// Use Lua to find mount index
			string luaCode = string.Format(@"
				local mountName = string.lower('{0}')
				for i = 1, GetNumCompanions('MOUNT') do
					local _, name, id = GetCompanionInfo('MOUNT', i)
					if string.lower(name) == mountName or tostring(id) == mountName then
						return i
					end
				end
				return 0
			", mountName.Replace("'", "\\'"));

			string result = Lua.GetReturnVal<string>(luaCode, 0);
			if (int.TryParse(result, out int index))
			{
				return index;
			}
			return 0;
		}

		public static bool CanMount()
		{
			LocalPlayer? me = Me;
			if (me == null)
				return false;

			// Check if player can use mounts at all
			// WotLK: Ground mounts at level 20 (except Paladin/Warlock at 20)
			int requiredLevel = 20;
			if (me.Level < requiredLevel)
				return false;

			// Check if player has any mounts available
			if (MountHelper.NumMounts <= 0)
				return false;

			if (!_combatTimer.IsFinished)
				return false;

			if (!_mountTimer.IsFinished)
				return false;

			if (me.Dead || me.IsGhost)
				return false;

			WoWPoint location = me.Location;

			// Check if we're in a known "can't mount" spot
			foreach (WoWPoint spot in _cantMountSpots)
			{
				if (location.Distance(spot) < 10f)
					return false;
			}

			// Transient states: combat, swimming, indoors — don't permanently blacklist the location.
			// Cant-mount spots are only for permanent geometry (low ceiling), not transient conditions.
			if (!me.IsOutdoors || me.IsSwimming || me.Combat)
				return false;

			// HB 4.3.4 ceiling raycast — permanent geometry: low ceiling blocks mount.
			float boundingHeight = me.BoundingHeight;
			WoWPoint headPos = location + new WoWPoint(0f, 0f, boundingHeight);
			WoWPoint aboveHead = headPos + new WoWPoint(0f, 0f, boundingHeight / 2f);
			if (GameWorld.TraceLine(headPos, aboveHead, GameWorld.CGWorldFrameHitFlags.HitTestLOS))
			{
				AddCantMountSpot(location);
				return false;
			}

			return true;
		}

		public static bool IsOutdoors
		{
			get
			{
				LocalPlayer? me = Me;
				return me?.IsOutdoors ?? false;
			}
		}

		public static void AddCantMountSpot(WoWPoint location)
		{
			if (!_cantMountSpots.Contains(location))
			{
				_cantMountSpots.Add(location);
				Logging.Write(System.Drawing.Color.Red, "Blacklisted mount spot at: {0}", location);
			}
		}

		/// <summary>
		/// Returns true if <paramref name="location"/> is within 10y of a known can't-mount spot.
		/// Ported from HB 6.2.3 Mount.smethod_6.
		/// </summary>
		internal static bool IsInCantMountSpot(WoWPoint location)
		{
			return _cantMountSpots.Any(spot => spot.Distance(location) < 10f);
		}

		public static void ClearCantMountSpots()
		{
			_cantMountSpots.Clear();
		}

		/// <summary>
		/// Removes all cant-mount spots within <paramref name="radius"/> yards of <paramref name="center"/>.
		/// Call after a successful mount or harvest to clean up stale entries.
		/// </summary>
		public static void RemoveCantMountSpotsNear(WoWPoint center, float radius)
		{
			_cantMountSpots.RemoveAll(spot => spot.Distance(center) < radius);
		}

		[Obsolete("StateMount(LocationRetriever) should be used.")]
		public static void StateMount()
		{
			StateMount(static () => WoWPoint.Empty);
		}

		public static void StateMount(LocationRetriever travelingTo)
		{
			if (!LevelbotSettings.Instance.UseMount || Me?.Mounted == true || !CanMount())
				return;

			MountUp(travelingTo);
		}

		public static void MountUp(LocationRetriever travelingTo)
		{
			_currentDestinationRetriever = travelingTo;
			MountUp(() =>
			{
				WoWUnit? firstUnit = Targeting.Instance.FirstUnit;
				if (firstUnit != null && firstUnit.Distance < MountDistance)
					return false;

				return true;
			});
		}

		public static bool ShouldMount(WoWPoint travelingTo)
		{
			LocalPlayer? me = Me;
			if (me == null)
				return false;

			if (me.Mounted)
				return false;

			if (Battlegrounds.IsInsideBattleground || me.IsInInstance)
				return true;

			float distanceSqr = me.Location.DistanceSqr(travelingTo);
			float mountDistanceSqr = MountDistance * MountDistance;

			return distanceSqr >= mountDistanceSqr;
		}

		/// <summary>
		/// Check if we should dismount for a given destination.
		/// Ported from HB 4.3.4.
		/// </summary>
		public static bool ShouldDismount(WoWPoint travelingTo)
		{
			LocalPlayer? me = Me;
			if (me == null)
				return false;

			// A dismount is already in flight — me.Mounted just hasn't caught up. Answering true here re-logs
			// the reason ("Dismount to kill bot poi." ×3) and re-triggers the callers every tick of the window.
			if (DismountPending)
				return false;

			// Ghost Wolf/Travel Form travelers fall out here (not "mounted") — deliberate, NOT a gap: the
			// client auto-cancels those forms on the first spellcast, exactly like auto-dismount, so the
			// kill-POI path needs no shift-out logic. Don't add one.
			if (!me.Mounted)
				return false;

			// Dismount if in combat and not moving
			if (me.Combat && !me.IsMoving)
			{
				Logging.WriteDebug("Dismount for attacker.");
				return true;
			}

			if (travelingTo == WoWPoint.Empty)
				return false;

			WoWPoint location = me.Location;
			float distance = location.Distance(travelingTo);

			// NO explicit dismount for pulls (the old Kill-POI ≤PullDistance and hotspot-with-target ≤100yd
			// branches) — WotLK auto-dismounts on the first spellcast, so like a real player the bot rides all
			// the way to cast range and the PULL CAST itself dismounts it (the hotspot branch even dropped us
			// 100yd out and walked the rest). The combat-attacker branch above stays: jumped while mounted and
			// stationary, with nothing castable (OOM melee fallback), the cast path may never fire — that one
			// needs the button. Interaction dismounts below stay too (can't loot/vendor mounted).

			// Dismount for interacting with objects/NPCs
			if (BotPoi.Current.Type == PoiType.Loot || 
				BotPoi.Current.Type == PoiType.Skin ||
				BotPoi.Current.Type == PoiType.Harvest ||
				BotPoi.Current.Type == PoiType.Sell ||
				BotPoi.Current.Type == PoiType.Repair ||
				BotPoi.Current.Type == PoiType.Train ||
				BotPoi.Current.Type == PoiType.Mail)
			{
				if (distance <= 10f)
				{
					Logging.WriteDebug("Dismount for interaction.");
					return true;
				}
			}

			return false;
		}

		/// <summary>
		/// Pulses mount state and fires events (call from main bot pulse).
		/// </summary>
		public static void Pulse()
		{
			var me = Me;
			if (me == null) return;

			bool isMounted = me.Mounted;

			if (isMounted && !_wasMounted)
			{
				// Just mounted — fire event and check Cancel flag (HB 6.2.3 pattern)
				var args = new MountUpEventArgs(me.IsFlying, "Mount");
				args.Destination = _currentDestinationRetriever?.Invoke() ?? WoWPoint.Empty;
				OnMountUp?.Invoke(null, args);
				if (args.Cancel)
				{
					Logging.WriteDebug("Mount-up cancelled by event handler");
					Dismount("cancelled by event handler");
					_wasMounted = false;
					return;
				}
			}
			else if (!isMounted && _wasMounted)
			{
				// Just dismounted
				RaiseOnDismount(string.Empty);
			}

			_wasMounted = isMounted;
		}

		public static float MountDistance => (float)LevelbotSettings.Instance.MountDistance;

		public delegate bool CanMountDelegate();
	}
}
