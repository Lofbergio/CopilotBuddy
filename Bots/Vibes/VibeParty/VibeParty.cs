using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Bots.Grind;
using Bots.Vibes.Shared;
using Bots.VibeGrinder;
using CommonBehaviors;
using CommonBehaviors.Actions;
using CommonBehaviors.Decorators;
using PartyBot.IPC;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.Logic.Questing;
using Styx.Plugins;
using Styx.RemotableObjects;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace VibeParty
{
	// Forked from DiscoBot (the HB-port "Party Bot") 2026-07-07 as the Vibes-family party/coordination
	// botbase. Shares the PartyBot transport (RemotingServer/Client, BotMessage, LeaderPlugin) — that's
	// plumbing, not botbase. Behaviour is currently identical to DiscoBot; coordination phases build in
	// here (see PARTY-COORDINATION-DESIGN.md), leaving the DiscoBot port frozen. Copy, don't rewrite.
	public class VibeParty : BotBase
	{
		// ──────────────────────────────────────────────────────────────────────
		// BotBase overrides
		// ──────────────────────────────────────────────────────────────────────

		public override string Name => "VibeParty";

		public override Composite Root
		{
			get
			{
				if (_root != null) return _root;
				_root = new PrioritySelector(
					// Absolutely idle — do nothing at all (no buffing either). A wait-HOLD yields to
					// combat: a held follower still defends itself, then re-holds when the fight ends.
					new Decorator(ctx => VibePartySettings.Instance.DoNothing || (_waiting && !StyxWoW.Me.Combat), new ActionIdle()),
					// Leader: you drive manually, but the routine keeps buffs up on self + party. Buff upkeep runs
					// OUT OF COMBAT only (so we never touch your current target mid-fight), then idle — no
					// follow / combat / loot. Do Nothing above takes precedence for a truly dead bot.
					new Decorator(ctx => VibePartySettings.Instance.IsLeader,
						new PrioritySelector(
							CreateLeaderBuffBehavior(),
							new ActionIdle()
						)),
					CreateDeathBehavior(),
					CreateRezBehavior(),                              // assigned rezzer: position at the corpse, then the routine casts
					CreateCombatBehavior(),                           // smethod_5
					CreateEventBehavior(),                            // method_5
					CreateLootBehavior(),                             // method_3
					CreateTurnInBehavior(),                           // Phase 2 — turn in at the leader's NPC (creature enders)
					CreateGoVisitBehavior(),                          // Phase 2 — turn in / pick up at a GAME OBJECT (corpses/altars/barrels)
					CreateUseQuestStarterBehavior(),                  // Phase 1: use drop-only quest-starter items in bags
					CreateWaterServiceBehavior(),                     // Mage: conjure + hand out water on request (downtime)
					new Decorator(ctx => !StyxWoW.Me.IsInInstance && !Battlegrounds.IsInsideBattleground, LevelBot.CreateVendorBehavior()),
					CreateFollowBehavior()                            // smethod_0
				);
				return _root;
			}
		}

		public override PulseFlags PulseFlags => PulseFlags.All;

		public override Form ConfigurationForm => new Forms.VibePartyConfig();

		// ──────────────────────────────────────────────────────────────────────
		// Start / Stop
		// ──────────────────────────────────────────────────────────────────────

		public override void Start()
		{
			// Absolutely idle: do nothing at all (no leader broadcast, no buffing). Checked FIRST so it takes
			// precedence over Leader mode.
			if (VibePartySettings.Instance.DoNothing)
				return;

			// Leader mode — you drive manually; we host the party hub and drive it ourselves (publish Command
			// each Pulse, aggregate follower Progress). Replaces the legacy LeaderPlugin/RemotingServer for
			// VibeParty (DiscoBot keeps that one); disable LeaderPlugin so :1337 and our :1338 don't both run.
			if (VibePartySettings.Instance.IsLeader)
			{
				_leaderPanelShown = false;   // re-show (or re-create after a client relog) the panel on each bot start
				DisableLeaderPluginIfEnabled();
				if (_bus == null)
				{
					try { _bus = new PartyBus(isLeader: true, StyxWoW.Me.Guid, StyxWoW.Me.Name); }
					catch (Exception ex)
					{
						Logging.Write(System.Drawing.Color.Red,
							"[VibeParty] Could not start the party hub on port {0} (already in use? role-switch race?): {1}",
							PartyBus.Port, ex.Message);
						return;
					}
				}
				_bus.Subscribe("Progress", OnProgressReceived);
				_bus.Subscribe("Alert", OnAlertReceived);   // follower alerts → panel strip + OOM raid warning
				_bus.Subscribe("WaterStatus", OnWaterStatus);   // mage service state → the panel's water line
				_partyLoot = new PartyLoot(_bus, isLeader: true);   // Phase 5: the lease broker
				// The leader drinks too: requester-side PartyWater (ask when out, hear WaterKind, purge stale).
				// MUST be created BEFORE the relay below — Subscribe is last-wins per type and the relay must
				// own the WaterRequest slot (the leader never serves, so losing OnWaterRequest costs nothing).
				if (_partyWater == null)
					_partyWater = new PartyWater(_bus);
				// Relay follower→follower water requests: a requester's WaterRequest reaches us (target 0); re-
				// broadcast it so the mage follower(s) hear it. (Targeted WaterOffers are auto-forwarded by the
				// bus; our OWN requests broadcast directly — Publish never loops back into this relay.)
				_bus.Subscribe("WaterRequest", m => _bus!.Publish("WaterRequest", m.Payload));

				// Phase 1: auto-share each quest we accept with the party (no-ops solo/unshareable).
				if (!_leaderHooked)
				{
					Lua.Events.AttachEvent("QUEST_ACCEPTED", new LuaEventHandlerDelegate(OnQuestAcceptedShare));
					// Abandon sync: diff our log on every change (turn-in vs abandon resolved in Pulse).
					Lua.Events.AttachEvent("QUEST_LOG_UPDATE", new LuaEventHandlerDelegate(OnLeaderQuestLogUpdate));
					Lua.Events.AttachEvent("QUEST_COMPLETE", new LuaEventHandlerDelegate(OnLeaderQuestComplete));
					// Water arrives by trade — the leader gets the WATER-ONLY auto-accept (see OnTradeShouldAccept).
					Lua.Events.AttachEvent("TRADE_SHOW",                new LuaEventHandlerDelegate(OnTradeShouldAccept));
					Lua.Events.AttachEvent("TRADE_ACCEPT_UPDATE",       new LuaEventHandlerDelegate(OnTradeShouldAccept));
					Lua.Events.AttachEvent("TRADE_TARGET_ITEM_CHANGED", new LuaEventHandlerDelegate(OnTradeShouldAccept));
					Lua.Events.AttachEvent("TRADE_MONEY_CHANGED",       new LuaEventHandlerDelegate(OnTradeShouldAccept));
					_leaderHooked = true;
				}
				return;
			}

			// Follower: connect to the leader's hub (:1338) and mirror. The client reconnects on its own, so
			// starting before the leader is up is fine — it just retries (fail degraded, never blocks).
			DisableLeaderPluginIfEnabled();
			EnsurePartyProfile();
			FollowerQuestLedger.EnsureLoaded();   // quest knowledge for id-driven visits (loud if missing)
			if (_bus == null)
				_bus = new PartyBus(isLeader: false, StyxWoW.Me.Guid, StyxWoW.Me.Name);
			_alertBus = _bus;   // static mirror — alert publishers sit in static methods
			if (_partyLoot == null)
				_partyLoot = new PartyLoot(_bus, isLeader: false);   // Phase 5: the lease client
			if (_partyWater == null)
				_partyWater = new PartyWater(_bus);                  // water service (requester + mage)

			if (!_hooked)
			{
				_bus.Subscribe("Command", OnCommandReceived);
				_bus.Subscribe("LeaderPickup", OnLeaderPickup);   // durable "I took a quest HERE" records
				_bus.Subscribe("QuestAbandon", OnQuestAbandonMsg);   // leader dropped a quest — drop + block it
				_bus.Subscribe("QuestAccept", OnQuestAcceptMsg);     // leader accepted/re-accepted — whitelist + lift block
				_bus.Subscribe("LeaderQuests", OnLeaderQuestsMsg);   // leader's log snapshot — seed the accept whitelist
				_bus.Subscribe("RezAssign", OnRezAssign);            // leader assigned a rezzer to a corpse
				_bus.Subscribe("RezRelease", OnRezRelease);          // no rezzer available — release + corpse-run
				_bus.Subscribe("Order", OnOrderReceived);            // leader panel orders (vendor/hearth/…)
				AttachLuaEvents();
				Vendors.OnVendorItems += OnVendorSweep;   // disposition: only true junk sells
				Vendors.OnMailItems += OnMailSweep;       // disposition: valuables queue for the bank
				LootTargeting.Instance.IncludeTargetsFilter += LevelBot.LevelbotIncludeLootsFilter;
				LootTargeting.Instance.IncludeTargetsFilter += IncludeQuestSparkles;   // Goober-type quest objects (LevelBot only admits chests)
				LootTargeting.Instance.IncludeTargetsFilter += PruneDangerousCollectibles;
				LootTargeting.Instance.IncludeTargetsFilter += PartyLootFilter;   // Phase 5: lease-gate collectibles
				Targeting.Instance.IncludeTargetsFilter += new IncludeTargetsFilterDelegate(IncludeTargetsFilter);
				Targeting.Instance.WeighTargetsFilter += new WeighTargetsDelegate(WeighTargetsFilter);
				WoWChat.Party += OnPartyChat;
				WoWChat.PartyLeader += OnPartyChat;
				_hooked = true;
			}
		}

		public override void Stop()
		{
			if (_leaderHooked)
			{
				Lua.Events.DetachEvent("QUEST_ACCEPTED", new LuaEventHandlerDelegate(OnQuestAcceptedShare));
				Lua.Events.DetachEvent("QUEST_LOG_UPDATE", new LuaEventHandlerDelegate(OnLeaderQuestLogUpdate));
				Lua.Events.DetachEvent("QUEST_COMPLETE", new LuaEventHandlerDelegate(OnLeaderQuestComplete));
				Lua.Events.DetachEvent("TRADE_SHOW",                new LuaEventHandlerDelegate(OnTradeShouldAccept));
				Lua.Events.DetachEvent("TRADE_ACCEPT_UPDATE",       new LuaEventHandlerDelegate(OnTradeShouldAccept));
				Lua.Events.DetachEvent("TRADE_TARGET_ITEM_CHANGED", new LuaEventHandlerDelegate(OnTradeShouldAccept));
				Lua.Events.DetachEvent("TRADE_MONEY_CHANGED",       new LuaEventHandlerDelegate(OnTradeShouldAccept));
				_leaderHooked = false;
			}

			_bus?.Stop();
			_bus = null;
			_alertBus = null;
			_partyLoot = null;
			_partyWater = null;

			if (VibePartySettings.Instance.DoNothing || VibePartySettings.Instance.IsLeader)
				return;

			// Restore the vendor auto-resolve flag we forced in EnsurePartyProfile — a profile-driven
			// botbase run after us may rely on its authored <Vendor> entries only.
			if (_origFindVendors.HasValue)
			{
				CharacterSettings.Instance.FindVendorsAutomatically = _origFindVendors.Value;
				_origFindVendors = null;
			}

			DetachLuaEvents();
			Vendors.OnVendorItems -= OnVendorSweep;
			Vendors.OnMailItems -= OnMailSweep;
			LootTargeting.Instance.IncludeTargetsFilter -= LevelBot.LevelbotIncludeLootsFilter;
			LootTargeting.Instance.IncludeTargetsFilter -= IncludeQuestSparkles;
			LootTargeting.Instance.IncludeTargetsFilter -= PruneDangerousCollectibles;
			LootTargeting.Instance.IncludeTargetsFilter -= PartyLootFilter;
			Targeting.Instance.IncludeTargetsFilter -= new IncludeTargetsFilterDelegate(IncludeTargetsFilter);
			Targeting.Instance.WeighTargetsFilter -= new WeighTargetsDelegate(WeighTargetsFilter);
			WoWChat.Party -= OnPartyChat;
			WoWChat.PartyLeader -= OnPartyChat;
			_hooked = false;
		}

		// ──────────────────────────────────────────────────────────────────────
		// LFG teleport mirror — followers match the leader's inside/outside state
		// ──────────────────────────────────────────────────────────────────────

		// The leader porting OUT of an LFG dungeon (rest/train/vendor break) should pull the party out;
		// porting back IN should pull them in. ⚠ GHOST GUARD: a dead leader releasing reads exactly like
		// "leader left the instance" — a corpse run must NOT scatter the party out, so the mirror stands
		// down entirely while LeaderGhost (and while WE are dead/ghost or in combat). STABILITY: the
		// mismatch must hold ~8s before acting (zoning/loading screens flap both flags); vendor-errand
		// POIs defer the port-in so a follower finishes selling/training first.
		private static DateTime _teleportMismatchSince = DateTime.MinValue;
		private static DateTime _teleportActedAt = DateTime.MinValue;

		private static void MirrorLeaderTeleportTick()
		{
			if (_botMessage == null) return;
			var me = StyxWoW.Me;
			if (me == null || !me.IsValid) return;

			bool mismatch = me.IsInInstance != _botMessage.LeaderInInstance;
			if (!mismatch || _botMessage.LeaderGhost || me.Dead || me.IsGhost || me.Combat)
			{
				_teleportMismatchSince = DateTime.MinValue;
				return;
			}
			if (_teleportMismatchSince == DateTime.MinValue) { _teleportMismatchSince = DateTime.Now; return; }
			if ((DateTime.Now - _teleportMismatchSince).TotalSeconds < 8) return;
			if ((DateTime.Now - _teleportActedAt).TotalSeconds < 20) return;   // one attempt per window

			if (me.IsInInstance)
			{
				Logging.Write("VibeParty: leader left the dungeon — teleporting out.");
				Lua.DoString("LFGTeleport(1)");
			}
			else
			{
				// Finish an in-flight vendor errand before porting back in.
				PoiType poi = BotPoi.Current.Type;
				if (poi == PoiType.Sell || poi == PoiType.Repair || poi == PoiType.Buy
					|| poi == PoiType.Train || poi == PoiType.Mail)
					return;
				Logging.Write("VibeParty: leader is in the dungeon — teleporting in.");
				Lua.DoString("LFGTeleport(0)");
			}
			_teleportActedAt = DateTime.Now;
			_teleportMismatchSince = DateTime.MinValue;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Hearth sync — the leader hearthing means "the party goes home"
		// ──────────────────────────────────────────────────────────────────────

		private const int HearthstoneItemId = 6948;
		// Hearthstone + Astral Recall (a shaman leader recalling still means "go home").
		private static bool IsHearthCast(int spellId) => spellId == 8690 || spellId == 556;

		private static bool _leaderWasHearthing;                              // edge detect on the leader's cast
		private static DateTime _hearthPendingUntil = DateTime.MinValue;      // keep trying inside this window (combat/interrupt can defer a cast)
		private static bool _hearthAttempted;                                 // this window used the stone (CD > 0 then = success, not "was already down")
		private static bool _hearthFromLeaderCast;                            // window armed by the leader's cast (abort-mirror applies) vs !hearth (it must not)

		// Edge-triggered on the leader's hearth cast, then a pending WINDOW rather than a one-shot:
		// a follower mid-fight (or whose 10s cast gets interrupted) retries until the window closes.
		// The stone's cooldown only starts on SUCCESS, so "attempted + now on cooldown" is the landed
		// signal. ABORT MIRROR: our cast is up but the leader's is gone AND the leader still stands
		// here (a real hearth removes it from the object manager) — cancel ours; hearthing without
		// the leader strands the follower at the inn with no way back to the party.
		private static void MirrorLeaderHearthTick()
		{
			if (_botMessage == null) return;
			var me = StyxWoW.Me;
			if (me == null || !me.IsValid) return;

			bool leaderHearthing = IsHearthCast(_botMessage.LeaderCastingSpellId);
			if (leaderHearthing && !_leaderWasHearthing)
			{
				_hearthPendingUntil = DateTime.UtcNow.AddSeconds(30);
				_hearthAttempted = false;
				_hearthFromLeaderCast = true;
				Logging.Write("[VibeParty] leader is hearthing — following home.");
			}
			_leaderWasHearthing = leaderHearthing;

			if (IsHearthCast(me.CastingSpellId))
			{
				// Abort-mirror only for leader-cast-armed windows — a !hearth order must complete
				// even though the leader never casts.
				if (!leaderHearthing && _hearthFromLeaderCast)
				{
					WoWPlayer? leader = ObjectManager.GetObjectByGuid<WoWPlayer>(_botMessage.LeaderGuid);
					if (leader != null && leader.Distance < 60)
					{
						Logging.Write("[VibeParty] leader cancelled its hearth — cancelling ours.");
						Lua.DoString("SpellStopCasting()");
						_hearthPendingUntil = DateTime.MinValue;
					}
				}
				return;   // cast in flight — FollowLeader's cast-hold keeps us planted
			}

			HearthWindowTick();
		}

		// The armed hearth window, executed. Split out of the mirror because it is the UNIVERSAL half:
		// the leader has no leader to mirror, but a #hearth order arms the same window on it and the
		// same retry rules apply (deferred by combat/cast, landed = attempted + stone on cooldown).
		private static void HearthWindowTick()
		{
			var me = StyxWoW.Me;
			if (me == null || !me.IsValid) return;
			if (IsHearthCast(me.CastingSpellId)) return;   // our cast is already in flight
			if (DateTime.UtcNow >= _hearthPendingUntil) return;
			if (me.Dead || me.IsGhost || me.Combat || me.IsCasting) return;   // window retries once these clear

			WoWItem? hearth = me.BagItems?.FirstOrDefault(i => i != null && i.Entry == HearthstoneItemId);
			if (hearth == null)
			{
				Logging.Write("[VibeParty] hearth sync: no Hearthstone in bags — staying put.");
				PublishAlert(false, "cannot hearth - no Hearthstone in bags");
				_hearthPendingUntil = DateTime.MinValue;
				return;
			}
			if (hearth.CooldownTimeLeft > TimeSpan.Zero)
			{
				if (!_hearthAttempted)
				{
					Logging.Write("[VibeParty] hearth sync: Hearthstone on cooldown ({0:F0}m) — staying put.",
						hearth.CooldownTimeLeft.TotalMinutes);
					PublishAlert(false, string.Format("cannot hearth - stone on cooldown ({0:F0}m)", hearth.CooldownTimeLeft.TotalMinutes));
				}
				_hearthPendingUntil = DateTime.MinValue;   // attempted + on CD = it landed; otherwise we can't go
				return;
			}
			if (me.IsMoving) WoWMovement.MoveStop();
			Logging.Write("[VibeParty] hearth sync: using Hearthstone.");
			hearth.Use();
			_hearthAttempted = true;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Synthetic profile — vendors/trainers resolve from data.bin
		// ──────────────────────────────────────────────────────────────────────

		// VibeParty is profile-less, and the whole vendor tree hangs off ProfileManager.CurrentProfile —
		// a null profile made NeedToSell/Repair/Train/Buy permanently "no vendor known", so followers
		// never trained (the class trainer can be 30yd away) and the hunter ammo restock could never
		// fire. Same pattern as VibeGrinder's GrindAreaSynthesizer: an empty-vendor profile +
		// FindVendorsAutomatically makes GetClosestVendor fall through to data.bin. The sell mask is
		// WIDE (grey→blue, never purple) because the shared ItemDisposition sweeps protect everything
		// that isn't true junk and queue the valuables for mail — identical policy (and the same
		// VibeGrinderSettings "Loot" knobs) as VibeGrinder, so one loot policy governs both botbases.
		private static Profile? _syntheticProfile;
		private static bool? _origFindVendors;
		private static readonly MailboxService _mailboxes = new MailboxService();
		private static uint _mailboxMap = uint.MaxValue;

		private static void EnsurePartyProfile()
		{
			if (_syntheticProfile == null)
			{
				var xml = new XElement("HBProfile",
					new XElement("MinFreeBagSlots", 2),
					new XElement("MinDurability", "0.35"),
					new XElement("SellGrey", true),
					new XElement("SellWhite", true),
					new XElement("SellGreen", true),
					new XElement("SellBlue", true),
					new XElement("SellPurple", false),
					new XElement("MailGrey", false),
					new XElement("MailWhite", false),
					new XElement("MailGreen", false),
					new XElement("MailBlue", false),
					new XElement("MailPurple", false));
				_syntheticProfile = new Profile(xml, null) { Name = "VibeParty (synthetic)" };
			}
			_origFindVendors ??= CharacterSettings.Instance.FindVendorsAutomatically;
			CharacterSettings.Instance.FindVendorsAutomatically = true;
			ProfileManager.UseSyntheticProfile(_syntheticProfile);
			LoadMailboxesIfMapChanged();
		}

		// Feed the map's faction-safe mailboxes into the synthetic profile so the mail run (which
		// piggybacks on sell/repair visits when MailRecipient is set) can find one. No toggle: an
		// unset MailRecipient already means "no mailing", and with no mailbox the Mail items just
		// stay in bags (the safe failure).
		private static void LoadMailboxesIfMapChanged()
		{
			// MapId is event-written and can read -1 (= uint.MaxValue) in some states — skip those ticks.
			uint map = StyxWoW.Me?.MapId is uint m ? m : uint.MaxValue;
			if (map == uint.MaxValue || map == _mailboxMap) return;
			var mgr = ProfileManager.CurrentProfile?.MailboxManager;
			if (mgr == null) return;
			mgr.ForcedMailboxes = _mailboxes.LoadSafeMailboxes(map);
			_mailboxMap = map;
		}

		// Disposition sweeps — thin mirrors of VibeGrinder's (same shared ItemDisposition classifier;
		// see VibeGrinder/Loot/CLAUDE.md for the policy table). Sell hook protects every bag item whose
		// disposition isn't Vendor (the wide mask then only sells true junk); mail hook queues the Mail
		// items. Fail-safe: an item that can't classify is protected.
		private static void OnVendorSweep(SellItemsEventArgs args)
		{
			var me = StyxWoW.Me;
			if (me == null) return;
			int protectedCount = 0, willMail = 0;
			foreach (WoWItem item in me.BagItems)   // BagItems, never CarriedItems (equipped gear)
			{
				if (item == null) continue;
				DispositionAction action;
				try { action = ItemDisposition.Classify(item); }
				catch { action = DispositionAction.Keep; }
				if (action != DispositionAction.Vendor && !args.IdExceptions.Contains(item.Entry))
				{
					args.IdExceptions.Add(item.Entry);
					protectedCount++;
					if (action == DispositionAction.Mail) willMail++;
				}
			}
			if (protectedCount > 0)
				Logging.Write("[VibeParty] Vendor sweep: protecting {0} item(s) from sale ({1} queued to mail, rest kept).",
					protectedCount, willMail);
		}

		private static void OnMailSweep(MailItemsEventArgs args)
		{
			var me = StyxWoW.Me;
			if (me == null) return;
			int queued = 0;
			foreach (WoWItem item in me.BagItems)
			{
				if (item == null) continue;
				DispositionAction action;
				try { action = ItemDisposition.Classify(item); }
				catch { continue; }
				if (action == DispositionAction.Mail && !args.AdditionalItems.Contains(item))
				{
					args.AdditionalItems.Add(item);
					queued++;
				}
			}
			if (queued > 0)
				Logging.Write("[VibeParty] Mail run: queuing {0} valuable item(s) for the bank.", queued);
		}

		// ──────────────────────────────────────────────────────────────────────
		// LeaderLocation property
		// ──────────────────────────────────────────────────────────────────────

		public static WoWPoint LeaderLocation
		{
			get
			{
				if (_botMessage == null) return WoWPoint.Zero;
				WoWPoint pt = new WoWPoint(_botMessage.LeaderX, _botMessage.LeaderY, _botMessage.LeaderZ);
				if (pt.Distance(StyxWoW.Me.Location) < 50f || !_waitTimer0.IsFinished)
					return pt;
				_waitTimer0.Reset();
				return pt;
			}
		}

		// ──────────────────────────────────────────────────────────────────────
		// BotMessage handler — smethod_1
		// ──────────────────────────────────────────────────────────────────────

		// Command (leader→follower): the payload is a serialized BotMessage — the SAME command the legacy
		// broadcast carried, so all downstream follow/assist/turn-in logic is untouched (it reads _botMessage).
		// Fires on a hub thread → pure data only (assign the field), never a game-thread call.
		private static void OnCommandReceived(PartyMessage msg)
		{
			BotMessage? cmd;
			try { cmd = System.Text.Json.JsonSerializer.Deserialize<BotMessage>(msg.Payload, _cmdJson); }
			catch { return; }
			if (cmd == null) return;
			_botMessage = cmd;
			RaFHelper.SetLeader(cmd.LeaderGuid);
		}

		// Alert (follower→leader, hub thread — pure data only): queue raw payloads; the bot thread
		// drains them (log + panel strip; critical → raid warning + sound, un-missable by design).
		private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _alertQueue =
			new System.Collections.Concurrent.ConcurrentQueue<string>();
		private static readonly List<(DateTime At, string Name, string Text, bool Crit)> _alerts =
			new List<(DateTime, string, string, bool)>();

		private void OnAlertReceived(PartyMessage msg)
		{
			if (!string.IsNullOrEmpty(msg.Payload)) _alertQueue.Enqueue(msg.Payload);
		}

		private static void AlertDrainTick()
		{
			while (_alertQueue.TryDequeue(out string? raw))
			{
				string[] parts = raw.Split(new[] { '|' }, 3);
				if (parts.Length < 3) continue;
				bool crit = parts[0] == "1";
				_alerts.Add((DateTime.Now, parts[1], parts[2], crit));
				if (_alerts.Count > 8) _alerts.RemoveAt(0);
				Logging.Write(crit ? System.Drawing.Color.Red : System.Drawing.Color.Khaki,
					"[VibeParty] ALERT {0}: {1}", parts[1], parts[2]);
				// Critical (OOM): raid-warning banner + sound, independent of the panel being open.
				if (crit)
					Lua.DoString("RaidNotice_AddMessage(RaidWarningFrame, '" + EscLua(parts[1].ToUpper() + " IS " + parts[2].ToUpper())
						+ "', ChatTypeInfo['RAID_WARNING']) PlaySound('RaidWarning')");
			}
		}

		// WaterStatus (mage→leader, hub thread — pure data only): last-heard service state for the
		// Main-tab water line ("<have>/<target>|<guid>,…" — see PartyWater.PublishStatus).
		private static string? _waterStatusRaw;
		private static string? _waterMageName;

		private void OnWaterStatus(PartyMessage msg)
		{
			_waterMageName = msg.SenderName;
			_waterStatusRaw = msg.Payload;
		}

		// Everything pushed into the panel lands inside single-quoted Lua literals.
		private static string EscLua(string s) => (s ?? "").Replace("\\", "").Replace("'", "\\'");

		// Progress (follower→leader): store raw per-quest completion; aggregate later on the bot thread. Pure data.
		private void OnProgressReceived(PartyMessage msg)
		{
			MemberProgress mp = new MemberProgress { Name = msg.SenderName, LastUtcTicks = DateTime.UtcNow.Ticks };
			foreach (string tok in (msg.Payload ?? "").Split(';'))
			{
				if (tok.Length == 0) continue;
				if (tok.StartsWith("#rez:"))   // rez census header — "#rez:<0|1>:<role>"
				{
					string[] f = tok.Substring(5).Split(':');
					if (f.Length >= 2) { mp.CanRez = f[0] == "1"; mp.Role = f[1]; }
					continue;
				}
				if (tok.StartsWith("#bags:"))   // free bag slots — the usual reason a follower stalls
				{
					if (int.TryParse(tok.Substring(6), out int free)) mp.FreeBags = free;
					continue;
				}
				if (tok.StartsWith("#errand:"))   // self-directed vendor run in progress
				{
					mp.Errand = tok.Substring(8);
					continue;
				}
				int c = tok.IndexOf(':');
				if (c <= 0) continue;
				if (uint.TryParse(tok.Substring(0, c), out uint qid) && qid != 0)
					mp.QuestComplete[qid] = tok.Substring(c + 1) == "1";
			}
			_partyProgress[msg.SenderGuid] = mp;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Bus pump (bot thread) — publish Command (leader) / Progress (follower); leader visibility
		// ──────────────────────────────────────────────────────────────────────

		public override void Pulse()
		{
			PartyBus? bus = _bus;
			if (bus == null || VibePartySettings.Instance.DoNothing) return;

			_partyLoot?.Tick();   // Phase 5: leader TTL sweep / follower lease upkeep

			if (VibePartySettings.Instance.IsLeader)
			{
				// Broadcast command state at ~the legacy 76ms cadence — coalesced, since followers only need the
				// latest pos/target/combat, not one message per pulse (keeps the outbound queue tiny).
				if ((DateTime.Now - _cmdPublishAt).TotalMilliseconds >= 100)
				{
					_cmdPublishAt = DateTime.Now;
					bus.Publish("Command", System.Text.Json.JsonSerializer.Serialize(BuildLeaderCommand(), _cmdJson));
				}
				// Phase 3 visibility: log who's behind on the leader's quests (throttled, change-only).
				if ((DateTime.Now - _progressReviewAt).TotalSeconds >= 3)
				{
					_progressReviewAt = DateTime.Now;
					ReviewPartyProgress();
				}
				AutoInviteTick();   // party-form: invite every live bus member that isn't grouped yet
				_partyWater?.WaterTick();   // requester side only: the leader asks for water when out
				QuestAbandonSyncTick(bus);  // abandoned (NOT completed) quests propagate to the party
				ShowLeaderCommandPanel();     // inject the addon-style control panel once attached + in world
				DrainPanelOrdersTick(bus);    // panel button clicks → PartyBus "Order" messages
				HearthWindowTick();           // the leader's own #hearth order (no mirror — nobody to mirror)
				AlertDrainTick();             // follower alerts: strip data + OOM raid warning (fires even with the panel hidden)
				UpdateLeaderPanelTick();      // push live roster/quests/alerts into the panel (~2s, panel-shown gated)
				if ((DateTime.Now - _rezBrokerAt).TotalSeconds >= 2) { _rezBrokerAt = DateTime.Now; RezBrokerTick(bus); }  // party-rez broker (runs while dead)
			}
			else
			{
				if ((DateTime.Now - _progressReportAt).TotalSeconds >= 5)
				{
					// Follower: report our per-quest completion up (~5s → doubles as the liveness heartbeat).
					_progressReportAt = DateTime.Now;
					ReportProgress(bus);
				}
				ErrandReportTick();         // vendor/repair/train runs are self-directed — tell the leader
				_partyWater?.WaterTick();   // requester: ask for water when low; mage: advertise + clean stale (throttled)
				LoadMailboxesIfMapChanged();   // keep the synthetic profile's mailboxes on the current map
				MirrorLeaderTeleportTick();    // LFG: match the leader's inside/outside-the-dungeon state
				MirrorLeaderBagsTick();        // bag-visibility sync: leader's bags open ⇒ ours open
				MirrorLeaderHearthTick();      // hearth sync: leader hearthing ⇒ we hearth (cancel if it cancels)
				while (_orderQueue.TryDequeue(out string? order)) ExecuteOrder(order);   // leader panel orders
				ProcessAbandonQueue();         // drop quests the leader abandoned (block set stops re-accepts)
			PulseLeaderQuestEdge();       // leader took a NEW quest ⇒ re-open every giver now, don't wait out a throttle
				PulseFollowBreak();            // executes queued /follow-break taps (press one pulse, release next)
				if (StyxWoW.Me.Combat && !_wasInCombat) QueueFollowBreak();   // combat entry: break glue the moment WE are in combat
				_wasInCombat = StyxWoW.Me.Combat;
				RestEntryFollowBreak();        // one backward step on rest entry kills stationary follow glue
				// Party-rez death bookkeeping: stamp death time; clear the wait latches on revive.
				if (StyxWoW.Me.Dead || StyxWoW.Me.IsGhost) { if (_deadSince == DateTime.MinValue) _deadSince = DateTime.UtcNow; }
				else { _deadSince = DateTime.MinValue; _rezReleaseOrdered = false; _resurrectPending = false; _rezGiveUpWhispered = false; _rezHoldLogged = false; }
				// Healer OOM beacon: whisper the leader once when we drop below 10% mana; re-arm above 30%.
				if (MyRole() == "Healer" && StyxWoW.Me.MaxMana > 0)
				{
					double manaPct = StyxWoW.Me.ManaPercent;
					if (manaPct < 10 && !_oomWhispered) { _oomWhispered = true; WhisperLeader("OOM"); Logging.Write(System.Drawing.Color.Khaki, "[VibeParty] healer OOM — whispered leader."); }
					else if (manaPct > 30) _oomWhispered = false;
				}
				// Re-arm the per-fight follow-break one-shot the moment we drop out of combat state.
				if (!IsInCombatState()) _combatEntryStopDone = false;
			}
		}

		// The command state followers mirror. Combat intent travels ONLY as the LeaderTargetGuid /
		// LeaderInCombat fields (the assist tiers read those): the message stays "FollowLeader" through the
		// leader's fights so FollowLeader()'s combat approach/hold keeps driving the follower. The retired
		// "Kill" message routed followers into a separate leader-distance-gated arm that duplicated the
		// assist logic — and left NO active branch for a follower standing outside that gate.
		private static BotMessage BuildLeaderCommand()
		{
			LocalPlayer me = StyxWoW.Me;
			WoWPoint loc = me.Location;
			string type;
			ulong targetGuid = 0;
			PoiType poi = BotPoi.Current.Type;
			if ((poi == PoiType.Repair || poi == PoiType.Sell)
				&& BotPoi.Current.AsObject != null && BotPoi.Current.AsObject.WithinInteractRange)
			{
				type = "Vendor";
				targetGuid = BotPoi.Current.AsObject.Guid;
			}
			else type = "FollowLeader";

			return new BotMessage
			{
				Message = type,
				LeaderX = loc.X, LeaderY = loc.Y, LeaderZ = loc.Z,
				TargetGuid = targetGuid,
				LeaderInInstance = me.IsInInstance,
				LeaderGhost = me.Dead || me.IsGhost,
				LeaderGuid = me.Guid,
				LeaderName = me.Name,
				Timestamp = DateTime.Now,
				LeaderTargetGuid = me.CurrentTargetGuid,
				LeaderInCombat = me.Combat,
				LeaderBagsOpen = LeaderBagsOpenCached(),
				LeaderCastingSpellId = me.CastingSpellId
			};
		}

		// Bag-visibility sync: bag UI state is Lua-only, so poll at 1Hz and cache — the 10Hz command
		// build must stay memory-read cheap.
		private static bool _leaderBagsOpen;
		private static DateTime _leaderBagsCheckedAt = DateTime.MinValue;

		private static bool LeaderBagsOpenCached()
		{
			if ((DateTime.Now - _leaderBagsCheckedAt).TotalSeconds >= 1)
			{
				_leaderBagsCheckedAt = DateTime.Now;
				// Frame truth, addon-aware: this client runs ElvUI, whose bag module REPLACES the stock
				// ContainerFrames (they never show — second dead run), so check its frame first, then
				// the defaults. IsBagOpen() isn't a global here at all (first dead run).
				bool open = Lua.GetReturnVal<int>(
					"local o=0 if ElvUI_ContainerFrame and ElvUI_ContainerFrame:IsShown() then o=1 end " +
					"if o==0 then for i=1,(NUM_CONTAINER_FRAMES or 13) do local f=_G['ContainerFrame'..i] if f and f:IsShown() then o=1 end end end return o", 0) == 1;
				if (open != _leaderBagsOpen)
					Logging.WriteDebug("[VibeParty] leader bags {0} — mirroring to followers.", open ? "OPEN" : "closed");
				_leaderBagsOpen = open;
			}
			return _leaderBagsOpen;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Errand visibility — a follower peeling off to a vendor is SELF-directed
		// ──────────────────────────────────────────────────────────────────────

		// NeedToSell/Repair/Train fire on the follower's own judgement, so the leader gets no say and
		// no notice — the follower just walks off mid-grind. Reported BOTH ways on purpose: as live
		// STATE (heartbeat → the Main tab's errand line, which stands until the errand ends) and as an
		// EDGE (one alert → the dock, visible from every tab). The state line is what makes it
		// un-missable; the alert is what makes the moment visible.
		private static PoiType _errandPoi = PoiType.None;
		private static string _errand = "";

		private static string ErrandWord(PoiType t)
		{
			switch (t)
			{
				case PoiType.Sell:   return "selling";
				case PoiType.Repair: return "repairing";
				case PoiType.Buy:    return "restocking";
				case PoiType.Train:  return "training";
				case PoiType.Mail:   return "mailing";
				default:             return "";
			}
		}

		private static void ErrandReportTick()
		{
			PoiType t = BotPoi.Current.Type;
			string word = ErrandWord(t);
			if (word.Length == 0) t = PoiType.None;   // every non-errand POI is the same "no errand" state
			if (t == _errandPoi) return;
			_errandPoi = t;
			_errand = word;
			if (word.Length == 0) return;             // errand ended — the line clearing says it
			string where = BotPoi.Current.Name ?? "";
			Logging.Write(System.Drawing.Color.Khaki, "[VibeParty] errand: {0}{1}.", word,
				where.Length > 0 ? " at " + where : "");
			PublishAlert(false, where.Length > 0 ? word + " at " + TruncRaw(where, 16) : word);
		}

		// Follower (bot thread): report per-quest completion. Sent even OUTSIDE a party — the report
		// is also the liveness/name beacon AutoInviteTick's roster is built from. Completion comes
		// from the LUA quest log (CompletedQuestsLua) — PlayerQuest.IsCompleted false-positives on
		// in-progress quests (docs/gotchas.md) and painted wrong states onto the leader's readout.
		private static void ReportProgress(PartyBus bus)
		{
			List<PlayerQuest> quests = StyxWoW.Me.QuestLog.GetAllQuests();
			if (quests == null) return;
			var completed = CompletedQuestsLua();
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.Append("#rez:").Append(ComputeCanRezNow() ? '1' : '0').Append(':').Append(MyRole()).Append(';');
			sb.Append("#bags:").Append(StyxWoW.Me.FreeBagSlots).Append(';');
			if (_errand.Length > 0) sb.Append("#errand:").Append(_errand).Append(';');
			foreach (PlayerQuest q in quests)
			{
				if (q == null || q.Id == 0) continue;
				sb.Append(q.Id).Append(':').Append(completed.ContainsKey(q.Id) ? '1' : '0').Append(';');
			}
			bus.Publish("Progress", sb.ToString());
		}

		// ── Party-rez census helpers (each bot reports its own; the leader also computes its own locally,
		//    since PartyBus.Publish never delivers to the sender) ──────────────────────────────────────
		private const int RebirthReagentId = 17034;   // Maple Seed — druid Rebirth reagent (zero-consumable exception)
		private const double RezMinManaPct = 15;

		// The class's resurrection spell, or null for a class that can't rez. Druid's ONLY rez is Rebirth
		// (a 2s battle-rez, castable in AND out of combat); the others are 10s OOC-only casts.
		private static string MyResSpell()
		{
			switch (StyxWoW.Me.Class)
			{
				case WoWClass.Priest:  return "Resurrection";
				case WoWClass.Paladin: return "Redemption";
				case WoWClass.Shaman:  return "Ancestral Spirit";
				case WoWClass.Druid:   return "Rebirth";
				default:               return null;
			}
		}

		// "Available to rez RIGHT NOW": alive, knows the res, and ready — mana for the 10s casters, or the
		// reagent + off-cooldown for a druid's Rebirth (Lua truth, cheap at the 5s heartbeat cadence).
		private static bool ComputeCanRezNow()
		{
			LocalPlayer me = StyxWoW.Me;
			if (me == null || me.Dead || me.IsGhost) return false;
			string res = MyResSpell();
			if (res == null || !SpellManager.HasSpell(res)) return false;
			if (me.Class == WoWClass.Druid)
			{
				return Lua.GetReturnVal<int>(
					"local c = GetItemCount(" + RebirthReagentId + ") or 0 " +
					"local _, d = GetSpellCooldown('Rebirth') " +
					"if c > 0 and (not d or d <= 2) then return 1 else return 0 end", 0U) == 1;
			}
			return me.MaxMana <= 0 || me.ManaPercent >= RezMinManaPct;
		}

		// Reported role intent for rez target priority — the same three-tier resolve as AnswerRoleCheck
		// (config → talent guess), minus the client-ticked LFD tier (no popup open outside a role check).
		private static string MyRole()
		{
			string cfg = (VibePartySettings.Instance.LfgRole ?? "Auto").Trim();
			if (cfg.Equals("Tank", StringComparison.OrdinalIgnoreCase)) return "Tank";
			if (cfg.Equals("Healer", StringComparison.OrdinalIgnoreCase)) return "Healer";
			if (cfg.Equals("Damage", StringComparison.OrdinalIgnoreCase) || cfg.Equals("DPS", StringComparison.OrdinalIgnoreCase)) return "Damage";
			DeriveLfgRole(out bool tank, out bool heal);
			return tank ? "Tank" : heal ? "Healer" : "Damage";
		}

		// ── Party resurrection ──────────────────────────────────────────────────────────────────────
		// VibeParty owns the LOGIC (census/assignment/positioning/whispers); the routine owns the CAST via
		// the neutral Styx.CommonBot.PartyRez static. The broker runs in the LEADER Pulse (even while the
		// leader is dead — Pulse is ungated on alive); execution is a FOLLOWER tree branch (the leader is
		// human-driven and never reaches it). Spec: docs/superpowers/specs/2026-07-14-party-rez-design.md.
		private const float RezRange = 28f;              // just inside the ~30yd res range
		private const double RezAttemptTimeoutSec = 25;  // one rezzer's patience on a target (10s cast + travel + accept)
		private const double RezLatchTimeoutSec = 30;    // leader re-brokers a stuck assignment after this

		private static ulong _brokerTargetGuid;          // the one outstanding assignment (sequential)
		private static ulong _brokerRezzerGuid;
		private static DateTime _brokerAssignedAt;
		private static bool _rezReleaseSent;
		private static DateTime _rezBrokerAt = DateTime.MinValue;

		private static ulong _myRezMemberGuid;           // follower: the dead member I'm assigned to rez
		private static DateTime _myRezStartedAt;
		private static bool _myRezWhispered;
		private static volatile bool _rezReleaseOrdered; // follower death path: leader said release (no rezzer)
		private static bool _oomWhispered;               // healer OOM beacon debounce (re-armed above 30% mana)

		private static int RolePriority(string role) => role == "Tank" ? 0 : role == "Healer" ? 1 : 2;

		// Leader broker: order the dead by role (tank>healer>dps), pick one rezzer (followers first, leader
		// last), assign the highest-priority corpse; in combat only a Rebirth-druid can act, OOC anyone. One
		// assignment outstanding at a time (sequential), resolved when the member is alive or the latch times
		// out. No alive rezzer anywhere → tell the party to release.
		private static void RezBrokerTick(PartyBus bus)
		{
			LocalPlayer me = StyxWoW.Me;
			if (me == null) return;

			var guids = new HashSet<ulong> { me.Guid };
			foreach (var g in me.GetPartyMemberGUIDs()) guids.Add(g);
			foreach (var g in me.GetRaidMemberGUIDs()) guids.Add(g);

			var dead = new List<(ulong Guid, string Role, WoWPoint Loc)>();
			var rezzers = new List<(ulong Guid, WoWPlayer P, bool Druid)>();
			bool anyRezzerAlive = false;
			bool combat = me.Combat;

			foreach (ulong g in guids)
			{
				bool self = g == me.Guid;
				WoWPlayer p = self ? null : ObjectManager.GetObjectByGuid<WoWPlayer>(g);
				bool canRez, alive, druid, inCombat; string role;
				if (self)
				{
					canRez = ComputeCanRezNow(); role = MyRole();
					alive = !me.Dead && !me.IsGhost; druid = me.Class == WoWClass.Druid; inCombat = me.Combat;
				}
				else
				{
					if (!_partyProgress.TryGetValue(g, out MemberProgress mp)) continue;
					canRez = mp.CanRez; role = mp.Role;
					alive = p != null && !p.Dead && !p.IsGhost;
					druid = p != null && p.Class == WoWClass.Druid;
					inCombat = p != null && p.Combat;
				}
				if (inCombat) combat = true;
				if (self ? (me.Dead || me.IsGhost) : (p != null && (p.Dead || p.IsGhost)))
					dead.Add((g, role, self ? me.Location : p.Location));
				if (canRez && alive) { anyRezzerAlive = true; rezzers.Add((g, self ? null : p, druid)); }
			}

			// Resolve the outstanding assignment before making a new one.
			if (_brokerTargetGuid != 0)
			{
				WoWPlayer t = ObjectManager.GetObjectByGuid<WoWPlayer>(_brokerTargetGuid);
				bool doneOrStale = (t != null && !t.Dead && !t.IsGhost)
					|| (DateTime.UtcNow - _brokerAssignedAt).TotalSeconds > RezLatchTimeoutSec;
				if (!doneOrStale) return;
				_brokerTargetGuid = 0; _brokerRezzerGuid = 0;
			}

			if (dead.Count == 0) { _rezReleaseSent = false; return; }
			if (!anyRezzerAlive)
			{
				if (!_rezReleaseSent)
				{
					_rezReleaseSent = true;
					bus.Publish("RezRelease", "");
					Logging.Write(System.Drawing.Color.Khaki, "[VibeParty] rez: no one available — party releasing.");
				}
				return;
			}
			_rezReleaseSent = false;

			dead.Sort((a, b) => RolePriority(a.Role).CompareTo(RolePriority(b.Role)));
			foreach (var d in dead)
			{
				var cands = rezzers.Where(r => r.Guid != d.Guid && (!combat || r.Druid)).ToList();
				if (cands.Count == 0) continue;
				var followers = cands.Where(r => r.Guid != me.Guid).ToList();
				var pool = followers.Count > 0 ? followers : cands;
				var rezzer = pool.OrderBy(r => r.P != null ? r.P.Location.Distance(d.Loc) : float.MaxValue).First();

				_brokerTargetGuid = d.Guid; _brokerRezzerGuid = rezzer.Guid; _brokerAssignedAt = DateTime.UtcNow;
				bus.Publish("RezAssign", rezzer.Guid + ":" + d.Guid);
				WoWPlayer rp = ObjectManager.GetObjectByGuid<WoWPlayer>(rezzer.Guid);
				WoWPlayer tp = ObjectManager.GetObjectByGuid<WoWPlayer>(d.Guid);
				Logging.Write(System.Drawing.Color.Khaki, "[VibeParty] rez: assign {0} -> {1} ({2}).",
					rp?.Name ?? "?", tp?.Name ?? "?", combat ? "combat" : "ooc");
				break;
			}
		}

		// Bus (hub thread → pure data). If the assignment is mine, arm it; if it moved to someone else, stand down.
		private void OnRezAssign(PartyMessage m)
		{
			string[] f = (m.Payload ?? "").Split(':');
			if (f.Length != 2 || !ulong.TryParse(f[0], out ulong rezzer) || !ulong.TryParse(f[1], out ulong target)) return;
			if (rezzer == StyxWoW.Me.Guid) { _myRezMemberGuid = target; _myRezStartedAt = DateTime.UtcNow; _myRezWhispered = false; }
			else if (_myRezMemberGuid == target) ClearMyRez();
		}

		private void OnRezRelease(PartyMessage m) { _rezReleaseOrdered = true; ClearMyRez(); }

		private static void ClearMyRez()
		{
			_myRezMemberGuid = 0; _myRezWhispered = false;
			Styx.CommonBot.PartyRez.Target = 0;
		}

		// Follower execution: position at the corpse, then hand off to the routine to cast. Sits ABOVE
		// CreateCombatBehavior — returns Success while travelling (consumes the tick), Failure once in range
		// so the routine's Rest/combat path ticks and casts PartyRez.Target.
		private static Composite CreateRezBehavior()
		{
			return new Decorator(ctx => _myRezMemberGuid != 0, new TreeSharp.Action(ctx => RezMoveAndCast()));
		}

		private static RunStatus RezMoveAndCast()
		{
			LocalPlayer me = StyxWoW.Me;
			WoWPlayer member = ObjectManager.GetObjectByGuid<WoWPlayer>(_myRezMemberGuid);
			if (member != null && !member.Dead && !member.IsGhost) { ClearMyRez(); return RunStatus.Failure; }   // rezzed
			if ((DateTime.UtcNow - _myRezStartedAt).TotalSeconds > RezAttemptTimeoutSec) { ClearMyRez(); return RunStatus.Failure; }
			if (me.Combat && me.Class != WoWClass.Druid) { Styx.CommonBot.PartyRez.Target = 0; return RunStatus.Failure; } // only Rebirth in combat

			// Target the dead unit (unreleased) or the member's corpse (released) — both are first-class.
			ulong castGuid; WoWPoint loc;
			if (member != null && member.Dead && !member.IsGhost) { castGuid = member.Guid; loc = member.Location; }
			else
			{
				WoWCorpse corpse = FindPartyCorpse(_myRezMemberGuid);
				if (corpse == null) { Styx.CommonBot.PartyRez.Target = 0; return RunStatus.Failure; }
				castGuid = corpse.Guid; loc = corpse.Location;
			}

			if (me.Location.Distance(loc) > RezRange) { Navigator.MoveTo(loc); return RunStatus.Success; }

			if (!_myRezWhispered) { WhisperLeader("Rezzing " + (member?.Name ?? "ally")); _myRezWhispered = true; }
			Styx.CommonBot.PartyRez.Target = castGuid;   // the routine casts in its Rest/combat path (cast-hold holds us still)
			return RunStatus.Failure;
		}

		private static WoWCorpse FindPartyCorpse(ulong ownerGuid)
		{
			foreach (WoWCorpse c in ObjectManager.GetObjectsOfType<WoWCorpse>())
				if (c != null && c.OwnerGuid == ownerGuid) return c;
			return null;
		}

		private static void WhisperLeader(string msg)
		{
			// Whispers double as panel alerts — OOM is the critical one (raid-warning on the leader).
			PublishAlert(msg == "OOM", msg);
			string leader = _botMessage?.LeaderName;
			if (string.IsNullOrEmpty(leader)) return;
			Lua.DoString("SendChatMessage('" + msg.Replace("'", "") + "', 'WHISPER', nil, '" + leader.Replace("'", "") + "')");
		}

		// Order (leader→follower, hub thread — pure data only): queue raw order strings; the bot
		// thread drains them through ExecuteOrder.
		private static readonly System.Collections.Concurrent.ConcurrentQueue<string> _orderQueue =
			new System.Collections.Concurrent.ConcurrentQueue<string>();

		private void OnOrderReceived(PartyMessage msg)
		{
			if (!string.IsNullOrEmpty(msg.Payload)) _orderQueue.Enqueue(msg.Payload);
		}

		// Follower → leader alert channel: feeds the leader panel's alert strip; critical alerts
		// also fire a raid warning + sound there. Static mirror of _bus because the publishers
		// (hearth mirror, turn-in ladder) are static. Payload: crit|name|text.
		private static PartyBus? _alertBus;

		private static void PublishAlert(bool critical, string text)
		{
			_alertBus?.Publish("Alert", (critical ? "1" : "0") + "|" + StyxWoW.Me.Name + "|" + text);
		}

		// Leader (bot thread): invite every LIVE bus member not yet grouped. Roster = _partyProgress
		// (the follower heartbeat carries its name). Localhost hub ⇒ only our own toons can be on it.
		private DateTime _inviteTickAt = DateTime.MinValue;
		private readonly Dictionary<ulong, DateTime> _invitedAt = new Dictionary<ulong, DateTime>();
		// A leavegroup order is in flight — see the empty-party gate below. Released by world state
		// (the party actually emptied), never by a timer or an attempt count.
		private bool _disbanding;

		private void AutoInviteTick()
		{
			if ((DateTime.Now - _inviteTickAt).TotalSeconds < 5) return;
			_inviteTickAt = DateTime.Now;
			// Disband in progress: re-inviting while members are still dropping would rebuild the
			// SAME group and leave the dungeon bound. Wait for a genuinely empty party — that is the
			// state the reset depends on — then reform immediately (the 15s spam throttle guards a
			// party that never formed, not a deliberate regroup).
			if (_disbanding)
			{
				if (StyxWoW.Me.PartyMembers.Count > 0) return;
				_disbanding = false;
				_invitedAt.Clear();
				Logging.Write(System.Drawing.Color.MediumSeaGreen, "[VibeParty] party disbanded — reforming a fresh group.");
			}
			if (StyxWoW.Me.IsInRaid) return;
			if (StyxWoW.Me.PartyMembers.Count >= 4) return;

			long cutoff = DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(ProgressLivenessSeconds).Ticks;
			HashSet<ulong> grouped = new HashSet<ulong>(StyxWoW.Me.PartyMemberGuids);
			foreach (KeyValuePair<ulong, MemberProgress> kv in _partyProgress)
			{
				if (kv.Value.LastUtcTicks < cutoff) continue;                       // silent → not live
				if (kv.Key == StyxWoW.Me.Guid || grouped.Contains(kv.Key)) continue;
				if (string.IsNullOrEmpty(kv.Value.Name)) continue;
				if (_invitedAt.TryGetValue(kv.Key, out DateTime last) && (DateTime.Now - last).TotalSeconds < 15) continue;
				_invitedAt[kv.Key] = DateTime.Now;
				Logging.Write(System.Drawing.Color.MediumSeaGreen, "[VibeParty] inviting {0} to the party.", kv.Value.Name);
				Lua.DoString("InviteUnit('" + kv.Value.Name.Replace("'", "\\'") + "')");
			}
		}

		// Leader (bot thread): log which LIVE followers are behind on each quest the leader holds — change-only,
		// so no spam. A member silent past the liveness window drops out (a dead client never wedges the readout).
		private void ReviewPartyProgress()
		{
			List<PlayerQuest> mine = StyxWoW.Me.QuestLog.GetAllQuests();
			if (mine == null) return;
			long cutoff = DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(ProgressLivenessSeconds).Ticks;

			// Hold the readout until every CURRENT party member has a live report — reviewing from the
			// first heartbeat re-logged every quest line per joiner as connections trickled in ("waiting
			// on Marge" → "Maggie, Marge" → …, pure arrival-order churn that read as state).
			foreach (ulong g in StyxWoW.Me.PartyMemberGuids)
				if (!_partyProgress.TryGetValue(g, out MemberProgress mp0) || mp0.LastUtcTicks < cutoff)
					return;

			foreach (PlayerQuest q in mine)
			{
				if (q == null || q.Id == 0) continue;
				List<string> behind = new List<string>();
				foreach (KeyValuePair<ulong, MemberProgress> kv in _partyProgress)
				{
					MemberProgress mp = kv.Value;
					if (mp.LastUtcTicks < cutoff) continue;                            // stale/dead — drop it
					if (mp.QuestComplete.TryGetValue(q.Id, out bool done) && !done)     // reports it, not complete
						behind.Add(mp.Name);
				}
				behind.Sort();
				string key = string.Join(",", behind);
				_lastBehind.TryGetValue(q.Id, out string? prev);
				if (key == (prev ?? "")) continue;                                     // unchanged → no log
				_lastBehind[q.Id] = key;
				if (behind.Count == 0)
					Logging.Write(System.Drawing.Color.MediumSeaGreen, "[VibeParty] '{0}' — all live party members complete.", q.Name);
				else
					Logging.Write(System.Drawing.Color.Khaki, "[VibeParty] '{0}' — waiting on {1}.", q.Name, string.Join(", ", behind));
			}
		}

		private static void DisableLeaderPluginIfEnabled()
		{
			PluginContainer? lp = PluginManager.Plugins.FirstOrDefault(p => p.Name == "LeaderPlugin");
			if (lp != null && lp.Enabled)
			{
				Logging.Write("VibeParty: Disabling legacy LeaderPlugin (VibeParty runs its own hub).");
				lp.Enabled = false;
			}
		}

		private sealed class MemberProgress
		{
			public string Name = "";
			public long LastUtcTicks;
			public bool CanRez;                 // reported: alive + knows its class res + ready (mana / druid reagent+CD)
			public string Role = "Damage";      // reported LfgRole intent (Tank/Healer/Damage) — rez target priority
			public int FreeBags = -1;           // reported free bag slots; -1 = never reported (old client / first beat)
			public string Errand = "";          // self-directed vendor errand in progress ("selling"…), "" = none
			public readonly Dictionary<uint, bool> QuestComplete = new Dictionary<uint, bool>();
		}

		// ──────────────────────────────────────────────────────────────────────
		// Lua events
		// ──────────────────────────────────────────────────────────────────────

		private void AttachLuaEvents()
		{
			Lua.Events.AttachEvent("LFG_PROPOSAL_SHOW",   new LuaEventHandlerDelegate(OnLfgProposalShow));
			Lua.Events.AttachEvent("LFG_OFFER_CONTINUE",  new LuaEventHandlerDelegate(OnLfgOfferContinue));
			Lua.Events.AttachEvent("LFG_ROLE_CHECK_SHOW", new LuaEventHandlerDelegate(OnLfgRoleCheckShow));
			Lua.Events.AttachEvent("PARTY_INVITE_REQUEST",new LuaEventHandlerDelegate(OnPartyInviteRequest));
			Lua.Events.AttachEvent("QUEST_ACCEPT_CONFIRM", new LuaEventHandlerDelegate(OnQuestAcceptConfirm));
			Lua.Events.AttachEvent("QUEST_DETAIL",         new LuaEventHandlerDelegate(OnQuestDetail));
			Lua.Events.AttachEvent("RESURRECT_REQUEST",    new LuaEventHandlerDelegate(OnResurrectRequest));
			Lua.Events.AttachEvent("TRADE_SHOW",                new LuaEventHandlerDelegate(OnTradeShouldAccept));
			Lua.Events.AttachEvent("TRADE_ACCEPT_UPDATE",       new LuaEventHandlerDelegate(OnTradeShouldAccept));
			Lua.Events.AttachEvent("TRADE_TARGET_ITEM_CHANGED", new LuaEventHandlerDelegate(OnTradeShouldAccept));
			Lua.Events.AttachEvent("TRADE_MONEY_CHANGED",       new LuaEventHandlerDelegate(OnTradeShouldAccept));
		}

		private void DetachLuaEvents()
		{
			Lua.Events.DetachEvent("LFG_PROPOSAL_SHOW",   new LuaEventHandlerDelegate(OnLfgProposalShow));
			Lua.Events.DetachEvent("LFG_OFFER_CONTINUE",  new LuaEventHandlerDelegate(OnLfgOfferContinue));
			Lua.Events.DetachEvent("LFG_ROLE_CHECK_SHOW", new LuaEventHandlerDelegate(OnLfgRoleCheckShow));
			Lua.Events.DetachEvent("QUEST_ACCEPT_CONFIRM", new LuaEventHandlerDelegate(OnQuestAcceptConfirm));
			Lua.Events.DetachEvent("QUEST_DETAIL",         new LuaEventHandlerDelegate(OnQuestDetail));
			Lua.Events.DetachEvent("RESURRECT_REQUEST",    new LuaEventHandlerDelegate(OnResurrectRequest));
			Lua.Events.DetachEvent("TRADE_SHOW",                new LuaEventHandlerDelegate(OnTradeShouldAccept));
			Lua.Events.DetachEvent("TRADE_ACCEPT_UPDATE",       new LuaEventHandlerDelegate(OnTradeShouldAccept));
			Lua.Events.DetachEvent("TRADE_TARGET_ITEM_CHANGED", new LuaEventHandlerDelegate(OnTradeShouldAccept));
			Lua.Events.DetachEvent("TRADE_MONEY_CHANGED",       new LuaEventHandlerDelegate(OnTradeShouldAccept));
		}

		// method_6 — PARTY_INVITE_REQUEST
		private void OnPartyInviteRequest(object sender, LuaEventArgs e)
		{
			if (_botMessage != null && e.Args.Length > 0)
			{
				string? name = e.Args[0] as string;
				// Name-gated to the leader — random players can't pull us into their group.
				_pendingGroupInvite = name == _botMessage.LeaderName;
			}
		}

		// method_7 — LFG_PROPOSAL_SHOW. Always accept: a proposal only exists because OUR party queued.
		private void OnLfgProposalShow(object sender, LuaEventArgs e)
		{
			_pendingDungeonProposal = true;
		}

		// method_8 — LFG_OFFER_CONTINUE
		private void OnLfgOfferContinue(object sender, LuaEventArgs e)
		{
			_pendingDungeonOfferContinue = true;
		}

		// method_9 — LFG_ROLE_CHECK_SHOW. Humanized stagger (2-5s, name-hashed so the four followers
		// answer at different moments): four instant simultaneous accepts look botty, and the beat
		// leaves the (human) leader time to see the check fire. The ROLE itself needs no queue-time
		// prep — AnswerRoleCheck resolves it from the per-character LfgRole setting / the client's
		// remembered roles / a talent guess, in that order.
		private void OnLfgRoleCheckShow(object sender, LuaEventArgs e)
		{
			_pendingRoleCheck = true;
			_roleCheckActAt = DateTime.Now.AddMilliseconds(2000 + Math.Abs(StyxWoW.Me.Name.GetHashCode()) % 3000);
		}

		// Role-check answer, three tiers. The role is USER INTENT — talents cannot derive the job at
		// leveling time (a prot-bound paladin specs RET until Seal of Command; a Disc priest opens
		// SHADOW for Spirit Tap), so:
		//   1. the per-character LfgRole setting (Tank/Healer/Damage — the durable "prep");
		//   2. Auto → whatever the client already has ticked (WoW remembers the last-used LFD roles,
		//      so a role set once in the LFD window is honored forever);
		//   3. nothing ticked → the talent guess below → Damage.
		private static void AnswerRoleCheck()
		{
			string cfg = (VibePartySettings.Instance.LfgRole ?? "Auto").Trim();
			bool tank = false, heal = false;
			string source = "setting";
			if (cfg.Equals("Tank", StringComparison.OrdinalIgnoreCase)) tank = true;
			else if (cfg.Equals("Healer", StringComparison.OrdinalIgnoreCase)) heal = true;
			else if (cfg.Equals("Damage", StringComparison.OrdinalIgnoreCase) || cfg.Equals("DPS", StringComparison.OrdinalIgnoreCase)) { }
			else
			{
				int ticked = Lua.GetReturnVal<int>(
					"local n = 0 " +
					"if LFDRoleCheckPopupRoleButtonTank and LFDRoleCheckPopupRoleButtonTank.checkButton:GetChecked() then n = n + 1 end " +
					"if LFDRoleCheckPopupRoleButtonHealer and LFDRoleCheckPopupRoleButtonHealer.checkButton:GetChecked() then n = n + 2 end " +
					"if LFDRoleCheckPopupRoleButtonDPS and LFDRoleCheckPopupRoleButtonDPS.checkButton:GetChecked() then n = n + 4 end " +
					"return n", 0U);
				if (ticked != 0)
				{
					Logging.Write("VibeParty: role check — accepting with the client's ticked roles.");
					Lua.DoString("LFDRoleCheckPopupAcceptButton:Click() StaticPopup1Button1:Click()");
					return;
				}
				DeriveLfgRole(out tank, out heal);
				source = "talent guess";
			}
			bool dps = !tank && !heal;
			Logging.Write("VibeParty: role check — accepting as {0} ({1}).",
				tank ? "TANK" : heal ? "HEALER" : "DAMAGE", source);
			Lua.DoString(string.Format(
				"local t, h, d = LFDRoleCheckPopupRoleButtonTank, LFDRoleCheckPopupRoleButtonHealer, LFDRoleCheckPopupRoleButtonDPS " +
				"if t and t.checkButton then t.checkButton:SetChecked({0}) end " +
				"if h and h.checkButton then h.checkButton:SetChecked({1}) end " +
				"if d and d.checkButton then d.checkButton:SetChecked({2}) end " +
				"if d and d.checkButton and not (t.checkButton:GetChecked() or h.checkButton:GetChecked() or d.checkButton:GetChecked()) then d.checkButton:SetChecked(true) end " +
				"LFDRoleCheckPopupAcceptButton:Click() StaticPopup1Button1:Click()",
				tank ? "true" : "false", heal ? "true" : "false", dps ? "true" : "false"));
		}

		// Tier-3 talent GUESS (last resort, fresh toons with nothing set anywhere): healer trees →
		// HEALER, prot trees → TANK, else DAMAGE. Known-wrong for leveling builds by design — that's
		// what the setting and the client-ticked tiers are for. Feral druid stays DAMAGE (cat/bear is
		// ambiguous from the tab). Cached 30s; 0/0/0 → DAMAGE.
		private static DateTime _lfgRoleReadAt = DateTime.MinValue;
		private static bool _lfgTank, _lfgHeal;

		private static void DeriveLfgRole(out bool tank, out bool heal)
		{
			if ((DateTime.Now - _lfgRoleReadAt).TotalSeconds < 30) { tank = _lfgTank; heal = _lfgHeal; return; }
			_lfgRoleReadAt = DateTime.Now;
			string talents = Lua.GetReturnVal<string>(
				"local g = GetActiveTalentGroup and GetActiveTalentGroup() or 1 local r = '' " +
				"for i = 1, 3 do local _, _, p = GetTalentTabInfo(i, false, false, g) r = r .. (p or 0) .. ';' end return r", 0U);
			var parts = (talents ?? "").Split(';');
			int t1 = parts.Length > 0 && int.TryParse(parts[0], out int a) ? a : 0;
			int t2 = parts.Length > 1 && int.TryParse(parts[1], out int b) ? b : 0;
			int t3 = parts.Length > 2 && int.TryParse(parts[2], out int c) ? c : 0;
			int max = Math.Max(t1, Math.Max(t2, t3));
			_lfgTank = false; _lfgHeal = false;
			if (max > 0)
			{
				switch (StyxWoW.Me.Class)
				{
					case WoWClass.Priest:  _lfgHeal = t3 != max; break;                       // Disc/Holy heal, Shadow DPS
					case WoWClass.Paladin: _lfgHeal = t1 == max; _lfgTank = !_lfgHeal && t2 == max; break;
					case WoWClass.Shaman:  _lfgHeal = t3 == max; break;                       // Resto
					case WoWClass.Druid:   _lfgHeal = t3 == max; break;                       // Resto; Feral → DAMAGE
					case WoWClass.Warrior: _lfgTank = t3 == max; break;                       // Prot
				}
			}
			tank = _lfgTank; heal = _lfgHeal;
		}

		// method_10 — QUEST_DETAIL. A follower only sees an offer via the leader's share or its own
		// quest-starter item — both mirror-intent, so accept.
		private void OnQuestDetail(object sender, LuaEventArgs e)
		{
			// ONE owner per frame. This handler exists for offers we did NOT open (a real share, a
			// quest-starter item, a server-pushed chain) — if the visit loop is mid-transaction it owns the
			// frame and will accept/decline itself. Arming here too made the two race: the pickup closed the
			// frame as "wrong quest" while this path logged "Accepting shared quest" for an unshared quest,
			// and only a human right-click settled it (live 2026-07-19, Rejold Barleybrew q315/q413).
			if (QuestInteractionCore.IsDriving) return;
			_pendingQuestAccept = true;
		}

		// Phase 1 (follower) — auto-confirm quests the leader shares. Escort / auto-accept shares raise
		// QUEST_ACCEPT_CONFIRM instead of QUEST_DETAIL.
		// ⚠ The ONE accept path that cannot ask ShouldAcceptOffer: this is a StaticPopup, not the quest
		// frame, so there is no readable quest id — the event carries only a NAME. It stays safe by
		// CONSTRUCTION rather than by policy: a share confirm can only originate from someone already in
		// our party, and the party is our own toons formed around the leader. Do NOT copy this exemption
		// anywhere an id IS readable, and if a quest ever slips in through here, gate it on the name
		// against the leader's quest titles rather than widening the exemption.
		private void OnQuestAcceptConfirm(object sender, LuaEventArgs e)
		{
			if (QuestInteractionCore.IsDriving) return;   // the visit transaction owns the frame
			Lua.DoString("ConfirmAcceptQuest()");
		}

		// ──────────────────────────────────────────────────────────────────────
		// Quest-abandon sync — the leader's quest log is the party's source of truth
		// ──────────────────────────────────────────────────────────────────────
		// Turn-in and abandon are IDENTICAL at the "quest left the log" level. The discriminator is
		// the server's permanent completed flag (IsQuestFlaggedCompleted), checked on a 2s deferral —
		// a turn-in sets it in the same transaction, but the client-side write can lag the log-removal
		// event, and broadcasting an abandon for a completed quest would strip the party of done work.

		private static readonly HashSet<uint> _leaderLogIds = new HashSet<uint>();
		private static bool _leaderLogSeeded;
		private static bool _leaderLogDirty = true;   // seed on the first tick
		private static readonly Dictionary<uint, DateTime> _pendingAbandonCheck = new Dictionary<uint, DateTime>();

		// Turn-in detection (leader). A human turn-in ALWAYS opens the reward panel (QUEST_COMPLETE);
		// a human abandon never does. Snapshot the log when the panel opens — the id that leaves it within
		// the window handed in, so it is NOT an abandon. Robust where IsQuestFlaggedCompleted lags the
		// removal event (that lag misfired real turn-ins as abandons — mage run 2026-07-13).
		private static volatile HashSet<uint> _turnInSnapshot = new HashSet<uint>();
		private static DateTime _turnInSnapshotAt = DateTime.MinValue;
		// Leader quest-set broadcast throttle (seeds late-joining followers' accept whitelist).
		private static string _lastLeaderQuestsCsv = "";
		private static DateTime _lastLeaderQuestsAt = DateTime.MinValue;

		private void OnLeaderQuestComplete(object sender, LuaEventArgs e)
		{
			_turnInSnapshot = new HashSet<uint>(ReadOwnQuestLogIds());
			_turnInSnapshotAt = DateTime.UtcNow;
		}

		private void OnLeaderQuestLogUpdate(object sender, LuaEventArgs e)
		{
			_leaderLogDirty = true;
		}

		private static void QuestAbandonSyncTick(PartyBus bus)
		{
			// Deferred verdicts first: gone from the log for 2s — completed or abandoned?
			if (_pendingAbandonCheck.Count > 0)
			{
				List<uint> due = _pendingAbandonCheck.Where(kv => DateTime.UtcNow >= kv.Value).Select(kv => kv.Key).ToList();
				foreach (uint id in due)
				{
					_pendingAbandonCheck.Remove(id);
					if (_leaderLogIds.Contains(id))
						continue;   // re-accepted inside the window
					HashSet<uint> snap = _turnInSnapshot;
					if ((DateTime.UtcNow - _turnInSnapshotAt).TotalSeconds < 30 && snap.Contains(id))
						continue;   // the reward panel was open for it → handed in, not abandoned
					if (Lua.GetReturnVal<int>("return IsQuestFlaggedCompleted(" + id + ") and 1 or 0", 0U) == 1)
						continue;   // turned in — never an abandon
					Logging.Write(System.Drawing.Color.Khaki, "[VibeParty] leader abandoned quest {0} — the party follows.", id);
					bus.Publish("QuestAbandon", id.ToString());
				}
			}

			// Re-broadcast the leader quest set for any follower that joined during a quiet spell.
			if (_leaderLogSeeded && (DateTime.UtcNow - _lastLeaderQuestsAt).TotalSeconds > 15)
			{
				_lastLeaderQuestsAt = DateTime.UtcNow;
				_lastLeaderQuestsCsv = string.Join(",", _leaderLogIds);
				bus.Publish("LeaderQuests", _lastLeaderQuestsCsv);
			}

			if (!_leaderLogDirty) return;
			_leaderLogDirty = false;

			HashSet<uint> current = ReadOwnQuestLogIds();
			if (_leaderLogSeeded)
				foreach (uint id in _leaderLogIds)
					if (!current.Contains(id) && !_pendingAbandonCheck.ContainsKey(id))
						_pendingAbandonCheck[id] = DateTime.UtcNow.AddSeconds(2);
			_leaderLogSeeded = true;
			_leaderLogIds.Clear();
			foreach (uint id in current) _leaderLogIds.Add(id);
			string csv = string.Join(",", current);
			if (csv != _lastLeaderQuestsCsv)   // publish the whitelist snapshot on any change
			{
				_lastLeaderQuestsCsv = csv;
				_lastLeaderQuestsAt = DateTime.UtcNow;
				bus.Publish("LeaderQuests", csv);
			}
		}

		private static HashSet<uint> ReadOwnQuestLogIds()
		{
			var set = new HashSet<uint>();
			string res = Lua.GetReturnVal<string>(
				"local r = '' for i = 1, GetNumQuestLogEntries() do " +
				"local t, _, _, _, h, _, _, _, q = GetQuestLogTitle(i) " +
				"if not h and q then r = r .. q .. ';' end end return r", 0U) ?? "";
			foreach (string tok in res.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
				if (uint.TryParse(tok, out uint id)) set.Add(id);
			return set;
		}

		// Follower side. Handlers fire on hub threads → pure data (the queue/block set); the Lua
		// abandon runs from Pulse. The BLOCK set is what makes an abandon stick: every accept path
		// consults it, otherwise the turn-in machinery re-hoovers the quest at the next giver visit
		// (or the 10-min pickup record replays it) and the abandon silently un-does itself.
		private static readonly ConcurrentDictionary<uint, DateTime> _abandonBlock = new ConcurrentDictionary<uint, DateTime>();
		private static readonly ConcurrentQueue<uint> _abandonQueue = new ConcurrentQueue<uint>();
		private static readonly TimeSpan AbandonBlockTtl = TimeSpan.FromMinutes(30);

		// The party does the LEADER's quests, nothing else (user directive 2026-07-13). This is the set of
		// quest ids the leader has picked up this session — the ONLY quests a follower may ACCEPT (turn-in /
		// completion of anything already held stays ungated). Fed by the leader's per-accept `QuestAccept`
		// and its periodic `LeaderQuests` log snapshot (covers a follower that joined mid-session). Ids are
		// KEPT after the leader turns them in — a follower may still need to walk a prerequisite chain the
		// leader already finished (accept X → turn in X → X unlocks Y). Removed only on a real abandon.
		private static readonly ConcurrentDictionary<uint, byte> _leaderQuests = new ConcurrentDictionary<uint, byte>();

		private static bool IsLeaderQuest(uint id) => id != 0 && _leaderQuests.ContainsKey(id);

		private static void OnQuestAbandonMsg(PartyMessage m)
		{
			if (!uint.TryParse(m.Payload, out uint id) || id == 0) return;
			_abandonBlock[id] = DateTime.UtcNow + AbandonBlockTtl;
			_abandonQueue.Enqueue(id);
			_leaderQuests.TryRemove(id, out _);   // leader gave it up → drop from the accept whitelist
		}

		// Leader re-accepted a quest → lift the block so the share/pickup paths work again, and (re)admit it
		// to the accept whitelist.
		private static void OnQuestAcceptMsg(PartyMessage m)
		{
			if (!uint.TryParse(m.Payload, out uint id) || id == 0) return;
			_abandonBlock.TryRemove(id, out _);
			if (!_leaderQuests.ContainsKey(id)) _leaderQuestsDirty = true;   // hub thread: flag only, Pulse acts
			_leaderQuests[id] = 1;
		}

		// Leader's current quest-log snapshot (csv) — UNION into the whitelist (never replace: turned-in ids
		// must persist for chain prereqs). Seeds a follower that connected after the leader took its quests.
		private static void OnLeaderQuestsMsg(PartyMessage m)
		{
			if (string.IsNullOrEmpty(m.Payload)) return;
			foreach (string tok in m.Payload.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
				if (uint.TryParse(tok, out uint id) && id != 0 && !QuestBlocked(id))
				{
					if (!_leaderQuests.ContainsKey(id)) _leaderQuestsDirty = true;
					_leaderQuests[id] = 1;
				}
		}

		private static bool QuestBlocked(uint id)
			=> id != 0 && _abandonBlock.TryGetValue(id, out DateTime until) && DateTime.UtcNow < until;

		private static void ProcessAbandonQueue()
		{
			while (_abandonQueue.TryDequeue(out uint id))
			{
				if (!StyxWoW.Me.QuestLog.ContainsQuest(id)) continue;
				Logging.Write(System.Drawing.Color.Khaki, "[VibeParty] abandoning quest {0} (leader abandoned it).", id);
				Lua.DoString(
					"for i = 1, GetNumQuestLogEntries() do " +
					"local t, _, _, _, h, _, _, _, q = GetQuestLogTitle(i) " +
					"if not h and q == " + id + " then SelectQuestLogEntry(i) SetAbandonQuest() AbandonQuest() break end end");
			}
		}

		// Follower — auto-accept a trade from the party LEADER (gear/consumables/quest-item handoff). Always on
		// (no toggle): it's safe by construction. Receive-only — the bot never places items on ITS side, so
		// accepting can only take what the leader offers, never give anything away — and gated to the leader BY
		// NAME (UnitName('npc') is the trade partner while a trade window is open, per 3.3.5a TradeFrame), so a
		// random world player can't auto-trade us. Fires on every trade change (show / accept-update / item /
		// money) because WoW clears our accept flag whenever the leader adds an item; re-accepting each change
		// converges by the time the leader clicks. AcceptTrade() when already accepted is a no-op → can't loop.
		private void OnTradeShouldAccept(object sender, LuaEventArgs e)
		{
			if (VibePartySettings.Instance.IsLeader)
			{
				// Leader (human-driven): auto-accept ONLY a pure water delivery — our side empty, at least one
				// incoming item, EVERY incoming item a Conjured…Water, partner in the party. Anything else is
				// the human's own trade in progress; completing it under their cursor would hand over whatever
				// they'd placed so far.
				Lua.DoString(
					"local ok=(UnitInParty('npc') or UnitInRaid('npc')) and true or false " +
					"local incoming=0 " +
					"if ok then for i=1,6 do if GetTradePlayerItemLink(i) then ok=false end local l=GetTradeTargetItemLink(i) if l then if string.find(l,'Conjured') and string.find(l,'Water') then incoming=incoming+1 else ok=false end end end end " +
					"if ok and incoming>0 then AcceptTrade() end");
				return;
			}
			if (_botMessage == null) return;
			string leaderName = _botMessage.LeaderName ?? "";
			// Accept from the leader (by name — covers ungrouped TCP-follow) OR any party/raid member (covers a
			// party MAGE handing out water). Still receive-only: we never place items on our side, so accepting
			// can only take what's offered, never give anything away.
			Lua.DoString(string.Format(
				"if UnitName('npc')=='{0}' or UnitInParty('npc') or UnitInRaid('npc') then AcceptTrade() end", leaderName));
		}

		// Phase 1 (leader) — share each quest we accept with the party. 3.3.5a QUEST_ACCEPTED
		// arg1 = quest-log index; select it and push. QuestLogPushQuest no-ops when solo or the
		// quest isn't shareable.
		private void OnQuestAcceptedShare(object sender, LuaEventArgs e)
		{
			if (e.Args.Length < 1) return;
			// Lift any follower-side abandon block FIRST (bus is near-instant; the share below round-trips
			// the server) — re-accepting on the leader re-arms the quest for the whole party.
			int acceptedId = Lua.GetReturnVal<int>(
				"local t, _, _, _, _, _, _, _, q = GetQuestLogTitle(" + e.Args[0] + ") return q or 0", 0U);
			if (acceptedId > 0 && _bus != null)
				_bus.Publish("QuestAccept", acceptedId.ToString());
			Lua.DoString("SelectQuestLogEntry(" + e.Args[0] + ") QuestLogPushQuest()");
			// Durable pickup record: trailing followers arrive AFTER the live leader-at-giver signal
			// is gone — publish WHERE this quest was taken so they replay the visit when they pass
			// (10 min TTL, follower side). The share above covers shareables instantly; this record
			// covers the unshareables. Item-started accepts have no NPC target → nothing published
			// (the quest-starter path owns those).
			var giver = StyxWoW.Me.CurrentTarget;
			if (giver != null && giver.IsQuestGiver && _bus != null)
			{
				Logging.WriteDebug("VibeParty: pickup record — quest accepted at {0} [{1}].", giver.Name, giver.Entry);
				_bus.Publish("LeaderPickup", giver.Entry.ToString());
			}
		}

		// Bus (hub thread — pure data only): the leader accepted a quest at this NPC entry; remember
		// it for the trailing-replay window (RecentLeaderPickup).
		private void OnLeaderPickup(PartyMessage msg)
		{
			if (uint.TryParse(msg.Payload, out uint entry) && entry != 0)
				_leaderPickupLog[entry] = DateTime.UtcNow;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Party chat handler — method_1
		// ──────────────────────────────────────────────────────────────────────

		// The leader's in-game command panel — an addon-STYLE frame INJECTED once the leader is
		// attached and in world, not a real AddOn on disk: addon files only load at client start
		// (anything written post-attach needs a /reload), and the Interface folder is junction-
		// shared across clients (followers + the play client would all load it). The command table
		// lives HERE so the panel can never drift from the OnPartyChat switch. Each row is a
		// BUTTON that sends its command to party chat; /vp toggles, drag to move (Escape does NOT
		// close it — see the UISpecialFrames note in ShowLeaderCommandPanel).
		// Descriptions stay apostrophe-free (they land inside single-quoted Lua strings).
		private static bool _leaderPanelShown;
		// Order names, no prefix — the transport is the PartyBus ("Order" messages), never chat.
		// Self: the order acts on the leader's own body too. The dividing line is what the order DRIVES —
		// follower-only machinery (vendor runs, POIs, follow glue) must never drag the manually-driven
		// leader in, but an order that is purely an action on your OWN character ("everyone mounts",
		// "everyone hearths", the LFG/battleground teleports) means everyone, clicker included: that IS
		// the button. ExecuteOrder self-guards the targeted form, so "leavedungeon Bob" still can't port
		// the leader. (`dance` stays false on purpose — the only cosmetic order, and the leader is being
		// played by hand.)
		private static readonly (string Cmd, string Desc, bool Self)[] LeaderCommands =
		{
			("vendor",            "run the whole errand: sell + repair + mail", false),
			("hearth",            "everyone uses their hearthstone",            true),
			("forcesell",         "force a sell run",                           false),
			("forcerepair",       "force a repair run",                         false),
			("forcemail",         "force a mail run",                           false),
			("forcetrain",        "force a class trainer visit",                false),
			("follow",            "clear holds, snap everyone to /follow",      false),
			("wait",              "toggle hold-position",                       false),
			("mountup",           "everyone mounts",                            true),
			("dismount",          "everyone dismounts",                         true),
			("interact",          "followers interact with your target",        false),
			("clearpoi",          "drop the followers active POI",              false),
			("enterdungeon",      "LFG teleport into the dungeon",              true),
			("leavedungeon",      "LFG teleport out",                           true),
			("leavegroup",        "disband the party (resets the dungeon)",     true),
			("leavebattleground", "leave the battleground",                     true),
			("dance",             "morale",                                     false),
		};

		// "cmd" / "cmd Name" → does the leader run this one on itself? (ExecuteOrder self-guards the
		// targeted form, so a "mountup Bob" click never mounts the leader.)
		private static bool IsSelfOrder(string raw)
		{
			string token = raw.Trim();
			int sp = token.IndexOf(' ');
			if (sp > 0) token = token.Substring(0, sp);
			foreach (var c in LeaderCommands)
				if (c.Self && string.Equals(c.Cmd, token, StringComparison.OrdinalIgnoreCase))
					return true;
			return false;
		}

		// Layout (fixed 340x240, adversarially reviewed): 20px title row (brand + live party summary
		// + flat close), 22px full-background tab bar (real click targets — the old text-only tabs
		// were the complaint), fixed-height content, 2-line alert dock pinned bottom on EVERY tab.
		// Fixed height on purpose: per-tab resizing dragged the always-on alert dock up and down —
		// the one element that must never move. HEIGHT IS SET BY THE TALLEST TAB, which is Orders —
		// it went 2 columns × 9 rows → 3 × 6 precisely so the panel could lose 40px, because this
		// thing lives on screen permanently. Widening Orders again re-inflates every tab.
		// Buttons are a flat-dark factory (BACKGROUND texture
		// + free HIGHLIGHT-layer hover); UIPanelButtonTemplate's gold cannot be desaturated on this
		// client and clashes with the dark skin. ⚠ 3.3.5a has no SetWordWrap/SetMaxLines — every
		// pushed line is truncated C#-side (TruncRaw) or long text wraps and breaks the fixed rows.
		private static void ShowLeaderCommandPanel()
		{
			if (_leaderPanelShown) return;
			_leaderPanelShown = true;

			// Orders reference tab: the command table as a grid of flat buttons, description on hover.
			// The grid SIZES ITSELF to LeaderCommands in BOTH axes (column width and row pitch derive
			// from the page box) — a hardcoded row count silently pushed the next command added into
			// the alert dock. 3 columns: the longest label ('leavebattleground') still fits 106px, and
			// 3-wide is what lets the whole panel be 240 tall instead of 280.
			const int OrdersGridHeight = 144;   // page box: TOPLEFT 8,-52 → BOTTOMRIGHT -8,44 on a 240px panel
			const int OrdersGridWidth = 324;    // 340 panel - 8px padding each side
			const int OrdersCols = 3;
			var rows = new System.Text.StringBuilder();
			int gridRows = (LeaderCommands.Length + OrdersCols - 1) / OrdersCols;
			int pitch = Math.Min(22, OrdersGridHeight / gridRows);
			int colPitch = OrdersGridWidth / OrdersCols;
			int idx = 0;
			foreach (var (cmd, desc, _) in LeaderCommands)
			{
				int col = idx / gridRows, row = idx % gridRows;
				idx++;
				rows.Append($$$"""
					do local b = Btn(cp, {{{colPitch - 2}}}, {{{pitch - 2}}}, '|cff7fd5ff{{{cmd}}}|r')
					b:SetPoint('TOPLEFT', {{{col * colPitch}}}, {{{-row * pitch}}})
					b.text:ClearAllPoints() b.text:SetPoint('LEFT', 6, 0)
					b:SetScript('OnClick', function() Send('{{{cmd}}}') end)
					b:SetScript('OnEnter', function() GameTooltip:SetOwner(b, 'ANCHOR_RIGHT') GameTooltip:SetText('{{{desc}}}') GameTooltip:Show() end)
					b:SetScript('OnLeave', function() GameTooltip:Hide() end) end
					""").Append('\n');
			}

			// The frame survives bot restarts within one client session — reuse, don't duplicate
			// (re-injection would orphan a ghost frame; #1 injected-UI trap). The slash handler and the
			// UISpecialFrames de-registration run BEFORE the reuse early-return, so a bot restart both
			// installs the current handler and fixes up a frame injected by an older build.
			// ⚠ REUSE IS VERSION-GATED (PANEL_VER, below): a WoW client outlives many CB restarts, so a
			// layout change used to be invisible until the user relogged or /reload'd — the panel kept
			// the old frame AND its old Apply. The stamp is a hash of this whole script, so ANY edit
			// here rebuilds the frame automatically; there is no version constant to forget to bump.
			// ⚠ DELIBERATELY NOT in UISpecialFrames: Escape is the user's "clear every other panel"
			// reflex, and it must not take the command panel down with them. /vp is the only toggle.
			// Every show — first injection, bot restart, /vp — re-centers: the panel must land where
			// the user is looking, and it doubles as the recovery from an off-screen drag.
			string lua = $$$"""
				SLASH_VIBEPARTY1 = '/vp'
				SlashCmdList['VIBEPARTY'] = function()
					if not VibePartyPanel then return end
					if VibePartyPanel:IsShown() then VibePartyPanel:Hide()
					else
						VibePartyPanel:ClearAllPoints()
						VibePartyPanel:SetPoint('CENTER', UIParent, 'CENTER', 0, 0)
						VibePartyPanel:Show()
					end
				end
				for i = #UISpecialFrames, 1, -1 do
					if UISpecialFrames[i] == 'VibePartyPanel' then tremove(UISpecialFrames, i) end
				end
				if VibePartyPanel and VibePartyPanel.vpVer ~= PANEL_VER then
					VibePartyPanel:Hide()   -- 3.3.5 cannot destroy a frame; orphan it (inert: hidden, no OnUpdate)
					VibePartyPanel = nil
					VibePartyPanelApply = nil
				end
				if VibePartyPanel then
					VibePartyPanel:ClearAllPoints()
					VibePartyPanel:SetPoint('CENTER', UIParent, 'CENTER', 0, 0)
					VibePartyPanel:Show()
					return
				end
				local f = CreateFrame('Frame', 'VibePartyPanel', UIParent)
				f.vpVer = PANEL_VER
				f:SetWidth(340) f:SetHeight(240)
				f:SetPoint('CENTER', UIParent, 'CENTER', 0, 0)
				f:SetFrameStrata('MEDIUM') f:SetToplevel(true) f:SetClampedToScreen(true)
				f:SetBackdrop({bgFile='Interface\\Buttons\\WHITE8X8', edgeFile='Interface\\Buttons\\WHITE8X8', edgeSize=1})
				f:SetBackdropColor(0.055, 0.055, 0.06, 0.55) f:SetBackdropBorderColor(0, 0, 0, 0.9)
				f:SetMovable(true) f:EnableMouse(true) f:RegisterForDrag('LeftButton')
				f:SetScript('OnDragStart', function() f:StartMoving() end)
				f:SetScript('OnDragStop', function() f:StopMovingOrSizing() end)
				VibePartyPanelQueue = VibePartyPanelQueue or {}
				local function Send(order) table.insert(VibePartyPanelQueue, order) end
				local function Btn(parent, w, h, label, plain)
					local b = CreateFrame('Button', nil, parent)
					b:SetWidth(w) b:SetHeight(h)
					if not plain then
						b:SetBackdrop({edgeFile='Interface\\Buttons\\WHITE8X8', edgeSize=1})
						b:SetBackdropBorderColor(0.32, 0.32, 0.38, 1)
					end
					local bg = b:CreateTexture(nil, 'BACKGROUND') bg:SetAllPoints() bg:SetTexture(plain and 0.12 or 0.17, plain and 0.12 or 0.17, plain and 0.13 or 0.19, 0.9)
					local hl = b:CreateTexture(nil, 'HIGHLIGHT') hl:SetAllPoints() hl:SetTexture(1, 1, 1, 0.08)
					local t = b:CreateFontString(nil, 'OVERLAY', 'GameFontHighlightSmall') t:SetPoint('CENTER') t:SetText(label)
					b.bg = bg b.text = t
					return b
				end
				local title = f:CreateFontString(nil, 'OVERLAY', 'GameFontNormal') title:SetPoint('TOPLEFT', 8, -5) title:SetText('|cff33ff99VibeParty|r')
				local hd = f:CreateFontString('VibePartyPanelHeadText', 'OVERLAY', 'GameFontHighlightSmall')
				hd:SetPoint('TOPRIGHT', -26, -8) hd:SetText('')
				local xb = Btn(f, 16, 16, '|cff909090x|r', true) xb:SetPoint('TOPRIGHT', -5, -4)
				xb:SetScript('OnClick', function() f:Hide() end)
				local pages, tabs = {}, {}
				local function SelectTab(n)
					for i = 1, #tabs do
						local on = i == n
						tabs[i].bg:SetTexture(0.12, 0.12, 0.13, on and 1 or 0.35)
						tabs[i].text:SetText((on and '|cffffffff' or '|cff909090') .. tabs[i].label .. '|r')
						if on then tabs[i].accent:Show() pages[i]:Show() else tabs[i].accent:Hide() pages[i]:Hide() end
					end
				end
				local function AddTab(label)
					local i = #tabs + 1
					local b = Btn(f, 81, 22, label, true)
					b:SetPoint('TOPLEFT', 8 + (i - 1) * 81, -24)
					b.label = label
					local ac = b:CreateTexture(nil, 'BORDER') ac:SetTexture(0.5, 0.84, 1, 1)
					ac:SetPoint('BOTTOMLEFT', 0, 0) ac:SetPoint('BOTTOMRIGHT', 0, 0) ac:SetHeight(1) ac:Hide()
					b.accent = ac
					b:SetScript('OnClick', function() SelectTab(i) end)
					tabs[i] = b
					local p = CreateFrame('Frame', nil, f)
					p:SetPoint('TOPLEFT', 8, -52) p:SetPoint('BOTTOMRIGHT', -8, 44) p:Hide()
					pages[i] = p
					return p
				end
				local mp = AddTab('Main')
				local cp = AddTab('Orders')
				local pp = AddTab('Party')
				local qp = AddTab('Quests')
				local ln = f:CreateTexture(nil, 'ARTWORK') ln:SetTexture(0.5, 0.84, 1, 0.25)
				ln:SetPoint('BOTTOMLEFT', 8, 40) ln:SetPoint('BOTTOMRIGHT', -8, 40) ln:SetHeight(1)
				local al = f:CreateFontString('VibePartyPanelAlertText', 'OVERLAY', 'GameFontHighlightSmall')
				al:SetPoint('BOTTOMLEFT', 8, 7) al:SetPoint('BOTTOMRIGHT', -8, 7) al:SetHeight(30)
				al:SetJustifyH('LEFT') al:SetJustifyV('BOTTOM') al:SetSpacing(2)
				al:SetText('|cff707070no alerts yet|r')
				local quick = {
					{'Vendor','vendor'},   {'Hearth','hearth'},    {'Follow','follow'},   {'Hold','wait'},
					{'Mount','mountup'},   {'Dismount','dismount'},{'Leave Dg','leavedungeon'},{'Leave Grp','leavegroup'}}
				for i = 1, 8 do
					local b = Btn(mp, 80, 20, quick[i][1])
					b:SetPoint('TOPLEFT', ((i - 1) % 4) * 81, -math.floor((i - 1) / 4) * 22)
					local order = quick[i][2]
					b:SetScript('OnClick', function() Send(order) end)
				end
				local wt = mp:CreateFontString('VibePartyPanelWaterText', 'OVERLAY', 'GameFontHighlightSmall')
				wt:SetPoint('TOPLEFT', 0, -46) wt:SetPoint('TOPRIGHT', 0, -46) wt:SetJustifyH('LEFT')
				wt:SetText('|cff707070Water: no mage report|r')
				local bg2 = mp:CreateFontString('VibePartyPanelBagsText', 'OVERLAY', 'GameFontHighlightSmall')
				bg2:SetPoint('TOPLEFT', 0, -60) bg2:SetPoint('TOPRIGHT', 0, -60) bg2:SetJustifyH('LEFT')
				bg2:SetText('')
				local er = mp:CreateFontString('VibePartyPanelErrandText', 'OVERLAY', 'GameFontHighlightSmall')
				er:SetPoint('TOPLEFT', 0, -74) er:SetPoint('TOPRIGHT', 0, -74) er:SetJustifyH('LEFT')
				er:SetText('')
				local fd = mp:CreateFontString('VibePartyPanelFeedText', 'OVERLAY', 'GameFontHighlightSmall')
				fd:SetPoint('TOPLEFT', 0, -76) fd:SetPoint('BOTTOMRIGHT', 0, 0)
				fd:SetJustifyH('LEFT') fd:SetJustifyV('TOP') fd:SetSpacing(2)
				fd:SetText('|cff707070alerts appear here, newest first|r')
				{{{rows}}}
				for i = 1, 4 do
					local row = CreateFrame('Frame', 'VibePartyPanelSlot'..i, pp)
					row:SetPoint('TOPLEFT', 0, -16 - (i - 1) * 24) row:SetWidth(324) row:SetHeight(22) row:Hide()
					row.name = row:CreateFontString(nil, 'OVERLAY', 'GameFontHighlightSmall')
					row.name:SetPoint('LEFT', 0, 0) row.name:SetWidth(96) row.name:SetJustifyH('LEFT')
					row.info = row:CreateFontString(nil, 'OVERLAY', 'GameFontHighlightSmall')
					row.info:SetPoint('LEFT', 100, 0) row.info:SetWidth(92) row.info:SetJustifyH('LEFT')
					local acts = {{'Vend','vendor'},{'Train','forcetrain'},{'Wait','wait'}}
					for a = 1, 3 do
						local b = Btn(row, 40, 18, acts[a][1])
						b:SetPoint('LEFT', 196 + (a - 1) * 44, 0)
						local order = acts[a][2]
						b:SetScript('OnClick', function() if row.member then Send(order .. ' ' .. row.member) end end)
					end
				end
				local ph = pp:CreateFontString(nil, 'OVERLAY', 'GameFontDisableSmall')
				ph:SetPoint('TOPLEFT', 0, 0) ph:SetText('follower                 hp   quests')
				local qt = qp:CreateFontString('VibePartyPanelQuestText', 'OVERLAY', 'GameFontHighlightSmall')
				qt:SetPoint('TOPLEFT', 0, 0) qt:SetPoint('BOTTOMRIGHT', 0, 0)
				qt:SetJustifyH('LEFT') qt:SetJustifyV('TOP') qt:SetSpacing(3)
				qt:SetText('|cff707070no quest data yet|r')
				function VibePartyPanelApply(head, feed, alerts, quests, plain, disp, infos, water, bags, errand)
					VibePartyPanelHeadText:SetText(head)
					VibePartyPanelAlertText:SetText(alerts)
					VibePartyPanelQuestText:SetText(quests)
					-- Status lines COLLAPSE when they have nothing to say and the feed takes the space
					-- back. C# counts the same non-empty lines to pick how many feed lines it sends —
					-- one line too many spills past the divider (FontStrings are not clipped here).
					local y = -46
					local function status(fs, txt)
						if txt and txt ~= '' then
							fs:ClearAllPoints()
							fs:SetPoint('TOPLEFT', 0, y) fs:SetPoint('TOPRIGHT', 0, y)
							fs:SetText(txt) fs:Show()
							y = y - 14
						else
							fs:SetText('') fs:Hide()
						end
					end
					status(VibePartyPanelWaterText, water)
					status(VibePartyPanelBagsText, bags)
					status(VibePartyPanelErrandText, errand)
					VibePartyPanelFeedText:ClearAllPoints()
					VibePartyPanelFeedText:SetPoint('TOPLEFT', 0, y - 2)
					VibePartyPanelFeedText:SetPoint('BOTTOMRIGHT', 0, 0)
					VibePartyPanelFeedText:SetText(feed)
					for i = 1, 4 do
						local row = _G['VibePartyPanelSlot'..i]
						if plain[i] then row.member = plain[i] row.name:SetText(disp[i]) row.info:SetText(infos[i]) row:Show()
						else row.member = nil row:Hide() end
					end
				end
				SelectTab(1)
				DEFAULT_CHAT_FRAME:AddMessage('VibeParty: control panel ready - /vp toggles (opens centered).')
				""";
			Lua.DoString("local PANEL_VER = " + PanelScriptVersion(lua) + "\n" + lua);
		}

		// FNV-1a over the generated panel script — a stable layout identity across processes (unlike
		// string.GetHashCode, which .NET randomises per process and would rebuild the frame on every
		// bot start). Edit anything about the panel and the stamp changes; touch nothing and a client
		// that has already been injected keeps its frame.
		private static uint PanelScriptVersion(string script)
		{
			uint h = 2166136261;
			foreach (char c in script) { h ^= c; h *= 16777619; }
			return h;
		}

		// Panel clicks land in a Lua-side queue (buttons can't call C#); the leader drains it here
		// and publishes each order on the bus — chat is NOT the transport (the server parses '!'/'.'
		// prefixed chat as GM-command syntax and eats it).
		private static DateTime _panelOrderPollAt = DateTime.MinValue;

		private void DrainPanelOrdersTick(PartyBus bus)
		{
			if (!_leaderPanelShown) return;
			if ((DateTime.Now - _panelOrderPollAt).TotalMilliseconds < 250) return;
			_panelOrderPollAt = DateTime.Now;
			string drained = Lua.GetReturnVal<string>(
				"local q = VibePartyPanelQueue if not q or #q == 0 then return '' end VibePartyPanelQueue = {} return table.concat(q, ';')", 0U) ?? "";
			if (drained.Length == 0) return;
			foreach (string order in drained.Split(';'))
			{
				if (string.IsNullOrWhiteSpace(order)) continue;
				Logging.Write("[VibeParty] panel order: {0}", order);
				bus.Publish("Order", order);
				// Self orders run here too — same executor, same tick, so the leader mounts with the
				// party instead of watching it ride off. (Bot thread: Pulse, so Lua/casts are safe.)
				if (IsSelfOrder(order)) ExecuteOrder(order);
			}
		}

		// Panel live refresh: the leader composes the header summary, Main alert feed, the alert
		// dock, the Quests readout and the per-member roster slots, and pushes them through one
		// Lua apply call. Lua-side guard keeps it a no-op while the panel is hidden (alerts still
		// reach the user — AlertDrainTick fires the raid warning independently).
		private static DateTime _panelStatusAt = DateTime.MinValue;

		// 3.3.5a FontStrings have no SetWordWrap/SetMaxLines — an over-long line WRAPS and breaks
		// the fixed-line layout, so every raw text piece is capped before coloring.
		private static string TruncRaw(string s, int max)
			=> string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s.Substring(0, max - 2) + "..";

		private static string AlertLine((DateTime At, string Name, string Text, bool Crit) al)
			=> string.Format("|cff707070{0:HH:mm}|r |cffffffff{1}|r {2}{3}|r",
				al.At, EscLua(TruncRaw(al.Name, 12)), al.Crit ? "|cffff4040" : "|cffffd070", EscLua(TruncRaw(al.Text, 38)));

		// Main-tab water line: the mage reports "<have>/<target>|<guid>,…"; guids resolve through the
		// progress roster — the leader's own guid too (the leader is a requester as well).
		private static string ComposeWaterLine()
		{
			string? raw = _waterStatusRaw;
			if (string.IsNullOrEmpty(raw)) return "";   // no mage in the party — collapse, don't print a placeholder
			int bar = raw.IndexOf('|');
			string stock = bar < 0 ? raw : raw.Substring(0, bar);
			var names = new List<string>();
			if (bar >= 0)
			{
				foreach (string tok in raw.Substring(bar + 1).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
				{
					if (!ulong.TryParse(tok, out ulong guid)) continue;
					if (guid == StyxWoW.Me.Guid) names.Add(StyxWoW.Me.Name);
					else if (_partyProgress.TryGetValue(guid, out MemberProgress? mp) && !string.IsNullOrEmpty(mp.Name)) names.Add(mp.Name);
				}
			}
			string line = "|cff909090Water:|r |cffffffff" + EscLua(TruncRaw(_waterMageName ?? "?", 12))
				+ "|r |cffd0d0d0" + EscLua(TruncRaw(stock, 7)) + "|r";
			if (names.Count > 0)
				line += " |cff909090-|r |cffffc040" + EscLua(TruncRaw(string.Join(", ", names), 24)) + " waiting|r";
			return line;
		}

		// Main-tab bags line: the tightest bags in the party, because a full-bag follower silently stops
		// looting and the leader has no other way to see it. The leader reads its OWN slots locally
		// (Publish never delivers to the sender), followers report #bags in the heartbeat. Members that
		// have never reported are simply absent — no placeholder that reads as "0 free".
		// DISPLAY threshold only — nothing acts on it. The vendor tree's own trigger is the profile's
		// MinFreeBagSlots (0 on the synthetic profile, i.e. it reacts when bags are ALREADY full), so the
		// readout deliberately warns earlier: ~one loot-heavy pull of lead time.
		private const int BagsTightThreshold = 4;

		private static string ComposeBagsLine()
		{
			long cutoff = DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(ProgressLivenessSeconds).Ticks;
			var tight = new List<(string Name, int Free)> { (StyxWoW.Me.Name, (int)StyxWoW.Me.FreeBagSlots) };
			foreach (var kv in _partyProgress)
				if (kv.Value.LastUtcTicks >= cutoff && kv.Value.FreeBags >= 0 && !string.IsNullOrEmpty(kv.Value.Name))
					tight.Add((kv.Value.Name, kv.Value.FreeBags));
			tight.Sort((a, b) => a.Free != b.Free ? a.Free.CompareTo(b.Free) : string.CompareOrdinal(a.Name, b.Name));

			int worst = tight[0].Free;
			if (worst > BagsTightThreshold)
				return "";   // nothing to warn about — collapse the line, the feed gets the space
			// Name everyone at the worst level — "Eagan 0" when three are full is a misleading readout.
			var names = new List<string>();
			foreach (var t in tight)
				if (t.Free <= BagsTightThreshold && names.Count < 4) names.Add(t.Name);
			return "|cff909090Bags:|r " + (worst == 0 ? "|cffff4040" : "|cffffc040")
				+ EscLua(TruncRaw(string.Join(", ", names), 30)) + "|r |cffd0d0d0" + worst + " free|r";
		}

		// Main-tab errand line: who has peeled off to a vendor right now. Empty string when nobody has —
		// the Lua side then COLLAPSES the line and hands its 14px back to the alert feed.
		private static string ComposeErrandLine()
		{
			long cutoff = DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(ProgressLivenessSeconds).Ticks;
			var busy = new List<string>();
			foreach (var kv in _partyProgress.OrderBy(k => k.Value.Name))
				if (kv.Value.LastUtcTicks >= cutoff && kv.Value.Errand.Length > 0 && !string.IsNullOrEmpty(kv.Value.Name))
					busy.Add(TruncRaw(kv.Value.Name, 10) + " " + kv.Value.Errand);
			if (busy.Count == 0) return "";
			return "|cff909090Errand:|r |cffffc040" + EscLua(TruncRaw(string.Join(", ", busy), 40)) + "|r";
		}

		private void UpdateLeaderPanelTick()
		{
			if (!_leaderPanelShown) return;
			if ((DateTime.Now - _panelStatusAt).TotalSeconds < 2) return;
			_panelStatusAt = DateTime.Now;

			var mine = new List<PlayerQuest>();
			foreach (PlayerQuest q in StyxWoW.Me.QuestLog.GetAllQuests() ?? new List<PlayerQuest>())
				if (q != null && q.Id != 0) mine.Add(q);

			long cutoff = DateTime.UtcNow.Ticks - TimeSpan.FromSeconds(ProgressLivenessSeconds).Ticks;

			// Roster slots: name colored by STATE (the actionable glance signal), role as a letter,
			// numbers in the info column. Live reporters get the per-member order buttons.
			var plain = new List<string>();
			var disp = new List<string>();
			var infos = new List<string>();
			int liveN = 0, deadN = 0, combatN = 0, silentN = 0;
			foreach (var kv in _partyProgress.OrderBy(k => k.Value.Name))
			{
				MemberProgress mp = kv.Value;
				if (string.IsNullOrEmpty(mp.Name)) continue;
				bool live = mp.LastUtcTicks >= cutoff;
				WoWPlayer? p = ObjectManager.GetObjectByGuid<WoWPlayer>(kv.Key);
				bool dead = p != null && (p.Dead || p.IsGhost);
				bool combat = p != null && p.Combat;
				if (!live) silentN++; else { liveN++; if (dead) deadN++; else if (combat) combatN++; }

				int done = 0;
				foreach (PlayerQuest q in mine)
					if (mp.QuestComplete.TryGetValue(q.Id, out bool d) && d) done++;

				string nameColor = !live ? "|cff707070" : dead ? "|cffff4040" : combat ? "|cffffa040" : "|cffffffff";
				string roleLetter = string.IsNullOrEmpty(mp.Role) ? "D" : mp.Role.Substring(0, 1);
				string info = !live ? "|cff707070silent|r"
					: dead ? "|cffff4040DEAD|r"
					: p == null ? "|cff909090far|r"
					: string.Format("|cffd0d0d0{0:F0}%|r |cff909090{1}/{2}|r", p.HealthPercent, done, mine.Count);

				if (plain.Count < 4)
				{
					plain.Add(EscLua(mp.Name));
					disp.Add("|cff909090" + roleLetter + "|r " + nameColor + EscLua(TruncRaw(mp.Name, 12)) + "|r");
					infos.Add(info);
				}
			}

			// Header summary: the worst live condition wins — readable without opening any tab.
			string head = deadN > 0 ? "|cffff4040" + deadN + " DEAD|r"
				: silentN > 0 ? "|cff909090" + silentN + " silent|r"
				: combatN > 0 ? "|cffffa040" + combatN + " in combat|r"
				: liveN > 0 ? "|cff40ff40" + liveN + "/" + liveN + " ok|r"
				: "|cff707070no followers|r";

			// Quests: collapse done quests to a count (packing every quest was the space complaint);
			// list only the ones the party is still working, capped.
			var quests = new System.Text.StringBuilder();
			int doneQ = 0, listed = 0, overflow = 0;
			foreach (PlayerQuest q in mine)
			{
				var behind = new List<string>();
				bool anyReport = false;
				foreach (var kv in _partyProgress)
				{
					if (kv.Value.LastUtcTicks < cutoff) continue;
					if (!kv.Value.QuestComplete.TryGetValue(q.Id, out bool d)) continue;
					anyReport = true;
					if (!d) behind.Add(kv.Value.Name);
				}
				if (anyReport && behind.Count == 0) { doneQ++; continue; }
				// Same hard box fit as the feed: 7 quest lines + the "N done" line + the "(+N more)"
				// line = 9, all that the 144px page holds before the text spills into the alert dock.
				if (listed >= 7) { overflow++; continue; }
				listed++;
				behind.Sort();
				string verdict = !anyReport ? "|cff707070no reports|r"
					: "|cffffc040waiting: " + EscLua(TruncRaw(string.Join(", ", behind), 26)) + "|r";
				quests.AppendFormat("|cffffffff{0}|r {1}\\n", EscLua(TruncRaw(q.Name, 24)), verdict);
			}
			var questsOut = new System.Text.StringBuilder();
			if (doneQ > 0) questsOut.Append("|cff40ff40").Append(doneQ).Append(" done|r\\n");
			questsOut.Append(quests);
			if (overflow > 0) questsOut.Append("|cff707070(+").Append(overflow).Append(" more)|r");
			if (questsOut.Length == 0) questsOut.Append("|cff707070no quests in your log|r");

			// Feeds: newest first — mid-combat the leader reads "what just fired" top-down.
			// ⚠ The line count is a HARD box fit, not a preference: 3.3.5 FontStrings are not clipped
			// by their parent frame, so an extra line spills over the divider into the alert dock.
			// The status lines collapse when empty (Lua `status()`), so the feed's box grows with them:
			// 96px under the quick buttons, minus 14 per shown status line, at ~14px a line.
			string water = ComposeWaterLine(), bags = ComposeBagsLine(), errand = ComposeErrandLine();
			int statusLines = (water.Length > 0 ? 1 : 0) + (bags.Length > 0 ? 1 : 0) + (errand.Length > 0 ? 1 : 0);
			int mainFeedLines = (96 - 14 * statusLines) / 14;
			var feed = new System.Text.StringBuilder();
			for (int i = _alerts.Count - 1; i >= 0 && i >= _alerts.Count - mainFeedLines; i--)
				feed.Append(AlertLine(_alerts[i])).Append("\\n");
			if (feed.Length == 0) feed.Append("|cff707070no alerts yet|r");

			var dock = new System.Text.StringBuilder();
			for (int i = _alerts.Count - 1; i >= 0 && i >= _alerts.Count - 2; i--)
				dock.Append(AlertLine(_alerts[i])).Append("\\n");
			if (dock.Length == 0) dock.Append("|cff707070no alerts yet|r");

			string plainArr = string.Join(",", plain.Select(n => "'" + n + "'"));
			string dispArr = string.Join(",", disp.Select(n => "'" + n + "'"));
			string infoArr = string.Join(",", infos.Select(n => "'" + n + "'"));
			Lua.DoString("if VibePartyPanelApply and VibePartyPanel:IsShown() then VibePartyPanelApply('"
				+ head + "', '" + feed + "', '" + dock + "', '" + questsOut + "', {"
				+ plainArr + "}, {" + dispArr + "}, {" + infoArr + "}, '" + water
				+ "', '" + bags + "', '" + errand + "') end");
		}

		private void OnPartyChat(WoWChat.ChatLanguageSpecificEventArgs e)
		{
			if (_botMessage == null) return;
			WoWPlayer? leader = ObjectManager.GetObjectByGuid<WoWPlayer>(_botMessage.LeaderGuid);
			if (leader == null || e.Author != leader.Name) return;

			// Typed fallback for orders. '#' — NOT '!' or '.': the server (acore) parses those chat
			// prefixes as GM-command syntax and eats the message ("Command \"follow\" does not exist").
			foreach (string raw in e.Message.Split(new[] { "#" }, StringSplitOptions.RemoveEmptyEntries))
				ExecuteOrder(raw);
		}

		// One order = "cmd" or "cmd Name" (targeted at ONE follower — everyone else drops it).
		// Orders arrive over the PartyBus ("Order", published from the leader panel) or as typed
		// '#cmd' party chat — one grammar, one executor.
		// ⚠ New commands here also go in the LeaderCommands panel table.
		private void ExecuteOrder(string raw)
		{
			string token = raw.Trim();
			if (token.Length == 0) return;
			int sp = token.IndexOf(' ');
			if (sp > 0)
			{
				string arg = token.Substring(sp + 1).Trim();
				token = token.Substring(0, sp);
				if (arg.Length > 0 && !arg.Equals(StyxWoW.Me.Name, StringComparison.OrdinalIgnoreCase))
					return;
			}
			WoWPlayer? leader = _botMessage != null ? ObjectManager.GetObjectByGuid<WoWPlayer>(_botMessage.LeaderGuid) : null;
			{
				switch (token.ToLowerInvariant())
				{
					case "dance":
						Logging.Write("VibeParty: Dancing");
						Lua.DoString("DoEmote('Dance')");
						break;
					case "leavedungeon":
						Logging.Write("VibeParty: Leaving Dungeon");
						Lua.DoString("LFGTeleport(1)");
						break;
					case "enterdungeon":
						Logging.Write("VibeParty: Entering Dungeon");
						Lua.DoString("LFGTeleport(0)");
						break;
					case "leavegroup":
						// Dungeon reset: the instance is bound to the GROUP, so porting out doesn't
						// free it — the party has to actually dissolve. Everyone drops their own
						// party (the leader included); the leader's AutoInviteTick reforms a NEW
						// group once it sees its own party empty.
						Logging.Write("VibeParty: leavegroup — leaving the party.");
						_disbanding = true;
						Lua.DoString("LeaveParty()");
						break;
					case "clearpoi":
						BotPoi.Clear("Leader said so");
						break;
					case "leavebattleground":
						Logging.Write("VibeParty: Leaving Battleground");
						Battlegrounds.LeaveBattlefield();
						break;
					case "forcetrain":
						Logging.Write("VibeParty: Someone told me to train.");
						Vendors.ForceTrainer = true;
						break;
					case "forcesell":
						Logging.Write("VibeParty: Someone told me to go sell.");
						Vendors.ForceSell = true;
						break;
					case "forcerepair":
						Logging.Write("VibeParty: Someone told me to repair.");
						Vendors.ForceRepair = true;
						break;
					case "forcemail":
						Logging.Write("VibeParty: Someone told me to mail.");
						Vendors.ForceMail = true;
						break;
					case "vendor":
						// The whole town errand in one word — the vendor tree resolves the rest
						// (nearest sell/repair from data.bin, disposition sweeps, mail if a
						// recipient is set; NeedToMail self-guards when it isn't).
						Logging.Write("VibeParty: leader said vendor — forcing a sell/repair/mail run.");
						Vendors.ForceSell = true;
						Vendors.ForceRepair = true;
						Vendors.ForceMail = true;
						break;
					case "hearth":
						// Arm the same retry window the leader-cast sync uses — but NOT as a
						// leader-cast window, so the abort-mirror never cancels an ordered hearth.
						Logging.Write("VibeParty: leader said hearth — using Hearthstone.");
						_hearthPendingUntil = DateTime.UtcNow.AddSeconds(30);
						_hearthAttempted = false;
						_hearthFromLeaderCast = false;
						break;
					case "dismount":
						Mount.Dismount("Request from Leader");
						break;
					case "mountup":
						Mount.MountUp(new LocationRetriever(() => WoWPoint.Zero));
						break;
					case "wait":
						_waiting = !_waiting;
						Logging.Write("VibeParty: leader said wait — {0}.", _waiting ? "holding position" : "resuming");
						PublishAlert(false, _waiting ? "holding position" : "resuming");
						if (_waiting)
						{
							// Idling the tree does NOT stop movement already in flight: native /follow
							// is client-persistent glue and keeps dragging us — break it or the hold
							// reads as a no-op. The queued backpedal tap covers the stationary-glue
							// case MoveStop can't (same mechanism as rest entry).
							WoWMovement.MoveStop();
							QueueFollowBreak();
						}
						break;
					case "interact":
						// Bus orders arrive regardless of distance (chat implied proximity, the bus
						// does not) — the leader or its target may not be in OUR object manager.
						// ignoreTimer: an explicit order must not be swallowed by the 2s spam guard.
						if (leader?.CurrentTarget != null)
						{
							Logging.Write("VibeParty: leader said interact — interacting with {0}.", leader.CurrentTarget.Name);
							leader.CurrentTarget.Interact(ignoreTimer: true);
						}
						else
							Logging.Write("VibeParty: interact order — leader or its target is not visible from here.");
						break;
					case "follow":
						// Manual "everyone follow me" — clear any wait-hold and snap to native /follow now.
						_waiting = false;
						if (leader != null && !StyxWoW.Me.Combat)
						{
							Logging.Write("VibeParty: leader said follow — engaging follow.");
							Lua.DoString(string.Format("FollowUnit('{0}', true)", leader.Name));
						}
						else
							Logging.Write("VibeParty: follow order — {0} (hold cleared; the follow tree takes over when it can).",
								leader == null ? "leader not visible from here" : "in combat");
						break;
				}
			}
		}

		// ──────────────────────────────────────────────────────────────────────
		// IncludeTargetsFilter — smethod_2
		// ──────────────────────────────────────────────────────────────────────

		private static void IncludeTargetsFilter(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
		{
			bool inParty = StyxWoW.Me.IsInParty;
			bool inRaid  = StyxWoW.Me.IsInRaid;
			// PartyMemberGuids excludes SELF (PartyDefenseTarget re-adds it for the same reason). Without it,
			// a mob whose only target is ME never enters the list — and IsInCombatState()'s FirstUnit gate
			// then holds the combat branch closed while I'm being hit (follower frozen mid-fight).
			List<ulong> partyGuids = StyxWoW.Me.PartyMemberGuids.ToList();
			List<ulong> raidGuids  = StyxWoW.Me.RaidMemberGuids.ToList();
			partyGuids.Add(StyxWoW.Me.Guid);
			raidGuids.Add(StyxWoW.Me.Guid);
			List<WoWPlayer>? members = inParty ? StyxWoW.Me.PartyMembers :
			                           inRaid  ? StyxWoW.Me.RaidMembers  : null;

			// Always include the leader's target so we register combat and can assist it — this is
			// what makes IsInCombatState() (which requires a FirstUnit) fire when the leader pulls,
			// before any mob is actively swinging at a party member.
			WoWUnit? leaderTarget = LeaderAssistTarget();
			if (leaderTarget != null)
				outgoing.Add(leaderTarget);

			foreach (WoWObject obj in incoming)
			{
				WoWUnit? unit = obj as WoWUnit;
				if (unit == null || !unit.Combat) continue;

				if (inParty && partyGuids.Contains(unit.CurrentTargetGuid))
				{
					outgoing.Add(unit);
					continue;
				}
				if (inRaid && raidGuids.Contains(unit.CurrentTargetGuid))
				{
					outgoing.Add(unit);
					continue;
				}
				if (members != null)
				{
					foreach (WoWPlayer member in members)
					{
						if (IsMemberThreatened(member, unit) && !outgoing.Contains(unit))
						{
							outgoing.Add(unit);
							break;
						}
					}
				}
			}
		}

		// smethod_3 — member threat helper
		private static bool IsMemberThreatened(WoWPlayer member, WoWUnit unit)
		{
			bool hasThreat = member.GetThreatInfoFor(unit).ThreatStatus >= ThreatStatus.NoobishTank;
			bool minionTargets = member.Minions.Any(m => m.CurrentTargetGuid == unit.Guid);
			return hasThreat || minionTargets;
		}

		// ──────────────────────────────────────────────────────────────────────
		// WeighTargetsFilter — smethod_4
		// ──────────────────────────────────────────────────────────────────────

		private static void WeighTargetsFilter(List<Targeting.TargetPriority> targets)
		{
			foreach (Targeting.TargetPriority tp in targets)
			{
				if (tp.Object == null || !(tp.Object is WoWUnit)) continue;
				WoWUnit unit = (WoWUnit)tp.Object;
				if (RaFHelper.Leader != null
					&& RaFHelper.Leader.CurrentTargetGuid == unit.Guid
					&& RaFHelper.Leader.CurrentTarget != null
					&& RaFHelper.Leader.CurrentTarget.GetThreatInfoFor(StyxWoW.Me).ThreatStatus >= ThreatStatus.SecurelyTanking)
				{
					tp.Score += 200.0;
				}
			}
		}

		// ──────────────────────────────────────────────────────────────────────
		// Follow behavior — smethod_0
		// ──────────────────────────────────────────────────────────────────────

		// Leader buff upkeep — keep the loaded routine's buffs on self + party (paladin blessings/seals, mage
		// Intellect, …) so a manually-driven leader never hand-rebuffs. OUT OF COMBAT only: buff casts then never
		// contend for a GCD or touch your current target while you're fighting (user decision 2026-07-07). Buff
		// maintenance ONLY — no movement/targeting/rotation; the routine's own HasAura/CanCast gates keep it silent
		// when everything's up. Mirrors the combat branch's pre-combat-buff shape (Behavior prop or Need/method).
		private static Composite CreateLeaderBuffBehavior()
		{
			return new Decorator(ctx => !StyxWoW.Me.Combat && RoutineManager.Current != null,
				new PrioritySelector(
					new Decorator(ctx => RoutineManager.Current.PreCombatBuffBehavior != null, RoutineManager.Current.PreCombatBuffBehavior!),
					new Decorator(ctx => RoutineManager.Current.NeedPreCombatBuffs,
						new TreeSharp.Action(ctx => RoutineManager.Current.PreCombatBuff()))
				));
		}

		private static Composite CreateFollowBehavior()
		{
			return new PrioritySelector(
				ctx => _botMessage,                                // context = botMessage
				new DecoratorIsNotPoiType(
					new[] { PoiType.Loot, PoiType.Harvest, PoiType.Skin, PoiType.Train, PoiType.Sell, PoiType.Kill },
					new PrioritySelector(
						// if botMessage is null → succeed without doing anything
						new Decorator(ctx => ctx == null, new ActionAlwaysSucceed()),
						// switch on Message type
						new Switch<string>(
							ctx => ctx is BotMessage m ? m.Message : null,
							new SwitchArgument<string>("Vendor",
								new TreeSharp.Action(ctx =>
								{
									Logging.WriteDebug("VibeParty: Vendoring");
									if (_botMessage != null)
									{
										WoWUnit? vendor = ObjectManager.GetObjectByGuid<WoWUnit>(_botMessage.TargetGuid);
										if (vendor != null)
											BotPoi.Current = new BotPoi(vendor, vendor.IsRepairMerchant ? PoiType.Repair : PoiType.Sell);
									}
								})),
							new SwitchArgument<string>("FollowLeader",
								new TreeSharp.Action(ctx => FollowLeader()))
						),
						// if too far from leader → navigate there
						new Decorator(
							ctx => LeaderLocation.Distance(StyxWoW.Me.Location) > 40f,
							new Sequence(
								new TreeSharp.Action(ctx => TreeRoot.StatusText = "Moving to leader"),
								new NavigationAction(ctx => LeaderLocation)
							)
						)
					)
				)
			);
		}

		// FollowLeader helper — smethod_18
		private static void FollowLeader()
		{
			if (_botMessage == null) return;

			// (1) Cast-hold: moving cancels a hardcast (instants don't set IsCasting, so they never block).
			if (StyxWoW.Me.IsCasting)
			{
				MoveMode("cast-hold");
				if (StyxWoW.Me.IsMoving)
					WoWMovement.MoveStop();
				return;
			}

			// (2) OWN combat → the ROUTINE owns all movement (user directive 2026-07-16); the combat-entry
			// tap already broke any /follow glue. Hands off — no approach, no hold, no MoveStop.
			if (StyxWoW.Me.Combat)
			{
				MoveMode("combat — routine owns movement");
				return;
			}
			// (2b) Leader fighting, we're not → NEVER native-/follow (glue walks to 2yd behind the fighting
			// tank = melee of its mob); mesh-navigate toward the fight instead, and the combat branch takes
			// over on combat entry. LeaderInCombat is a bus message and lags the fight — read the leader's
			// live OM combat too. No assist target yet (leader lining up / friendly target) → close on the
			// leader at the user's FollowDistance (that knob is authoritative spacing intent).
			WoWPlayer? leaderUnit = ObjectManager.GetObjectByGuid<WoWPlayer>(_botMessage.LeaderGuid);
			if (_botMessage.LeaderInCombat || (leaderUnit != null && leaderUnit.Combat))
			{
				WoWUnit? assist = LeaderAssistTarget();
				WoWPoint dest = assist != null ? assist.Location : LeaderLocation;
				double stopRange = assist != null ? Targeting.PullDistance : VibePartySettings.Instance.FollowDistance;
				// Hysteresis: this stop is level-triggered, so without the latch a jittering tanked mob
				// becomes a MoveTo/MoveStop-per-tick stutter.
				if (_followClosing) stopRange -= PosSlack(stopRange);
				if (dest.Distance(StyxWoW.Me.Location) > stopRange)
				{
					MoveMode("combat approach → " + (assist != null ? assist.Name : "leader"));
					_followClosing = true;
					Navigator.MoveTo(dest);
				}
				else
				{
					MoveMode(assist != null ? "combat hold (in range of " + assist.Name + ")" : "combat hold (near leader)");
					_followClosing = false;
					if (StyxWoW.Me.IsMoving)
						WoWMovement.MoveStop();
				}
				return;
			}

			// (3) Resting (eat/drink/regen) → hold position; native follow would drag us off the drink the moment
			// the leader drifts. Downtime only — combat above already took priority. 2026-07-07 user directive.
			if (RoutineManager.Current != null && RoutineManager.Current.NeedRest)
			{
				MoveMode("rest hold");
				if (StyxWoW.Me.IsMoving)
					WoWMovement.MoveStop();
				return;
			}

			// (4) Awaiting a water handoff → hold still so the party mage can reach and trade us.
			if (_partyWater != null && _partyWater.AwaitingWater)
			{
				MoveMode("water-handoff hold");
				if (StyxWoW.Me.IsMoving)
					WoWMovement.MoveStop();
				return;
			}

			WoWPlayer? leader = leaderUnit;
			// In instances DON'T idle inside FollowDistance — native /follow must engage as soon as we're out of
			// combat (cases 2/3 above still let us fight and rest), or the follower waits for a ~FollowDistance gap
			// before it starts moving and loses the leader on dungeon stairs/tight corners (user 2026-07-14).
			// Native /follow has its own ~few-yd stop distance, so staying engaged doesn't crowd the leader. Open
			// world keeps the gate (FollowDistance spacing is the point there).
			if (!StyxWoW.Me.IsInInstance && leader != null && leader.Distance <= VibePartySettings.Instance.FollowDistance)
			{
				MoveMode("idle near leader");
				if (!leader.Mounted)
					Mount.Dismount("Leader is not mounted");
				if (leader.CastingSpell != null)
				{
					MountType mt = (MountType)leader.CastingSpell.SpellEffect1.MiscValueB;
					if (mt == MountType.EpicGroundOnly || mt == MountType.Ground)
						Mount.MountUp(new LocationRetriever(() => WoWPoint.Zero));
				}
				StyxWoW.ResetAfk();
				return;
			}

			// Try to mount up if needed
			if ((leader == null && Mount.ShouldMount(LeaderLocation)) || (leader != null && leader.Mounted))
			{
				if (!StyxWoW.Me.Mounted)
					Mount.MountUp(new LocationRetriever(() => LeaderLocation));
			}

			if (leader != null)
			{
				// Smooth native /follow (the flawless multibox-style follow). We follow by
				// NAME, not focus: SetFocus was a raw write to the focus-GUID memory slot
				// that Lua's "focus" unit never sees, so FollowUnit('focus') threw the red
				// "Unknown unit" error. FollowUnit(name, exact) resolves reliably. Once
				// /follow starts the client drives movement (IsCTMing set) — don't re-issue
				// it or mesh-nav over it, or they fight.
				bool ctmActive = (WoWMovement.ActiveInputControl.Flags & WoWMovement.MovementDirection.IsCTMing)
					!= WoWMovement.MovementDirection.None;

				if ((leader.IsFlying || leader.IsSwimming) && leader.InLineOfSight)
				{
					MoveMode("ctm chase (leader flying/swimming)");
					WoWMovement.ClickToMove(LeaderLocation);
				}
				else if (StyxWoW.Me.IsInInstance)
				{
					MoveMode("instance follow");
					InstanceFollow(leader, ctmActive);
				}
				else if (leader.Distance <= 20.0 && leader.InLineOfSight)
				{
					MoveMode("native /follow " + leader.Name);
					if (!ctmActive)
						Lua.DoString(string.Format("FollowUnit('{0}', true)", leader.Name));
				}
				else if (leader.Distance >= VibePartySettings.Instance.FollowDistance)
				{
					// Out of range or no LoS: mesh-nav to catch up; native follow resumes in range.
					MoveMode("mesh catch-up to leader");
					Navigator.MoveTo(LeaderLocation);
				}
			}
			else
			{
				MoveMode("mesh catch-up to leader (no object)");
				Navigator.MoveTo(LeaderLocation);
			}
		}

		// Rest-entry follow break (user 2026-07-12): ONE tap of the backpedal key when rest begins. The
		// hold-position MoveStop in FollowLeader case (3) only fires if we happen to be MOVING when rest
		// starts, so /follow glue armed while the leader stands still survives into the rest and drags us
		// off the drink at his first step. A movement INPUT always cancels /follow, and rest issues no CTM,
		// so nothing re-arms it. Press on one pulse, release on the next (a tap, not a walk); skipped if a
		// Food/Drink aura is already up (the tap would cancel it — the reactive MoveStop covers that path).
		// Lives in Pulse, not the tree: a release inside a tree branch can be orphaned mid-step and leave
		// the walk key held forever (the GVHunter run-7 moonwalk).
		private static bool _restStepDone;

		private static void RestEntryFollowBreak()
		{
			bool needRest = RoutineManager.Current != null && RoutineManager.Current.NeedRest;
			if (!needRest) { _restStepDone = false; return; }
			if (_restStepDone) return;
			var me = StyxWoW.Me;
			if (me.Combat || me.IsCasting || me.Mounted || me.HasAura("Drink") || me.HasAura("Food")) return;
			_restStepDone = true;
			QueueFollowBreak();
		}

		// The /follow break tap: /follow has no cancel API — only a movement INPUT breaks it (MoveStop
		// does nothing for glue armed while stationary). Atomic press→sleep→release (the BackHop idiom)
		// so the key can never orphan; the hold only needs the client to render ONE frame with the key
		// down — 70ms covers ~15 fps.
		private static bool _followBreakQueued;
		private const int FollowBreakTapMs = 70;

		private static void QueueFollowBreak() => _followBreakQueued = true;

		private static void PulseFollowBreak()
		{
			// Defer while casting — the tap is a movement input and would cancel a hardcast; a casting
			// character is stationary, so the glue isn't dragging it anywhere meanwhile.
			if (!_followBreakQueued || StyxWoW.Me.IsCasting) return;
			_followBreakQueued = false;
			WoWMovement.Move(WoWMovement.MovementDirection.Backwards);
			System.Threading.Thread.Sleep(FollowBreakTapMs);
			WoWMovement.MoveStop(WoWMovement.MovementDirection.Backwards);
		}

		// Bag-visibility sync (follower): mirror the leader's open/closed bag UI, edge-triggered — apply only
		// on state CHANGE so a follower can still open its own bags without a per-tick fight. Explicit
		// Open/CloseBag (never OpenAllBags — it toggles) keeps the apply idempotent.
		private static bool _bagsMirrorOpen;

		private static void MirrorLeaderBagsTick()
		{
			if (_botMessage == null || _botMessage.LeaderBagsOpen == _bagsMirrorOpen) return;
			_bagsMirrorOpen = _botMessage.LeaderBagsOpen;
			Logging.WriteDebug("[VibeParty] mirroring leader's bags: {0}.", _bagsMirrorOpen ? "OPEN" : "closed");
			// OpenAllBags/CloseAllBags are the entry points every bag addon hooks (ElvUI replaces the
			// stock frames — direct OpenBag() bypasses it). OpenAllBags TOGGLES in the stock UI, so it
			// only fires when the same addon-aware detector as the leader's says we're actually closed.
			Lua.DoString(_bagsMirrorOpen
				? "local o=0 if ElvUI_ContainerFrame and ElvUI_ContainerFrame:IsShown() then o=1 end " +
				  "if o==0 then for i=1,(NUM_CONTAINER_FRAMES or 13) do local f=_G['ContainerFrame'..i] if f and f:IsShown() then o=1 end end end " +
				  "if o==0 then OpenAllBags() end"
				: "CloseAllBags()");
		}

		// Dungeon follow: native /follow is the PRIMARY. Instance mmaps are holey (missing tiles, and doors
		// that open on approach are not baked into the mesh), so mesh-nav strands followers ("general pathing /
		// missing navmesh", user 2026-07-13). Native /follow retraces the leader's ACTUAL walked path — through
		// the same doors and geometry — so it is mesh-FREE and cannot hit a hole the mesh has. Mesh-nav is only
		// the last resort when we have fallen out of follow range. We give up FollowDistance spacing during
		// travel (native /follow has a fixed ~2-3yd distance in 3.3.5a, not tunable), but only during travel:
		// the combat-entry MoveStop kills the glue on engage and the routine positions us for the fight, so
		// healer/ranged spacing is recovered exactly where it matters. (Superseded the 2026-07-12 mesh-first
		// InstanceFollow — its spacing win was not worth the dungeon-nav failures.)
		private const float InstanceFollowRange = 40f;   // native /follow range; beyond it, mesh-nav to catch up

		private static void InstanceFollow(WoWPlayer leader, bool ctmActive)
		{
			if (leader.Distance <= InstanceFollowRange)
			{
				// No LoS gate: native /follow paths toward the unit around corners the mesh cannot cover.
				if (!ctmActive)
					Lua.DoString(string.Format("FollowUnit('{0}', true)", leader.Name));
			}
			else
			{
				Navigator.MoveTo(LeaderLocation);   // fell out of follow range — mesh-nav to catch up, then follow resumes
			}
		}

		// smethod_57 — in-combat condition
		// true when: have a target AND (not mounted AND (in own combat, or an assist target in engage range)),
		//            or pet is in combat
		private static bool IsInCombatState()
		{
			if (Targeting.Instance.FirstUnit == null) return false;
			if (!StyxWoW.Me.Mounted)
			{
				if (StyxWoW.Me.Combat) return true;
				// Anchored on the FIGHT, not the leader: an assist target (leader's pull or party defense)
				// within engage range IS our fight wherever the leader stands. A distance-to-LEADER gate here
				// starves any follower outside it — it stands silent until the mob walks over and hits it.
				WoWUnit? assist = LeaderAssistTarget();
				if (assist != null && assist.Distance <= DefenseEngageRange) return true;
			}
			return StyxWoW.Me.GotAlivePet && StyxWoW.Me.Pet!.Combat;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Assist targeting — focus-fire the leader's target (this is what drives the routine)
		// ──────────────────────────────────────────────────────────────────────

		// The leader's broadcast target, but only if it's a live enemy we can attack (hostile or
		// neutral). Excludes the leader's friendly target (e.g. a healer-leader's heal target).
		private static WoWUnit? LeaderAssistTarget()
		{
			// Tier 0 — "kill the runner!": a fleeing, execute-range mob on the party's threat table
			// outranks even the leader's pull. The tank never retargets a runner while the pack hits
			// him — peeling it is exactly the followers' job: it dies in a GCD or two, and left alone
			// it comes back with friends.
			WoWUnit? runner = PartyDefenseTarget(runnersOnly: true);
			if (runner != null) return runner;

			// Tier 1 — the leader's target, only while the leader is actually IN combat (pull is on),
			// not while it's just targeting a mob to mark/inspect. Prefer the LIVE OM read over the bus —
			// the bus lags the fight (same lesson the mover's 2b branch already carries); the bus covers a
			// leader outside OM range.
			if (_botMessage != null)
			{
				WoWPlayer? leader = ObjectManager.GetObjectByGuid<WoWPlayer>(_botMessage.LeaderGuid);
				bool leaderFighting = leader != null ? leader.Combat : _botMessage.LeaderInCombat;
				ulong targetGuid = leader != null && leader.CurrentTargetGuid != 0
					? leader.CurrentTargetGuid : _botMessage.LeaderTargetGuid;
				if (leaderFighting && targetGuid != 0)
				{
					WoWUnit? t = ObjectManager.GetObjectByGuid<WoWUnit>(targetGuid);
					if (t != null && !t.Dead && t.Attackable && (t.IsHostile || t.IsNeutral)) return t;
				}
			}
			// Tier 2 — party defense: a mob already fighting ANY member (or a member's pet) is party
			// business whether or not the human leader ever targets it. Before this, only the aggroed
			// bot fought while the rest stood watching.
			return PartyDefenseTarget();
		}

		private const float DefenseEngageRange = 40f;   // assist-range norm; keeps stray far aggro from dragging the party
		// Natural flee starts at ≤~25% hp; the gate keeps CC-feared healthy mobs (a priest's Psychic
		// Scream peel carries the same unit flag) from being nuked back awake, while every genuine
		// runner passes.
		private const float RunnerHealthPct = 35f;
		private static DateTime _defenseLogAt = DateTime.MinValue;

		private static WoWUnit? PartyDefenseTarget(bool runnersOnly = false)
		{
			LocalPlayer me = StyxWoW.Me;
			bool inParty = me.IsInParty;
			bool inRaid = me.IsInRaid;
			if (!inParty && !inRaid) return null;

			var guids = new HashSet<ulong>(inRaid ? me.RaidMemberGuids : me.PartyMemberGuids) { me.Guid };
			List<WoWPlayer>? members = inParty ? me.PartyMembers : inRaid ? me.RaidMembers : null;

			WoWUnit? best = null;
			double bestDist = double.MaxValue;
			foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
			{
				if (u == null || u.Dead || !u.Combat || !u.Attackable) continue;
				if (!u.IsHostile && !u.IsNeutral) continue;
				if (runnersOnly && (!u.Fleeing || u.HealthPercent >= RunnerHealthPct)) continue;
				double d = u.Distance;
				if (d > DefenseEngageRange || d >= bestDist) continue;
				bool onParty = guids.Contains(u.CurrentTargetGuid);
				if (!onParty && members != null)
					foreach (WoWPlayer member in members)
						if (IsMemberThreatened(member, u)) { onParty = true; break; }
				if (!onParty) continue;
				bestDist = d;
				best = u;
			}
			if (best != null && DateTime.UtcNow > _defenseLogAt.AddSeconds(5))
			{
				_defenseLogAt = DateTime.UtcNow;
				if (runnersOnly)
					Logging.Write("VibeParty: killing the runner — {0} ({1:0}%).", best.Name, best.HealthPercent);
				else
				{
					WoWUnit? victim = ObjectManager.GetObjectByGuid<WoWUnit>(best.CurrentTargetGuid);
					Logging.Write("VibeParty: defending {0} — engaging {1}.", victim != null ? victim.Name : "the party", best.Name);
				}
			}
			return best;
		}

		private static bool ShouldAssistLeaderTarget()
		{
			WoWUnit? enemy = LeaderAssistTarget();
			if (enemy == null) return false;
			if (StyxWoW.Me.CurrentTargetGuid == enemy.Guid) return false;   // already focus-firing it
			// Don't yank a landing hardcast off a still-valid target; switch on the next tick.
			WoWUnit? cur = StyxWoW.Me.CurrentTarget;
			if (StyxWoW.Me.IsCasting && cur != null && !cur.Dead) return false;
			return true;
		}

		private static void AssistLeaderTarget() => LeaderAssistTarget()?.Target();

		// Hysteresis: stopping at the first tick inside range parks us ON the re-trigger boundary, and a
		// tanked mob jitters ±1-2yd — every wiggle re-triggers a one-step approach (stutter-step). While
		// approaching, close the slack inside; slack shrinks with the range so FollowDistance never hits 0.
		private static double PosSlack(double range) => Math.Min(4.0, range * 0.4);

		// ──────────────────────────────────────────────────────────────────────
		// Combat behavior — smethod_5
		// ──────────────────────────────────────────────────────────────────────

		private static Composite CreateCombatBehavior()
		{
			return new PrioritySelector(
				// Dismount if should
				new Decorator(ctx => Mount.ShouldDismount(BotPoi.Current.Location),
					new TreeSharp.Action(ctx => Mount.Dismount("Combat"))),

				new PrioritySelector(
					// Cancel skinning cast if POI is not Skin and we have pending skinning spell
					new Decorator(ctx => BotPoi.Current.Type != PoiType.Skin && StyxWoW.Me.HasPendingSpell("Skinning"),
						new TreeSharp.Action(ctx => Lua.DoString("SpellStopTargeting()"))),

					// If POI unit is dead → clear target and POI
					new DecoratorIsPoiType(PoiType.Kill,
						new PrioritySelector(
							new Decorator(ctx => BotPoi.Current.AsObject != null && BotPoi.Current.AsObject.ToUnit().Dead,
								new Sequence(
									new TreeSharp.Action(ctx => StyxWoW.Me.ClearTarget()),
									new TreeSharp.Action(ctx => BotPoi.Clear())
								)
							)
						)
					),

					// Not in combat: Rest + PreCombatBuff + Kill approach
					new Decorator(ctx => !StyxWoW.Me.Combat,
						new PrioritySelector(
							// Rest
							new PrioritySelector(
								new Decorator(ctx => RoutineManager.Current.RestBehavior != null, RoutineManager.Current.RestBehavior!),
								new Decorator(ctx => RoutineManager.Current.NeedRest,
									new Sequence(
										new ActionSetActivity("Resting"),
										new TreeSharp.Action(ctx => RoutineManager.Current.Rest())
									)
								)
							),
							// PreCombatBuff
							new PrioritySelector(
								new Decorator(ctx => RoutineManager.Current.PreCombatBuffBehavior != null, RoutineManager.Current.PreCombatBuffBehavior!),
								new Decorator(ctx => RoutineManager.Current.NeedPreCombatBuffs,
									new Sequence(
										new ActionSetActivity("Applying pre-combat buffs"),
										new TreeSharp.Action(ctx => RoutineManager.Current.PreCombatBuff())
									)
								)
							),
							// Pull target selection: if no target or target is dead, pick from target list
							new DecoratorIsPoiType(PoiType.Kill,
								new PrioritySelector(
									// If no target or target is dead → set target to FirstUnit
									new Decorator(ctx => !StyxWoW.Me.GotTarget || (StyxWoW.Me.GotTarget && StyxWoW.Me.CurrentTarget.Dead && Targeting.Instance.FirstUnit != null),
										new Sequence(
											new TreeSharp.Action(ctx => Logging.WriteDebug("Setting target to pull")),
											new TreeSharp.Action(ctx => Targeting.Instance.FirstUnit!.Target())
										)
									),
									// Move to pull target if too far
									new Decorator(ctx => BotPoi.Current.AsObject != null && BotPoi.Current.AsObject.ToUnit() != null && BotPoi.Current.AsObject.ToUnit().Distance > Targeting.PullDistance,
										new Sequence(
											new TreeSharp.Action(ctx => Logging.WriteDebug("Moving to pull target")),
											new NavigationAction(ctx => BotPoi.Current.Location)
										)
									)
								)
							),
							// Update POI to best target from TargetList (when in Kill POI)
							new DecoratorIsPoiType(PoiType.Kill,
								new PrioritySelector(
									new Decorator(ctx => Targeting.Instance.TargetList.Count != 0,
									new Decorator(ctx => BotPoi.Current.AsObject != Targeting.Instance.FirstUnit && BotPoi.Current.Type == PoiType.Kill,
											new Sequence(
												new ActionDebugString("Current POI is not the best pull target. Changing."),
												new ActionSetPoi(true, ctx => new BotPoi(Targeting.Instance.FirstUnit!, PoiType.Kill)),
												new TreeSharp.Action(ctx => BotPoi.Current.AsObject.ToUnit().Target())
											)
										)
									),
									// Pull if close enough and has target
									new Decorator(ctx => StyxWoW.Me.CurrentTarget != null,
										new PrioritySelector(
											new Decorator(ctx => RoutineManager.Current.PullBuffBehavior != null, RoutineManager.Current.PullBuffBehavior!),
											new Decorator(ctx => RoutineManager.Current.PullBehavior != null,
												new Sequence(
													new ActionSetActivity("Pulling"),
													RoutineManager.Current.PullBehavior!
												)
											)
										)
									)
								)
							)
						)
					),

					// In combat — smethod_57
					new Decorator(ctx => IsInCombatState(),
						new PrioritySelector(
							// Combat entry, ONE SHOT: break any /follow glue (client-persistent, no cancel API —
							// only a movement INPUT breaks it; MoveStop does nothing for glue armed while
							// stationary). In combat the ROUTINE owns all movement (user directive 2026-07-16) —
							// nothing in this branch may move or stop the character.
							new Decorator(ctx => !_combatEntryStopDone,
								new TreeSharp.Action(ctx =>
								{
									_combatEntryStopDone = true;
									QueueFollowBreak();
									Logging.WriteDebug("VibeParty: combat entry — breaking follow");
									return RunStatus.Failure;   // free action — never eats the pass
								})),
							// Assist the leader — focus-fire its target so the routine always has an
							// enemy. One-tick target switch, then falls through to Heal/Buff/Combat.
							new Decorator(ctx => ShouldAssistLeaderTarget(),
								new TreeSharp.Action(ctx => AssistLeaderTarget())),
							// Dismount if needed
							new Decorator(ctx => StyxWoW.Me.Mounted,
								new TreeSharp.Action(ctx => Mount.Dismount("Combat"))),
							// Move to POI target if too far (grind-style; skipped when assisting a leader —
							// the routine's own approach owns movement in that case).
							new Decorator(ctx => LeaderAssistTarget() == null && BotPoi.Current.AsObject != null && BotPoi.Current.AsObject.Distance > Targeting.PullDistance,
								new Sequence(
									new TreeSharp.Action(ctx =>
									{
										TreeRoot.StatusText = "Moving to target";
										MoveMode("poi approach → " + BotPoi.Current.AsObject!.Name);
									}),
									new NavigationAction(ctx => BotPoi.Current.AsObject!.Location)
								)
							),
							// Heal
							new PrioritySelector(
								new Decorator(ctx => RoutineManager.Current.HealBehavior != null, RoutineManager.Current.HealBehavior!),
								new Decorator(ctx => RoutineManager.Current.NeedHeal,
									new Sequence(
										new ActionSetActivity("Healing"),
										new TreeSharp.Action(ctx => RoutineManager.Current.Heal())
									)
								)
							),
							// CombatBuff
							new PrioritySelector(
								new Decorator(ctx => RoutineManager.Current.CombatBuffBehavior != null, RoutineManager.Current.CombatBuffBehavior!),
								new Decorator(ctx => RoutineManager.Current.NeedCombatBuffs,
									new Sequence(
										new ActionSetActivity("Applying combat buffs"),
										new TreeSharp.Action(ctx => RoutineManager.Current.CombatBuff())
									)
								)
							),
							// Combat
							new PrioritySelector(
								new Decorator(ctx => RoutineManager.Current.CombatBehavior != null, RoutineManager.Current.CombatBehavior!),
								new Sequence(
									new ActionSetActivity("Combat"),
									new TreeSharp.Action(ctx => RoutineManager.Current.Combat())
								)
							)
						)
					)
				)
			);
		}

		// ──────────────────────────────────────────────────────────────────────
		// Death behavior — smethod_7
		// ──────────────────────────────────────────────────────────────────────

		private static Composite CreateDeathBehavior()
		{
			return new PrioritySelector(
				// Accept an incoming rez the moment it lands — released or not (DungeonBuddy pattern).
				new Decorator(ctx => _resurrectPending,
					new TreeSharp.Action(ctx =>
					{
						_resurrectPending = false;
						Lua.DoString("if StaticPopup1 and StaticPopup1:IsVisible() then StaticPopup1Button1:Click() else AcceptResurrect() end");
						Logging.Write(System.Drawing.Color.Khaki, "[VibeParty] accepted resurrect.");
						return RunStatus.Success;
					})),
				// A rez may be coming (leader hasn't ordered release, still inside the wait window): HOLD — never
				// run to the corpse or the spirit healer, just wait and accept. Covers dead OR ghost.
				new Decorator(ctx => (StyxWoW.Me.Dead || StyxWoW.Me.IsGhost) && !RezGiveUp(),
					new PrioritySelector(
						new TreeSharp.Action(ctx => { RezHoldLogOnce(); return RunStatus.Failure; }),
						new ActionIdle()
					)),
				// Give up (leader said no rezzer, or the 120s watchdog expired): whisper the reason once, then let
				// LevelBot's corpse-run own the release + ghost run.
				new Decorator(ctx => StyxWoW.Me.Dead || StyxWoW.Me.IsGhost,
					new PrioritySelector(
						new TreeSharp.Action(ctx => { RezGiveUpWhisperOnce(); return RunStatus.Failure; }),
						LevelBot.CreateDeathBehavior()
					))
			);
		}

		private static volatile bool _resurrectPending;
		private static DateTime _deadSince = DateTime.MinValue;
		private static bool _rezGiveUpWhispered;
		private static bool _rezHoldLogged;
		private const double RezWaitWatchdogSec = 120;

		private void OnResurrectRequest(object sender, LuaEventArgs e) { _resurrectPending = true; }

		// A dead follower gives up waiting only when the leader says no rezzer is available, or the watchdog
		// expires. Held state (deadSince / the log+whisper latches) is reset on revive in Pulse.
		private static bool RezGiveUp()
			=> _rezReleaseOrdered
			   || (_deadSince != DateTime.MinValue && (DateTime.UtcNow - _deadSince).TotalSeconds > RezWaitWatchdogSec);

		private static void RezHoldLogOnce()
		{
			if (_rezHoldLogged) return;
			_rezHoldLogged = true;
			Logging.Write(System.Drawing.Color.Khaki, "[VibeParty] holding for rez.");
		}

		private static void RezGiveUpWhisperOnce()
		{
			if (_rezGiveUpWhispered) return;
			_rezGiveUpWhispered = true;
			string reason = _rezReleaseOrdered ? "no rezzer" : "waited " + (int)RezWaitWatchdogSec + "s";
			WhisperLeader("Corpse-running (" + reason + ")");
			Logging.Write(System.Drawing.Color.Khaki, "[VibeParty] corpse-running ({0}).", reason);
		}

		// ──────────────────────────────────────────────────────────────────────
		// Loot behavior — method_3
		// ──────────────────────────────────────────────────────────────────────

		private Composite CreateLootBehavior()
		{
			return new PrioritySelector(
				new Decorator(ctx => CanLoot(), LevelBot.CreateLootBehavior())
			);
		}

		// method_4
		private bool CanLoot()
		{
			return !Battlegrounds.IsInsideBattleground
				&& (!StyxWoW.Me.IsInInstance || VibePartySettings.Instance.LootInDungeons);
		}

		// Party-safety gate on collectible ground objects (quest items like "Handful of Oats", chests). The
		// inherited LevelBot loot pipeline surfaces them and charges straight in via ActionMoveToPoi with no
		// danger check — a follower walks into a live hostile's aggro bubble to reach one (2026-07-07 live).
		// Runs AFTER LevelbotIncludeLootsFilter (subscription order) so it prunes what that added. Reactive:
		// once the party clears the mobs the bubble lifts and the object passes on a later pulse — no blacklist.
		private const float CollectAggroMargin = 5f;   // mirrors VibeGrinder's ExposurePad

		private static void PruneDangerousCollectibles(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
		{
			outgoing.RemoveWhere(o => o is WoWGameObject go && HostileBubbleCovers(go.Location));
		}

		// Quest ground objects come in TWO GO types: chest-loot (LevelBot admits those via LootChests) and
		// "use" objects (Goober — mugs, kegs, levers, boards). LevelBot's filter only admits herb/mineral/
		// chest, so Goober-type quest objectives were invisible to followers (live report 2026-07-19). The
		// server already says per player which GO is our quest business — the sparkle dynamic flags CanLoot
		// reads ("on the quest, still needs it") — so include any sparkling non-profession GO in radius and
		// let the safety (PruneDangerousCollectibles) and lease (PartyLootFilter) filters behind us arbitrate.
		private static void IncludeQuestSparkles(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
		{
			foreach (WoWObject obj in incoming)
			{
				if (obj is not WoWGameObject go) continue;
				// Professions stay behind the Harvest settings; a bobber is a transient player-owned object.
				if (go.IsHerb || go.IsMineral || go.SubType == WoWGameObjectType.FishingBobber) continue;
				if (go.Distance > LootTargeting.LootRadius) continue;
				if (Blacklist.Contains(go.Guid)) continue;
				if (!go.CanLoot) continue;
				outgoing.Add(go);
			}
		}

		// Phase 5: lease-gate collectible ground objects — pursue only what WE hold a lease for; claim the nearest
		// free one. Runs AFTER PruneDangerousCollectibles so we never claim an object inside a hostile bubble.
		private void PartyLootFilter(List<WoWObject> incoming, HashSet<WoWObject> outgoing)
		{
			_partyLoot?.FilterCollectibles(outgoing);
		}

		// ──────────────────────────────────────────────────────────────────────
		// Water service (mage follower) — conjure + hand out water on request (downtime only)
		// ──────────────────────────────────────────────────────────────────────

		private static readonly string[] ConjureWaterSpells = { "Conjure Water" };   // WATER (mana), not food

		private Composite CreateWaterServiceBehavior()
		{
			// Mage only, out of combat, and someone actually asked. Serve waiting requesters with whatever we can
			// spare FIRST, top the stock up in the quiet ticks. Requesters hold still (AwaitingWater) so the
			// handoff is a stationary trade. Sits low in the tree → downtime only, never preempts combat/loot.
			return new Decorator(
				ctx => _partyWater != null && StyxWoW.Me.Class == WoWClass.Mage
					&& !StyxWoW.Me.Combat && _partyWater.HasRequests,
				new PrioritySelector(
					// 1. Deliver to the nearest requester once a full stack is spare (the trade splits bag
					//    stacks to assemble it — a fresh rank's 2-per-cast trickle never lands in one slot).
					new Decorator(ctx => WantDeliverWater(),
						new PrioritySelector(
							new Decorator(ctx => _waterTarget!.Distance > WaterTradeRange,
								new Sequence(
									new TreeSharp.Action(ctx => { _partyWater!.SendOffer(_waterTarget!.Guid); TreeRoot.StatusText = "Bringing water to " + _waterTarget!.Name; return RunStatus.Success; }),
									new NavigationAction(ctx => _waterTarget!.Location))),
							new Decorator(ctx => !_waterTarget!.IsMoving,
								new TreeSharp.Action(ctx => { TryDeliverWater(_waterTarget!); return RunStatus.Success; }))
						)),
					// 2. Stock up. A fresh conjure rank yields only 2/cast (+2 per character level), so filling
					//    the stock legitimately takes MANY casts — pace it, don't force it: a 3s conjure can never
					//    start while moving (retrying it every tick was a 12Hz busy-loop, Marge 2026-07-12_1237)
					//    and must not cancel our own drink; the throttle bounds any other silently-failing cast.
					new Decorator(
						ctx => PartyWater.ConjuredWaterCount() < PartyWater.MageWaterStock
							&& !StyxWoW.Me.IsCasting && !StyxWoW.Me.IsMoving && !StyxWoW.Me.HasAura("Drink")
							&& DateTime.UtcNow >= _nextConjureTry && FirstConjureSpell() != null,
						new TreeSharp.Action(ctx => { _nextConjureTry = DateTime.UtcNow.AddSeconds(2); SpellManager.Cast(FirstConjureSpell()); return RunStatus.Success; }))
				));
		}

		// Resolve the delivery target once per tick (needs a FULL STACK to give + a servable requester).
		// One stack per delivery, refill-on-empty (user 2026-07-12): partial top-ups left everyone holding
		// random amounts (11, 12, 6…) — consistent 20s beat frequent dribbles even if the requester waits
		// while a fresh-rank mage slowly stocks back up.
		private bool WantDeliverWater()
		{
			_waterTarget = null;
			if (_partyWater == null || PartyWater.ConjuredWaterCount() < PartyWater.ReserveForSelf + PartyWater.WaterStackGive) return false;
			_waterTarget = _partyWater.NextRequester();
			return _waterTarget != null;
		}

		private static string FirstConjureSpell()
		{
			foreach (string s in ConjureWaterSpells)
				if (SpellManager.HasSpell(s)) return s;
			return null;
		}

		private static bool TradeOpen()
			=> Lua.GetReturnVal<int>("return (TradeFrame and TradeFrame:IsShown()) and 1 or 0", 0) == 1;

		// UnitName('npc') is the trade partner while a trade window is open (3.3.5a TradeFrame) — the
		// same truth the recipient auto-accept gates on.
		private static string TradePartnerName()
			=> TradeOpen() ? (Lua.GetReturnVal<string>("return UnitName('npc') or ''", 0) ?? "") : "";

		// Synchronous VERIFIED trade: open with the requester, confirm the partner, place water, accept,
		// confirm the items left our bags. The requester auto-accepts (OnTradeShouldAccept, party-member
		// gate) and re-accepts when our item lands. Per-requester cooldown after every attempt.
		// ⚠ Never assume a step landed (fleet start 2026-07-20): an unverified handoff opened with the
		// LEADER instead of the requester — the leader auto-accepts any all-water trade — and "handed 20
		// water" was logged for trades that never completed; the leader collected ~2 stacks while the
		// named requester stayed dry. Every step below reads world truth before proceeding.
		private void TryDeliverWater(WoWPlayer requester)
		{
			if (StyxWoW.Me.IsMoving) WoWMovement.MoveStop();
			if (requester.Distance > WaterTradeRange) return;
			_partyWater!.SendOffer(requester.Guid);   // tell them to hold still

			// The delivery owns its trade window start-to-finish. A pre-existing window (leftover timed-out
			// attempt, or a human trading the mage) has an unknown partner and unknown slot state — close it
			// LOUD and start fresh; reusing it is how water landed on the wrong member.
			if (TradeOpen())
			{
				string holder = TradePartnerName();
				Logging.Write(System.Drawing.Color.Aqua, "[VibeParty] closing a leftover trade with {0} before serving {1}.",
					holder.Length != 0 ? holder : "?", requester.Name);
				Lua.DoString("CloseTrade()");
				if (!WaitFrame(() => !TradeOpen(), 1500)) return;   // still open — retry next tick
			}

			// Target() silently no-ops when the unit can't be selected — wait on the selection edge, or
			// InitiateTrade('target') fires at whatever we WERE targeting.
			requester.Target();
			if (!WaitFrame(() => StyxWoW.Me.CurrentTargetGuid == requester.Guid, 1000))
			{
				_partyWater.Served(requester.Guid);
				Logging.Write(System.Drawing.Color.Aqua, "[VibeParty] could not target {0} for the trade — will retry after cooldown.", requester.Name);
				return;
			}
			Lua.DoString("InitiateTrade('target')");
			if (!WaitFrame(TradeOpen, 2500))
			{
				_partyWater.Served(requester.Guid);   // cooldown, then retry — never a silent drop
				Logging.Write(System.Drawing.Color.Aqua, "[VibeParty] trade with {0} never opened — will retry after cooldown.", requester.Name);
				return;
			}
			// The window belongs to whoever the client OPENED it with, not to who we asked for — verify.
			string partner = TradePartnerName();
			if (partner != requester.Name)
			{
				Lua.DoString("CloseTrade()");
				_partyWater.Served(requester.Guid);
				Logging.Write(System.Drawing.Color.Red, "[VibeParty] trade opened with {0} instead of {1} — closed it, will retry after cooldown.",
					partner.Length != 0 ? partner : "?", requester.Name);
				return;
			}

			// Give one full STACK (20) by COUNT, splitting bag stacks as needed to assemble it. All conjured
			// drinks in bag count (stale ranks get purged lazily).
			int before = PartyWater.ConjuredWaterCount();
			int given = Lua.GetReturnVal<int>(
				"local reserve=" + PartyWater.ReserveForSelf + " local giveMax=" + PartyWater.WaterStackGive + " local total=0 " +
				"for b=0,4 do for s=1,GetContainerNumSlots(b) do local l=GetContainerItemLink(b,s) if l and string.find(l,'Conjured') and string.find(l,'Water') then local _,c=GetContainerItemInfo(b,s) total=total+(c or 1) end end end " +
				"local give=math.min(giveMax,total-reserve) local given=0 local slot=1 " +
				"if give>0 then for b=0,4 do for s=1,GetContainerNumSlots(b) do if given<give and slot<=6 then local l=GetContainerItemLink(b,s) if l and string.find(l,'Conjured') and string.find(l,'Water') then local _,c=GetContainerItemInfo(b,s) c=c or 1 local take=math.min(c,give-given) if take==c then PickupContainerItem(b,s) else SplitContainerItem(b,s,take) end ClickTradeButton(slot) slot=slot+1 given=given+take end end end end end " +
				"return given", 0);
			if (given <= 0)
			{
				Lua.DoString("CloseTrade()");
				_partyWater.Served(requester.Guid);
				Logging.Write(System.Drawing.Color.Aqua, "[VibeParty] nothing to spare for {0} (holding reserve) — will retry after cooldown.", requester.Name);
				return;
			}
			StyxWoW.Sleep(300);
			Lua.DoString("AcceptTrade()");
			// Success is world truth, not intent: the window closed AND the water left our bags. `given`
			// counts placement ATTEMPTS — on a locked slot or an unaccepted trade it is fiction.
			bool closed = WaitFrame(() => !TradeOpen(), 3000);
			bool transferred = closed && WaitFrame(() => PartyWater.ConjuredWaterCount() < before, 1500);
			_partyWater.Served(requester.Guid);
			if (transferred)
				Logging.Write(System.Drawing.Color.Aqua, "[VibeParty] handed {0} water to {1}.", before - PartyWater.ConjuredWaterCount(), requester.Name);
			else
			{
				Lua.DoString("CloseTrade()");
				Logging.Write(System.Drawing.Color.Red, "[VibeParty] trade with {0} did not complete ({1}) — will retry after cooldown.",
					requester.Name, closed ? "no items left our bags" : "window never closed");
			}
		}

		// Live, DB-free mirror of VibeGrinder's aggro-bubble danger model (WoWUnit.GetAggroRange): true if any
		// live hostile's aggro bubble (+ margin) covers dest. Neutrals/friendlies return 0 range (they don't
		// body-pull), so only real hostiles gate. Mobs already on us are the combat layer's job, not avoidance.
		private static bool HostileBubbleCovers(WoWPoint dest)
		{
			WoWUnit me = StyxWoW.Me;
			foreach (WoWUnit h in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
			{
				if (h == null || !h.IsAlive || h.IsFriendly || h.IsPlayer) continue;
				if (h.IsTargetingMeOrPet) continue;                  // in our fight already — combat owns it
				float bubble = h.GetAggroRange(me) + CollectAggroMargin;
				if (bubble <= CollectAggroMargin) continue;          // GetAggroRange==0 → neutral/friendly, no threat
				if (Math.Abs(h.Location.Z - dest.Z) >= 5f) continue; // different floor/cliff (server Z-sep gate)
				if (h.Location.DistanceSqr(dest) <= bubble * bubble) return true;
			}
			return false;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Event behavior — method_5 (party invite, dungeon proposal, role check, quest)
		// ──────────────────────────────────────────────────────────────────────

		private Composite CreateEventBehavior()
		{
			return new PrioritySelector(
				// Group invite
				new Decorator(ctx => _pendingGroupInvite,
					new Sequence(
						new TreeSharp.Action(ctx => Logging.Write("VibeParty: Accepting group invite")),
						new TreeSharp.Action(ctx => Lua.DoString("AcceptGroup()")),
						new TreeSharp.Action(ctx => _pendingGroupInvite = false),
						new WaitLuaEvent("PARTY_MEMBERS_CHANGED", 3,
							new TreeSharp.Action(ctx => Lua.DoString("StaticPopup_Hide(\"PARTY_INVITE\")")))
					)
				),
				// Dungeon proposal
				new Decorator(ctx => _pendingDungeonProposal,
					new Sequence(
						new TreeSharp.Action(ctx => Logging.Write("VibeParty: Accepting dungeon invite")),
						new TreeSharp.Action(ctx => Lua.DoString("AcceptProposal()")),
						new WaitContinue(30, ctx => StyxWoW.Me.IsInInstance,
							new TreeSharp.Action(ctx => _pendingDungeonProposal = false))
					)
				),
				// LFG offer continue (dungeon offer)
				new Decorator(ctx => _pendingDungeonOfferContinue,
					new Sequence(
						new TreeSharp.Action(ctx => Lua.DoString("StaticPopup1Button1:Click()")),
						new WaitContinue(1, ctx => false, new ActionIdle()),
						new TreeSharp.Action(ctx => _pendingDungeonOfferContinue = false)
					)
				),
				// Role check — staggered (see OnLfgRoleCheckShow), answered by the three-tier resolver.
				new Decorator(ctx => _pendingRoleCheck && DateTime.Now >= _roleCheckActAt,
					new Sequence(
						new TreeSharp.Action(ctx => AnswerRoleCheck()),
						new TreeSharp.Action(ctx => _pendingRoleCheck = false)
					)
				),
				// Quest accept — but never one the leader abandoned (a stale share/frame could re-arm it).
				new Decorator(ctx => _pendingQuestAccept,
					new Sequence(
						new TreeSharp.Action(ctx =>
						{
							if (ResolveOfferFrame()) { _pendingQuestAccept = false; return RunStatus.Success; }
							// ⛔ Staying armed must NOT eat the tick. Success here makes this branch succeed, so the
							// root PrioritySelector never reaches follow and the follower stands still — and a frame
							// closes by WALKING AWAY, so the freeze would sustain itself by suppressing the very
							// movement that ends it. Failure lets follow run; the flag re-reads next tick and the
							// visible-frame guard clears it once the frame is gone.
							return RunStatus.Failure;
						})
					)
				)
			);
		}

		// ──────────────────────────────────────────────────────────────────────
		// Quest offers — ONE policy, ONE executor
		// ──────────────────────────────────────────────────────────────────────

		/// <summary>
		/// THE accept policy. Every path that can take a quest asks this and nothing else: the
		/// unsolicited-offer resolver below, the core's chained-offer screen after a turn-in, and the
		/// quest-starter item scan. It used to be re-implemented at each of those, which is how one
		/// decision drifted into three shapes — one of them accepting an unreadable id outright.
		/// </summary>
		private static bool ShouldAcceptOffer(uint questId)
			=> questId != 0 && IsLeaderQuest(questId) && !QuestBlocked(questId);

		/// <summary>
		/// THE only place that answers an open offer frame — the sole caller of AcceptQuest/DeclineQuest.
		/// The quest frame has exactly one owner: while <see cref="QuestInteractionCore.IsDriving"/> the
		/// visit transaction owns it and this never runs (the event handler declines to arm), so the two
		/// can no longer fight over the same frame.
		/// </summary>
		/// <returns>True when the offer has been ANSWERED (or there is nothing to answer) and the pending
		/// flag may be cleared; false to stay armed and re-read on the next tick.</returns>
		private static bool ResolveOfferFrame()
		{
			// ASK, don't assume: answer the frame that is actually OPEN, never a remembered one. Without
			// this the resolver fires Lua into a closed frame and logs an accept that never happened.
			if (!QuestFrame.Instance.IsVisible) return true;   // nothing open — nothing to answer

			uint shown = QuestInteractionCore.ShownQuestId();
			if (shown == 0)
			{
				// ⛔ Never accept what we cannot identify — that is a hole straight through "the party does
				// the LEADER's quests, nothing else". But do NOT drop it either: nothing re-raises
				// QUEST_DETAIL for a frame that is already open, so clearing here would strand a real share
				// until some later CloseQuestFrames silently declined it. Stay armed and re-read — the
				// panel's own lifetime bounds this, since the guard above clears us once it closes.
				Logging.WriteDebug("VibeParty: quest offer with no readable id yet — re-reading next tick.");
				return false;
			}
			if (ShouldAcceptOffer(shown))
			{
				// NOT necessarily a share: this fires for any offer we did not open ourselves — a real
				// /share, a quest-starter item, or a server-pushed chain follow-up. Calling them all
				// "shared" made an unshared chain quest unexplainable in the log.
				Logging.Write("VibeParty: accepting offered quest {0}.", shown);
				Lua.DoString("AcceptQuest()");
				return true;
			}
			Logging.Write("VibeParty: declining quest {0} — {1}.", shown,
				QuestBlocked(shown) ? "the leader abandoned it" : "not one of the leader's quests");
			Lua.DoString("DeclineQuest()");
			return true;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Turn-in behavior (Phase 2) — hand in completed quests at the leader's NPC
		// ──────────────────────────────────────────────────────────────────────

		// Turn-in is leader-DRIVEN (a follower can't find enders alone: no quest DB, no per-unit "?" status). The
		// SIGNAL is "the leader is targeting a live quest-giver" (LeaderPlugin broadcasts LeaderTargetGuid every
		// pulse; while the leader has a giver's frame open, that IS its target). We act only while that signal is
		// live AND we hold a completed quest, then MOVE to a giver (the old code silently required WithinInteractRange
		// and never moved) and hand in everything its frame offers that our log marks complete.
		private static Composite CreateTurnInBehavior()
		{
			return new Decorator(ctx => WantTurnIn(),
				new PrioritySelector(
					new Decorator(ctx => !_turnInNpc!.WithinInteractRange || NeedTightApproach(_turnInNpc!),
						new Sequence(
							new TreeSharp.Action(ctx => TreeRoot.StatusText = "Moving to turn in at " + _turnInNpc!.Name),
							new NavigationAction(ctx => _turnInNpc!.Location))),
					new TreeSharp.Action(ctx => { VisitQuestEntity(_turnInNpc!); return RunStatus.Success; })
				));
		}

		// A click from the InteractRange boundary can silently no-op (QuestInteractionCore
		// .SafeInteractRange) — after a no-frame visit, the retry closes to the safe stand-off first.
		// Deadline-bounded: an NPC nav can't get that close to (behind a counter, on a platform) falls
		// back to interacting from the best position reached instead of orbiting it forever.
		private static bool NeedTightApproach(WoWUnit npc)
		{
			return _turnInCloseUntil.TryGetValue(npc.Guid, out DateTime until)
				&& DateTime.UtcNow < until
				&& npc.Distance > QuestInteractionCore.SafeInteractRange;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Game-object quest interaction (corpses/altars/barrels) — the ender/giver is a WoWGameObject
		// ──────────────────────────────────────────────────────────────────────

		// A GO carries NO QuestGiverStatus "?" a follower can read, so the signal is the ledger: "a nearby
		// GO whose DB row is an ender for a held completable quest (log-complete OR a discover quest the log
		// never flags — The Lost Pilot) or a giver for a wanted one (A Pilot's Revenge)". Bounded to
		// TurnInVicinity so a follower never treks off and abandons the leader; reachability is probed
		// BEFORE committing (CanReach), so an unreachable GO is never attempted and needs no failure memory.
		// The visit itself is the SAME frame-reading transaction as the creature path (VisitQuestEntity) —
		// it asks the object what it presents instead of trusting the log flag, which is what breaks the
		// discover-quest deadlock (isComplete stays nil; interacting with the corpse is what completes it).
		private static Composite CreateGoVisitBehavior()
		{
			return new Decorator(ctx => WantGoVisit(),
				new PrioritySelector(
					new Decorator(ctx => !_goVisitObj!.WithinInteractRange,
						new Sequence(
							new TreeSharp.Action(ctx => TreeRoot.StatusText = "Quest object: " + _goVisitObj!.Name),
							new TreeSharp.Action(ctx => MoveToGameObject()))),
					new TreeSharp.Action(ctx => { VisitQuestEntity(_goVisitObj!); return RunStatus.Success; })
				));
		}

		// Resolve a live GO with quest business near us this tick (sets _goVisitObj for the children). The
		// ledger's GoBusiness already screened held-completable turn-ins + wanted pickups (and excluded
		// repeatables), so this only has to find the nearest present, reachable object. Relies on WantTurnIn
		// having refreshed the ledger earlier this tick (it runs above us in the tree, out of combat).
		private static bool WantGoVisit()
		{
			_goVisitObj = null;
			LocalPlayer me = StyxWoW.Me;
			if (me.Combat || !FollowerQuestLedger.Loaded) return false;

			List<string>? blocked = null;
			var seen = new HashSet<int>();
			foreach (GoWork w in FollowerQuestLedger.GoBusiness)
			{
				if (!seen.Add(w.GoEntry)) continue;   // one live-resolve per distinct object entry
				if (!FollowerQuestLedger.TryNearestGoSpawn(w.GoEntry, (int)me.MapId, me.Location, out WoWPoint spawn))
				{
					Block(ref blocked, w.Title, "its game object " + w.GoEntry + " has no spawn row in QuestData.db");
					continue;
				}
				if (me.Location.Distance(spawn) > TurnInVicinity) continue;   // out of range is normal, not a fault

				// Resolve against OUR position, not the DB coordinate. The spawn row is only a hint that this
				// object is plausibly nearby (the gate above); the LIVE object is the truth, and matching it
				// tight to the DB coord fails the moment the two drift — live: the Thunder Ale Barrel (GO 270,
				// a single spawn at the Barleybrew farm) never resolved while we stood next to it. Scanning
				// only WoWGameObject means the creature/GO id overlap can't bite here.
				WoWGameObject? go = ObjectManager.GetObjectsOfType<WoWGameObject>(false, false)
					.Where(g => g != null && g.Entry == (uint)w.GoEntry && g.Distance <= TurnInVicinity)
					.OrderBy(g => g.Distance)
					.FirstOrDefault();
				if (go == null)
				{
					// ⚠ NOT a fault, and never a [N] line: many quest objects are CONDITIONAL SPAWNS that only
					// exist once a world action has happened (the Thunder Ale Barrel, GO 270, appears only
					// after its NPC is bribed with Thunder Ale — user, live 2026-07-19). "Not there" is the
					// normal resting state for those, indistinguishable from "not in view yet", and the bot
					// cannot trigger the precondition. Note it on the debug channel and move on.
					Block(ref blocked, w.Title, "object " + w.GoEntry + " not present yet (conditional spawn / not in view)");
					continue;
				}
				if (_turnInCooldown.TryGetValue(go.Guid, out DateTime until) && DateTime.UtcNow < until) continue;
				// LOOKAHEAD (doctrine rule 3): consult the canceller BEFORE committing. An object the mesh
				// can't route to never becomes a commitment, so there is no failed trip to remember.
				if (!go.WithinInteractRange && !CanReach(go.Location))
				{
					// Permanent for this spawn (off-mesh: on a cart/platform/behind a rail) — the one bail-out
					// that will never resolve on its own, so it must never be silent.
					Block(ref blocked, w.Title, "the mesh has no complete route to " + go.Name);
					continue;
				}

				_goVisitObj = go;
				return true;
			}
			GoBlocked(blocked);
			return false;
		}

		private static void Block(ref List<string>? into, string title, string reason)
			=> (into ??= new List<string>()).Add(title + ": " + reason);

		// The leader taking a NEW quest is an EDGE — the chain just unlocked for us too. Polling for it at the
		// 15s visit throttle means standing beside the giver doing nothing while the whitelist catches up
		// (live: a chained q320 'Return to Bellowfiz' was declined at the turn-in because the leader hadn't
		// accepted it yet, then took 31s / two throttle cycles to pick up). Clearing the throttle here makes
		// the follower re-ask every giver on the very next tick. Bot thread — the hub handler only sets the
		// flag (PartyBus subscribers are pure-data, they must never touch these collections).
		private static volatile bool _leaderQuestsDirty;
		private static void PulseLeaderQuestEdge()
		{
			if (!_leaderQuestsDirty) return;
			_leaderQuestsDirty = false;
			_turnInCooldown.Clear();
			_divergedAt.Clear();   // the divergence may be resolved now; let it re-report honestly if not
		}

		// Ledger predicted a pickup the NPC never offered. Edge-logged (one string, on change) — the point
		// is diagnosis, not a veto: if this line is loud and repeating, the SCREEN is wrong (an unenforced
		// server gate), which is a bug to fix in the ledger, not a failure to remember per NPC.
		// ⚠ Keyed PER NPC ENTRY, not one shared slot: with a single slot two NPCs that each have a standing
		// divergence alternate it and every visit counts as "changed" — the same multi-subject narrator bug
		// GoBlocked below documents. Bounded by the handful of quest NPCs we actually visit.
		private static readonly Dictionary<uint, string> _divergedAt = new Dictionary<uint, string>();
		private static void VisitDiverged(WoWObject entity, string wanted, string presented)
		{
			string line = wanted + "|" + presented;
			if (_divergedAt.TryGetValue(entity.Entry, out string? seen) && seen == line) return;
			_divergedAt[entity.Entry] = line;
			Logging.Write(System.Drawing.Color.Orange,
				"VibeParty: ledger wanted {0} at {1} but it offers [{2}] — server gate the screen can't see.",
				wanted, entity.Name, presented.Length == 0 ? "nothing" : presented);
		}

		// Edge-logged narrator: ONE snapshot of everything currently blocking a game-object turn-in, logged
		// only when that WHOLE picture changes. It holds what's true now, not a history of failures.
		// ⚠ A single-SUBJECT narrator (the MoveMode pattern) is wrong here and was a real bug: this scan
		// visits several quests per tick, so comparing one reason at a time just alternates between them and
		// every call counts as "changed" — 'Bitter Rivals' and 'Return to Bellowfiz' ping-ponged ~10 lines a
		// second live. Compare the composite, and sort it so dictionary order can't fake a change.
		private static string _goBlockReason = "";
		private static void GoBlocked(List<string>? reasons)
		{
			string now = "";
			if (reasons != null && reasons.Count > 0)
			{
				reasons.Sort(StringComparer.Ordinal);
				now = string.Join(" ; ", reasons);
			}
			if (now == _goBlockReason) return;
			_goBlockReason = now;
			if (now.Length == 0) return;   // cleared — the block resolved itself, nothing to announce
			// DEBUG, not a warning: every reason here is "can't do it right now" (conditional spawn, not in
			// view, no route, missing DB row) — none is actionable by the bot and none is a malfunction, so
			// none of it belongs on the [N] channel the user actually reads.
			TiDebug("game-object turn-in blocked — {0}.", now);
		}

		// ⚠ `ReachedDestination` while the object is STILL out of interact range means the mesh got us as
		// close as it can (barrel on a cart/platform/behind a rail). Returning Success there would re-issue
		// "arrived" every tick and the follower would stand at the object forever without ever attempting
		// it. Fail instead, so the tree falls through to the interaction attempt — which answers honestly
		// (Retry + a loud line + the shared throttle) rather than wedging silently.
		private static RunStatus MoveToGameObject()
		{
			MoveResult r = Navigator.MoveTo(_goVisitObj!.Location);
			return r == MoveResult.Moved || r == MoveResult.PathGenerated || r == MoveResult.UnstuckAttempt
				? RunStatus.Success
				: RunStatus.Failure;
		}

		// Provider-free reachability probe. ⚠ GeneratePath is NOT usable here: it returns a path whenever
		// Detour merely SUCCEEDS, without checking IsPartialPath — and an off-mesh object (barrel on a
		// cart/platform/cellar) yields exactly that, a partial path ending at the nearest reachable poly.
		// That reads as "reachable", we walk to a spot we can never interact from, and the trip repeats.
		// CanNavigateWithinDistance rejects partials outright and bounds the walk, so it is the real
		// canceller. Bound = the vicinity we already commit to, so it can't authorise a cross-zone trek.
		private static bool CanReach(WoWPoint dest)
			=> Navigator.CanNavigateWithinDistance(StyxWoW.Me.Location, dest, TurnInVicinity * 2f);

		// Gate + NPC resolution in ONE pass: sets _turnInNpc for the children this tick (avoids re-scanning the
		// object manager per child lambda). Cheap checks first so most ticks bail before the OM scan.
		private static bool WantTurnIn()
		{
			if (StyxWoW.Me.Combat) { _turnInNpc = null; return false; }

			// Ledger inputs, once per tick. Completion truth is the LUA quest log, never
			// PlayerQuest.IsCompleted (false-positives on in-progress quests, see CompletedQuestsLua).
			var completed = CompletedQuestsLua();
			var logIds = new HashSet<uint>();
			foreach (var pq in StyxWoW.Me.QuestLog.GetAllQuests())
				if (pq != null) logIds.Add(pq.Id);

			// A quest LEAVING our log is the authoritative "we just finished (or dropped) something" edge, and
			// the server-queried completed set is a derived view of it cached for a MINUTE. Invalidating on the
			// edge makes that cache SELF-HEALING: no code path can leave it stale by forgetting to credit
			// itself. Live proof of the alternative — a turn-in that landed but was misreported skipped the
			// invalidate, and the chained follow-up quest stayed invisible for exactly 60.0s.
			if (_lastLogIds != null)
				foreach (uint id in _lastLogIds)
					if (!logIds.Contains(id)) { Styx.Logic.Questing.QuestLog.InvalidateCompletedQuestCache(); break; }
			_lastLogIds = logIds;

			var doneSet = new HashSet<uint>(StyxWoW.Me.QuestLog.GetCompletedQuests() ?? Enumerable.Empty<uint>());
			FollowerQuestLedger.Refresh(_leaderQuests.Keys.ToList(), QuestBlocked, logIds, completed, doneSet);

			_leaderSignal = LeaderAtQuestGiver();

			// COMMIT to the chosen NPC while it stays usable — re-picking "nearest" every tick
			// flip-flops between givers mid-travel (observed: Eagan→Merissa 1.7s apart).
			if (_turnInNpc != null && _turnInNpc.IsValid && !_turnInNpc.Dead
				&& IsUsableGiver(_turnInNpc) && _turnInNpc.Distance <= TurnInVicinity * 1.5)
				return true;

			// The leader's own giver first (it knows the hub) while the live signal is up…
			if (_leaderSignal && _botMessage != null)
			{
				WoWUnit? lt = ObjectManager.GetObjectByGuid<WoWUnit>(_botMessage.LeaderTargetGuid);
				if (IsUsableGiver(lt)) { _turnInNpc = lt; return true; }
			}
			// …else the nearest giver with business: TURN-IN statuses need no signal at all (the "?"
			// IS the signal, however late we arrive), AVAILABLE statuses need the live signal or a
			// recent leader visit record (see IsUsableGiver / OnLeaderPickup).
			_turnInNpc = ObjectManager.GetObjectsOfType<WoWUnit>(false, false)
				.Where(u => u != null && u.Distance <= TurnInVicinity && IsUsableGiver(u))
				.OrderBy(u => u.Distance)
				.FirstOrDefault();
			return _turnInNpc != null;
		}

		// The signal: the leader is at/targeting a live quest-giver. No range check (that's the leader's range) —
		// WE navigate to whatever NPC we pick.
		private static bool LeaderAtQuestGiver()
		{
			if (_botMessage == null || _botMessage.LeaderTargetGuid == 0) return false;
			WoWUnit? npc = ObjectManager.GetObjectByGuid<WoWUnit>(_botMessage.LeaderTargetGuid);
			return npc != null && !npc.Dead && npc.IsQuestGiver;
		}

		// The CLIENT already knows whether an NPC has business with us — the server pushes per-NPC
		// quest-giver status (what renders the "!"/"?" markers) and the port exposes it as a memory
		// read (WoWObject.QuestGiverStatus), no interaction needed. TURN-IN statuses make an NPC a
		// candidate on their own — a follower reads the "?" like a player, so a talk-to quest that
		// completes by walking up never depends on leader timing (Milly, run 3). AVAILABLE statuses
		// only count while the leader signal is live (leader-at-giver = the leader intends this hub) —
		// otherwise followers would hoover every "!" they pass. Incomplete/None/Unavailable: never.
		private static bool GiverHasTurnIn(WoWUnit u)
		{
			switch (u.QuestGiverStatus)
			{
				case QuestGiverStatus.TurnIn:
				case QuestGiverStatus.TurnInRepeatable:
				case QuestGiverStatus.TurnInInvisible:
				case QuestGiverStatus.LowLevelTurnInRepeatable:
					return true;
				default:
					return false;
			}
		}

		private static bool GiverHasAvailable(WoWUnit u)
		{
			switch (u.QuestGiverStatus)
			{
				case QuestGiverStatus.Available:
				case QuestGiverStatus.AvailableRepeatable:
				case QuestGiverStatus.LowLevelAvailable:
				case QuestGiverStatus.LowLevelAvailableRepeatable:
					return true;
				default:
					return false;
			}
		}

		// A recent leader visit record makes an AVAILABLE giver usable without the live signal — the
		// leader accepted quests here minutes ago and the trailing follower replays the visit when it
		// passes (the "I talked to this NPC, you do the same" log). TTL'd; entry-keyed (any spawn of
		// the same NPC counts).
		private static bool RecentLeaderPickup(uint entry)
			=> _leaderPickupLog.TryGetValue(entry, out DateTime at)
			   && (DateTime.UtcNow - at).TotalMinutes < 10;

		// An NPC is worth a visit only when it holds CONCRETE business for us:
		//  - it shows OUR "?" (server truth, no DB needed), or
		//  - the ledger maps its entry to a wanted pickup / pending turn-in, or
		//  - fallback (whitelisted quest missing from QuestData.db): it shows Available AND the
		//    leader is at THIS entry now or picked up here recently — the old replay signals,
		//    scoped to the leader's actual NPC instead of licensing every "!" in the vicinity.
		//  - degraded mode (no DB at all): the old signals gate wholesale, as before the ledger.
		private static bool IsUsableGiver(WoWUnit? u)
			=> u != null && !u.Dead && u.IsQuestGiver
			   && (GiverHasTurnIn(u)
			       || FollowerQuestLedger.EntryHasBusiness(u.Entry)
			       || (FollowerQuestLedger.HasDbMissingWants && GiverHasAvailable(u)
			           && (LeaderTargetEntry() == u.Entry || RecentLeaderPickup(u.Entry)))
			       || (!FollowerQuestLedger.Loaded && GiverHasAvailable(u)
			           && (_leaderSignal || RecentLeaderPickup(u.Entry))))
			   && !_turnInDeadNpc.Contains(u.Guid)
			   && !(_turnInCooldown.TryGetValue(u.Guid, out DateTime until) && DateTime.UtcNow < until);

		private static uint LeaderTargetEntry()
		{
			if (!_leaderSignal || _botMessage == null) return 0;
			WoWUnit? npc = ObjectManager.GetObjectByGuid<WoWUnit>(_botMessage.LeaderTargetGuid);
			return npc?.Entry ?? 0;
		}

		// ──────────────────────────────────────────────────────────────────────
		// Quest-starter items (Phase 1) — accept drop-only, item-started quests
		// ──────────────────────────────────────────────────────────────────────

		// Some quests begin by USING a drop-only item (ItemInfo.BeginQuestId), which /share can't push — so a
		// follower that looted the same item must use ITS OWN copy. Using it raises QUEST_DETAIL, which the existing
		// accept handler auto-confirms. Never while combat / casting / a frame is open. Per-quest
		// cooldown so a slow accept or a level-gated item can't spam.
		private static Composite CreateUseQuestStarterBehavior()
		{
			return new Decorator(
				ctx => !StyxWoW.Me.Combat && !StyxWoW.Me.IsCasting
					&& !GossipFrame.Instance.IsVisible && !QuestFrame.Instance.IsVisible
					&& FirstUsableQuestStarter() != null,
				new TreeSharp.Action(ctx =>
				{
					WoWItem? item = FirstUsableQuestStarter();
					if (item == null || !item.IsValid) return RunStatus.Failure;
					if (StyxWoW.Me.IsMoving) { WoWMovement.MoveStop(); return RunStatus.Success; }
					uint qid = (uint)item.ItemInfo.BeginQuestId;
					Logging.Write("VibeParty: Using quest-starter item {0} (starts quest {1}).", item.Name, qid);
					_starterCooldown[qid] = DateTime.UtcNow.AddSeconds(30);
					_starterCheckAt = DateTime.MinValue;   // force a rescan next tick (the item is consumed on accept)
					item.Use();
					return RunStatus.Success;
				}));
		}

		// A bag item that STARTS a quest we don't already have or haven't finished, not on cooldown. Memoized ~1s so
		// the per-tick gate doesn't rescan bags + completed-quests every pulse.
		private static WoWItem? FirstUsableQuestStarter()
		{
			if ((DateTime.UtcNow - _starterCheckAt).TotalMilliseconds < 1000) return _starterItemCache;
			_starterCheckAt = DateTime.UtcNow;
			_starterItemCache = ScanQuestStarter();
			return _starterItemCache;
		}

		private static WoWItem? ScanQuestStarter()
		{
			LocalPlayer me = StyxWoW.Me;
			var completed = me.QuestLog.GetCompletedQuests();
			foreach (WoWItem item in me.BagItems)
			{
				if (item == null || item.ItemInfo == null) continue;
				int qid = item.ItemInfo.BeginQuestId;
				if (qid <= 0) continue;
				// Same accept policy as every other offer path — the whitelist + abandon block live in ONE place.
				if (!ShouldAcceptOffer((uint)qid)) continue;
				if (me.QuestLog.ContainsQuest((uint)qid)) continue;
				if (completed != null && completed.Contains((uint)qid)) continue;
				if (_starterCooldown.TryGetValue((uint)qid, out DateTime until) && DateTime.UtcNow < until) continue;
				return item;
			}
			return null;
		}

		// One visit = the ledger's concrete (questId, action) worklist driven through the shared
		// id-keyed QuestInteractionCore — no menu-walking, no title guessing. Same-title quests are
		// just different ids; every outcome is typed and logged. Combat is re-checked between quests.
		// Own the frame for the WHOLE episode (open + read the presented lists + act). Without this the
		// frame WE opened raises QUEST_DETAIL while nobody owns it, the offer handler arms for a frame this
		// visit is about to handle, and a tick later it answers an already-closed frame. Scoped as a WRAPPER
		// (same shape as PickUp/TurnIn) so every early return releases ownership by construction.
		private static void VisitQuestEntity(WoWObject entity)
		{
			using (QuestInteractionCore.Drive()) VisitQuestEntityOwned(entity);
		}

		// ASK, don't guess: open the frame and act ONLY on what this entity actually PRESENTS.
		//
		// ⛔ THE DIVISION OF AUTHORITY (the design rule this whole path exists to honour):
		//   the DB/ledger says where to WALK; the FRAME says what to DO. Neither does the other's job.
		// Everything the SERVER already decides — is it completable, is it available, are the prereqs/level/
		// exclusive-group satisfied — is answered by the open frame and must NEVER be re-derived from a
		// cached client view. Re-deriving it is what produced every failure this path has had: gating
		// turn-ins on the client `isComplete` flag deadlocked "discover the corpse" quests forever (it is
		// nil for them BY DESIGN — interacting with the ender is what completes them), and gating pickups on
		// a `GetCompletedQuests`-derived prereq check stalled a chained follow-up for that cache's full 60s.
		// Only OUR OWN policy is applied here: the leader whitelist and the no-repeatables rule.
		// Shared by creature NPCs and game objects — the frame flow is identical for both.
		private static void VisitQuestEntityOwned(WoWObject entity)
		{
			LocalPlayer me = StyxWoW.Me;
			if (me.IsMoving) WoWMovement.MoveStop();

			if (!QuestInteractionCore.OpenInteraction(entity, 0, "VibeParty[quest]"))
			{
				NoProgressAtNpc(entity);
				return;
			}

			bool isGo = entity is WoWGameObject;
			List<PlayerQuest> held = me.QuestLog.GetAllQuests() ?? new List<PlayerQuest>();
			// What the DB says this entity offers — a pure title→id map (3.3.5a's greeting API carries no
			// ids), never a permission. Also the routing intent, kept ONLY to name the collider on a miss.
			List<QuestWork> offers = FollowerQuestLedger.OffersAt(entity.Entry, isGo);
			var work = new List<QuestWork>();

			// Turn-ins: an active the FRAME flags complete that we actually hold. Ids come from our LIVE quest
			// log (it carries both id and title, uncached) — no derived completed-set, so a quest whose
			// completion the log never flags is handled identically to one it does. Same-title quests are
			// different ids, so each presented entry consumes ONE candidate id.
			var heldByTitle = new Dictionary<string, List<uint>>(StringComparer.OrdinalIgnoreCase);
			foreach (PlayerQuest pq in held)
			{
				if (pq == null || FollowerQuestLedger.IsRepeatable((int)pq.Id)) continue;   // policy: never blue quests
				if (!heldByTitle.TryGetValue(pq.Name, out var ids)) heldByTitle[pq.Name] = ids = new List<uint>();
				ids.Add(pq.Id);
			}
			foreach (QuestInteractionCore.PresentedQuest p in QuestInteractionCore.PresentedActives())
			{
				if (!p.IsComplete) continue;
				if (!heldByTitle.TryGetValue(p.Title, out var ids) || ids.Count == 0) continue;
				work.Add(new QuestWork { QuestId = ids[0], Title = p.Title, TurnIn = true });
				ids.RemoveAt(0);
			}

			// Pickups: an available the FRAME offers, mapped back to an id through the DB, then screened by
			// OUR policy alone (leader whitelist + no repeatables). That the server offered it already proves
			// its own gates are satisfied.
			List<QuestInteractionCore.PresentedQuest> presentedAvail = QuestInteractionCore.PresentedAvailables();
			foreach (QuestInteractionCore.PresentedQuest p in presentedAvail)
				foreach (QuestWork o in offers)
					if (string.Equals(o.Title, p.Title, StringComparison.OrdinalIgnoreCase) && MayPickUp(o.QuestId))
					{ work.Add(o); break; }

			// Single-quest entities open the DETAIL/PROGRESS panel directly — no list at all, and that panel
			// carries the id, so it needs no title round-trip: holding the quest means this is a turn-in,
			// otherwise it's an offer.
			if (work.Count == 0 && QuestFrame.Instance.IsVisible)
			{
				uint shown = QuestInteractionCore.ShownQuestId();
				if (shown != 0 && !FollowerQuestLedger.IsRepeatable((int)shown))
				{
					if (me.QuestLog.ContainsQuest(shown))
						work.Add(new QuestWork { QuestId = shown, Title = HeldTitle(held, shown), TurnIn = true });
					else if (MayPickUp(shown))
						work.Add(new QuestWork { QuestId = shown, Title = OfferTitle(offers, shown), TurnIn = false });
				}
			}

			work.Sort((a, b) => b.TurnIn.CompareTo(a.TurnIn));   // turn-ins first: they unlock chained pickups

			if (work.Count == 0)
			{
				// Doctrine rule 5 — NAME THE COLLIDER. Routing sent us here for a pickup and the server
				// presented none: a gate the routing screen can't see (or title drift). Diagnostics ONLY —
				// routing never authorises the action above. Edge-logged, so a standing divergence prints once.
				string wantedPick = string.Join(", ", RoutedPickups(entity, isGo).Select(w => w.QuestId + " '" + w.Title + "'"));
				if (wantedPick.Length > 0)
					VisitDiverged(entity, wantedPick, string.Join(" | ", presentedAvail.Select(p => p.Title)));
				TiDebug("visit {0} [{1}] status={2}: presents nothing for us — cooldown.", entity.Name, entity.Entry, EntityStatus(entity));
				CloseQuestFrames();
				SetTurnInCooldown(entity.Guid);
				return;
			}

			TiDebug("visit {0} [{1}] status={2}: {3}", entity.Name, entity.Entry, EntityStatus(entity),
				string.Join(", ", work.Select(w => (w.TurnIn ? "turnin " : "pickup ") + w.QuestId + " '" + w.Title + "'")));

			int turnedIn = 0, pickedUp = 0, retries = 0;
			foreach (QuestWork w in work)
			{
				if (me.Combat) break;   // combat reactivity between per-quest transactions
				QuestInteractOutcome outcome = w.TurnIn
					? QuestInteractionCore.TurnIn(entity, (int)w.QuestId, w.Title, "VibeParty[quest]",
						ShouldAcceptOffer)
					: QuestInteractionCore.PickUp(entity, (int)w.QuestId, w.Title, "VibeParty[quest]");
				switch (outcome)
				{
					case QuestInteractOutcome.Success:
						if (w.TurnIn) turnedIn++; else pickedUp++;
						break;
					case QuestInteractOutcome.Retry:
						retries++;   // ONLY a transient/reach problem feeds the ladder below
						break;
					case QuestInteractOutcome.NotComplete:
						// The server's own verdict: objectives outstanding. A normal answer, not a failure —
						// we asked because the frame listed it (or the list was ambiguous), and the server
						// arbitrated. Must NOT charge the reach ladder, or a multi-quest giver holding one
						// unfinished quest would be blacklisted for the session.
						TiDebug("{0}: q{1} '{2}' not completable yet — leaving it.", entity.Name, w.QuestId, w.Title);
						break;
					case QuestInteractOutcome.NotOffered:
						// The entity LISTED this a moment ago and then wouldn't serve it — a genuine race (the
						// list shifted under us), not a standing condition. Nothing to remember: the next
						// visit re-reads the list, and if it's really gone it simply won't be picked again.
						Logging.Write("VibeParty: {0} listed '{1}' but would not serve it — re-reading its list next visit.", entity.Name, w.Title);
						break;
					case QuestInteractOutcome.NoGiverFlag:
						_turnInDeadNpc.Add(entity.Guid);
						Logging.Write("VibeParty: {0} carries no questgiver flag — giving up on it this session.", entity.Name);
						SetTurnInCooldown(entity.Guid);
						return;
				}
			}

			CloseQuestFrames();

			if (turnedIn > 0) Logging.Write("VibeParty: Turned in {0} quest(s) at {1}.", turnedIn, entity.Name);
			if (pickedUp > 0) Logging.Write("VibeParty: Picked up {0} quest(s) at {1}.", pickedUp, entity.Name);

			if (turnedIn == 0 && pickedUp == 0 && retries > 0)
			{
				NoProgressAtNpc(entity);
				return;
			}
			_turnInFrameFails.Remove(entity.Guid);
			_turnInCloseUntil.Remove(entity.Guid);
			SetTurnInCooldown(entity.Guid);
		}

		// OUR policy on accepting an offer the server is already willing to give: it must be one of the
		// leader's quests (the mirror invariant) and never a repeatable "blue" one (turning it in re-offers
		// it, so a follower would loop). Deliberately NOT a re-derivation of any server rule.
		private static bool MayPickUp(uint questId)
			=> ShouldAcceptOffer(questId) && !FollowerQuestLedger.IsRepeatable((int)questId);

		private static string HeldTitle(List<PlayerQuest> held, uint id)
		{
			foreach (PlayerQuest pq in held)
				if (pq != null && pq.Id == id) return pq.Name;
			return FollowerQuestLedger.QuestName((int)id);
		}

		private static string OfferTitle(List<QuestWork> offers, uint id)
		{
			foreach (QuestWork o in offers)
				if (o.QuestId == id) return o.Title;
			return FollowerQuestLedger.QuestName((int)id);
		}

		// What ROUTING intended to do here — diagnostics only (the divergence line). Never authorises work.
		private static List<QuestWork> RoutedPickups(WoWObject entity, bool isGo)
		{
			var list = new List<QuestWork>();
			if (isGo)
			{
				foreach (GoWork g in FollowerQuestLedger.GoBusiness)
					if (g.GoEntry == (int)entity.Entry && !g.TurnIn)
						list.Add(new QuestWork { QuestId = g.QuestId, Title = g.Title, TurnIn = false });
			}
			else
			{
				foreach (QuestWork w in FollowerQuestLedger.BusinessAt(entity.Entry))
					if (!w.TurnIn) list.Add(w);
			}
			return list;
		}

		// The "?" status for the trace line — a WoWUnit exposes the server's marker; a game object has none.
		private static string EntityStatus(WoWObject entity)
			=> entity is WoWUnit u ? u.QuestGiverStatus.ToString() : "object";

		private static void CloseQuestFrames()
		{
			if (GossipFrame.Instance.IsVisible) GossipFrame.Instance.Close();
			else if (QuestFrame.Instance.IsVisible) QuestFrame.Instance.Close();
		}

		// The ONE failure ladder we keep, and it's about REACHING the entity, not about any quest: the frame
		// never opened / nothing advanced. A ghost or unreachable NPC (or an off-mesh object) must not orbit
		// us forever.
		private static void NoProgressAtNpc(WoWObject entity)
		{
			int fails = _turnInFrameFails.TryGetValue(entity.Guid, out int f) ? f + 1 : 1;
			_turnInFrameFails[entity.Guid] = fails;
			Logging.Write(System.Drawing.Color.Orange,
				"VibeParty: quest visit at {0} made no progress (attempt {1}) — will retry.", entity.Name, fails);
			if (fails >= 6)
			{
				_turnInDeadNpc.Add(entity.Guid);
				Logging.Write("VibeParty: {0} has made no progress in {1} visits — giving up on it this session.", entity.Name, fails);
				PublishAlert(false, "gave up on quest giver " + entity.Name + " after " + fails + " stalled visits");
			}
			int cooldownSec = fails >= 2 ? 120 : 15;
			_turnInCooldown[entity.Guid] = DateTime.UtcNow.AddSeconds(cooldownSec);
			// The retry must approach tighter (NeedTightApproach); +12s of travel budget past the
			// cooldown covers the ≤1yd close-in plus a stuck-jiggle before the fallback kicks in.
			_turnInCloseUntil[entity.Guid] = DateTime.UtcNow.AddSeconds(cooldownSec + 12);
		}

		// Turn-in trace — the debug channel that answers "what the fuck did the quester decide".
		// One visit ≈ a dozen lines; grep "VibeParty[turnin]".
		private static void TiDebug(string fmt, params object[] args)
			=> Logging.WriteDebug("VibeParty[turnin]: " + string.Format(fmt, args));

		// ⚠ The client QUEST LOG is the only completion truth — PlayerQuest.IsCompleted (descriptor
		// flag + native call) returned TRUE for an IN-PROGRESS kill quest (quest 15, second live run)
		// and sent the turn-in at an incomplete quest, wedging 25s of Continue-spam against a progress
		// panel the server rightly refused to advance. GetQuestLogTitle's isComplete (1 = complete) is
		// what the player's own quest log shows. One Lua sweep, id→title, cached ~1s.
		private static DateTime _completedScanAt = DateTime.MinValue;
		private static readonly Dictionary<uint, string> _completedCache = new Dictionary<uint, string>();

		private static Dictionary<uint, string> CompletedQuestsLua()
		{
			if ((DateTime.Now - _completedScanAt).TotalMilliseconds < 1000) return _completedCache;
			_completedScanAt = DateTime.Now;
			_completedCache.Clear();
			string res = Lua.GetReturnVal<string>(
				"local r = '' " +
				"for i = 1, GetNumQuestLogEntries() do " +
				"local t, _, _, _, h, _, c, _, q = GetQuestLogTitle(i) " +
				"if not h and c == 1 and q and t then r = r .. q .. '\\1' .. t .. '\\2' end end " +
				"return r", 0U);
			foreach (string tok in (res ?? "").Split('\x02'))
			{
				if (tok.Length == 0) continue;
				int cut = tok.IndexOf('\x01');
				if (cut <= 0) continue;
				if (uint.TryParse(tok.Substring(0, cut), out uint id))
					_completedCache[id] = tok.Substring(cut + 1);
			}
			return _completedCache;
		}

		private static bool WaitFrame(Func<bool> cond, int timeoutMs)
		{
			DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
			while (DateTime.UtcNow < deadline)
			{
				if (cond()) return true;
				StyxWoW.Sleep(50);
			}
			return cond();
		}

		private static void SetTurnInCooldown(ulong guid)
		{
			_turnInCooldown[guid] = DateTime.UtcNow.AddSeconds(15);
		}

		// ──────────────────────────────────────────────────────────────────────
		// Fields
		// ──────────────────────────────────────────────────────────────────────

		private Composite? _root;
		private PartyBus? _bus;
		private PartyLoot? _partyLoot;
		private static PartyWater? _partyWater;   // static so the static FollowLeader can read AwaitingWater
		private WoWPlayer? _waterTarget;
		private const float WaterTradeRange = 8f;
		private static DateTime _nextConjureTry = DateTime.MinValue;   // paces the stock-up cast (see CreateWaterServiceBehavior)
		private static readonly System.Text.Json.JsonSerializerOptions _cmdJson = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
		private static readonly ConcurrentDictionary<ulong, MemberProgress> _partyProgress = new ConcurrentDictionary<ulong, MemberProgress>();
		private static readonly Dictionary<uint, string> _lastBehind = new Dictionary<uint, string>();
		private const int ProgressLivenessSeconds = 30;
		private DateTime _cmdPublishAt = DateTime.MinValue;
		private DateTime _progressReportAt = DateTime.MinValue;
		private DateTime _progressReviewAt = DateTime.MinValue;
		private bool _hooked;
		private bool _leaderHooked;
		private bool _waiting;

		// Lua event flags
		private bool _pendingGroupInvite;
		private bool _pendingDungeonProposal;
		private bool _pendingDungeonOfferContinue;
		private bool _pendingRoleCheck;
		private DateTime _roleCheckActAt = DateTime.MinValue;   // humanized stagger (OnLfgRoleCheckShow)
		private bool _pendingQuestAccept;

		// Static state
		private static BotMessage? _botMessage;
		private static bool _combatEntryStopDone;   // one-shot follow-glue kill on combat entry (reset OOC)
		private static bool _followClosing;         // FollowLeader combat approach latch (boundary-parking hysteresis)

		// Movement narrator: one debug line per movement-mode CHANGE, never per tick — every movement
		// decision must be attributable from the log, or stutter bugs are unfalsifiable. Two authorities
		// fighting over the character shows up as this line flapping between two modes.
		private static string _moveMode = "";
		private static void MoveMode(string mode)
		{
			if (mode == _moveMode) return;
			_moveMode = mode;
			Logging.WriteDebug("VibeParty[move]: " + mode);
		}
		private static bool _wasInCombat;           // Pulse edge for the combat-entry follow-break tap
		private static readonly WaitTimer _waitTimer0 = WaitTimer.TenSeconds;
		private static readonly WaitTimer _waitTimer1 = new WaitTimer(TimeSpan.FromMinutes(3.0));
		private static readonly Dictionary<ulong, DateTime> _turnInCooldown = new Dictionary<ulong, DateTime>();
		private static readonly Dictionary<ulong, int> _turnInFrameFails = new Dictionary<ulong, int>();    // guid → no-progress visits (reset on any progress)
		private static readonly Dictionary<ulong, DateTime> _turnInCloseUntil = new Dictionary<ulong, DateTime>();   // guid → retry must approach to SafeInteractRange until this deadline
		private static readonly HashSet<ulong> _turnInDeadNpc = new HashSet<ulong>();                       // no-flag / 6 stalled visits → dead for the session
		private static readonly ConcurrentDictionary<uint, DateTime> _leaderPickupLog = new ConcurrentDictionary<uint, DateTime>();   // npc entry → leader accepted a quest there (hub thread writes)
		private static bool _leaderSignal;                        // LeaderAtQuestGiver(), cached per tick by WantTurnIn
		private static HashSet<uint>? _lastLogIds;                // previous tick's held quests — the log-shrank edge
		private static WoWUnit? _turnInNpc;                       // resolved once per tick by WantTurnIn(), committed while usable
		private const float TurnInVicinity = 40f;                 // hub radius for the nearest-giver fallback

		// Game-object quest visits: the ender/giver is a WoWGameObject with no "?" — driven off the ledger,
		// bounded by a reachability probe up front (CanReach) and the shared entity-visit throttle. No
		// per-quest failure memory: a refusal we can't attempt in the first place never needs remembering.
		private static WoWGameObject? _goVisitObj;                // resolved per tick by WantGoVisit()
		private static readonly Dictionary<uint, DateTime> _starterCooldown = new Dictionary<uint, DateTime>();
		private static WoWItem? _starterItemCache;
		private static DateTime _starterCheckAt = DateTime.MinValue;
	}
}
