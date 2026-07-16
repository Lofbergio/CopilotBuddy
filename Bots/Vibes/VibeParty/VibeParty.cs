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
					// Absolutely idle — do nothing at all (no buffing either).
					new Decorator(ctx => _waiting || VibePartySettings.Instance.DoNothing, new ActionIdle()),
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
					CreateTurnInBehavior(),                           // Phase 2 — turn in at the leader's NPC
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
				AttachLuaEvents();
				Vendors.OnVendorItems += OnVendorSweep;   // disposition: only true junk sells
				Vendors.OnMailItems += OnMailSweep;       // disposition: valuables queue for the bank
				LootTargeting.Instance.IncludeTargetsFilter += LevelBot.LevelbotIncludeLootsFilter;
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
				_partyWater?.WaterTick();   // requester: ask for water when low; mage: advertise + clean stale (throttled)
				LoadMailboxesIfMapChanged();   // keep the synthetic profile's mailboxes on the current map
				MirrorLeaderTeleportTick();    // LFG: match the leader's inside/outside-the-dungeon state
				MirrorLeaderBagsTick();        // bag-visibility sync: leader's bags open ⇒ ours open
				ProcessAbandonQueue();         // drop quests the leader abandoned (block set stops re-accepts)
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

		// The command state followers mirror — faithful to the legacy LeaderPlugin.Pulse minus its private
		// IncludeTargetsFilter (the follower's assist keys on LeaderTargetGuid = our live target, not the Kill
		// message's TargetGuid, so a human leader's current target is all we need).
		private static BotMessage BuildLeaderCommand()
		{
			LocalPlayer me = StyxWoW.Me;
			WoWPoint loc = me.Location;
			string type;
			ulong targetGuid = 0;
			PoiType poi = BotPoi.Current.Type;
			if (me.Combat && me.CurrentTarget != null && !me.CurrentTarget.Dead)
			{
				type = "Kill";
				targetGuid = me.CurrentTargetGuid;
			}
			else if ((poi == PoiType.Repair || poi == PoiType.Sell)
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
				LeaderBagsOpen = LeaderBagsOpenCached()
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
			string leader = _botMessage?.LeaderName;
			if (string.IsNullOrEmpty(leader)) return;
			Lua.DoString("SendChatMessage('" + msg.Replace("'", "") + "', 'WHISPER', nil, '" + leader.Replace("'", "") + "')");
		}

		// Leader (bot thread): invite every LIVE bus member not yet grouped. Roster = _partyProgress
		// (the follower heartbeat carries its name). Localhost hub ⇒ only our own toons can be on it.
		private DateTime _inviteTickAt = DateTime.MinValue;
		private readonly Dictionary<ulong, DateTime> _invitedAt = new Dictionary<ulong, DateTime>();

		private void AutoInviteTick()
		{
			if ((DateTime.Now - _inviteTickAt).TotalSeconds < 5) return;
			_inviteTickAt = DateTime.Now;
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
			_pendingQuestAccept = true;
		}

		// Phase 1 (follower) — auto-confirm quests the leader shares. Escort / auto-accept shares
		// raise QUEST_ACCEPT_CONFIRM instead of QUEST_DETAIL; always on like the detail accept.
		private void OnQuestAcceptConfirm(object sender, LuaEventArgs e)
		{
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
			_leaderQuests[id] = 1;
		}

		// Leader's current quest-log snapshot (csv) — UNION into the whitelist (never replace: turned-in ids
		// must persist for chain prereqs). Seeds a follower that connected after the leader took its quests.
		private static void OnLeaderQuestsMsg(PartyMessage m)
		{
			if (string.IsNullOrEmpty(m.Payload)) return;
			foreach (string tok in m.Payload.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
				if (uint.TryParse(tok, out uint id) && id != 0 && !QuestBlocked(id))
					_leaderQuests[id] = 1;
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

		private void OnPartyChat(WoWChat.ChatLanguageSpecificEventArgs e)
		{
			if (_botMessage == null) return;
			WoWPlayer? leader = ObjectManager.GetObjectByGuid<WoWPlayer>(_botMessage.LeaderGuid);
			if (leader == null || e.Author != leader.Name) return;

			foreach (string token in e.Message.Split(new[] { "!" }, StringSplitOptions.RemoveEmptyEntries))
			{
				switch (token)
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
					case "dismount":
						Mount.Dismount("Request from Leader");
						break;
					case "mountup":
						Mount.MountUp(new LocationRetriever(() => WoWPoint.Zero));
						break;
					case "wait":
						_waiting = !_waiting;
						break;
					case "interact":
						leader.CurrentTarget?.Interact();
						break;
					case "follow":
						// Manual "everyone follow me" — clear any wait-hold and snap to native /follow now.
						Logging.Write("VibeParty: leader said follow — engaging follow.");
						_waiting = false;
						if (leader != null && !StyxWoW.Me.Combat)
							Lua.DoString(string.Format("FollowUnit('{0}', true)", leader.Name));
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
			List<ulong> partyGuids = StyxWoW.Me.PartyMemberGuids.ToList();
			List<ulong> raidGuids  = StyxWoW.Me.RaidMemberGuids.ToList();
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
								new TreeSharp.Action(ctx => FollowLeader())),
							new SwitchArgument<string>("Kill",
								new Decorator(
									ctx => LeaderLocation.Distance(StyxWoW.Me.Location) <= Targeting.PullDistance,
									new Sequence(
										ctx => _botMessage != null ? ObjectManager.GetObjectByGuid<WoWUnit>(_botMessage.TargetGuid) : null,
										new Decorator(
											ctx => ctx != null && ctx is WoWUnit && ((WoWUnit)ctx).BaseAddress != 0U && ((WoWUnit)ctx).Distance <= 40.0,
											new Sequence(
												new DecoratorContinue(ctx => StyxWoW.Me.CurrentTarget != (WoWUnit)ctx,
													new TreeSharp.Action(ctx => ((WoWUnit)ctx).Target())),
												new TreeSharp.Action(ctx => BotPoi.Current = new BotPoi((WoWUnit)ctx, PoiType.Kill)),
												new TreeSharp.Action(ctx => Logging.WriteDebug("VibeParty: Killing something"))
											)
										)
									)
								)
							)
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
		// true when: have a target AND (not mounted AND in combat, or party member in combat within pull range,
		//             or pet is in combat)
		private static bool IsInCombatState()
		{
			if (Targeting.Instance.FirstUnit == null) return false;
			if (!StyxWoW.Me.Mounted)
			{
				if (StyxWoW.Me.Combat) return true;
				if (StyxWoW.Me.PartyMembers.Any(p => p.Combat)
					&& LeaderLocation.Distance(StyxWoW.Me.Location) <= Targeting.PullDistance)
					return true;
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
			// not while it's just targeting a mob to mark/inspect. This keeps organized pulls real.
			if (_botMessage != null && _botMessage.LeaderInCombat && _botMessage.LeaderTargetGuid != 0)
			{
				WoWUnit? t = ObjectManager.GetObjectByGuid<WoWUnit>(_botMessage.LeaderTargetGuid);
				if (t != null && !t.Dead && t.Attackable && (t.IsHostile || t.IsNeutral)) return t;
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
						ctx => PartyWater.ConjuredWaterCount() < MageWaterStock
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
			if (_partyWater == null || PartyWater.ConjuredWaterCount() < PartyWater.ReserveForSelf + WaterStackGive) return false;
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

		// Synchronous trade: open with the requester, place water, accept. The requester auto-accepts
		// (OnTradeShouldAccept, party-member gate) and re-accepts when our item lands. Per-requester cooldown after.
		private void TryDeliverWater(WoWPlayer requester)
		{
			if (StyxWoW.Me.IsMoving) WoWMovement.MoveStop();
			if (requester.Distance > WaterTradeRange) return;
			_partyWater!.SendOffer(requester.Guid);   // tell them to hold still

			if (!TradeOpen())
			{
				requester.Target();
				Lua.DoString("InitiateTrade('target')");
				if (!WaitFrame(TradeOpen, 2500))
				{
					_partyWater.Served(requester.Guid);   // cooldown, then retry — never a silent drop
					Logging.Write(System.Drawing.Color.Aqua, "[VibeParty] trade with {0} never opened — will retry after cooldown.", requester.Name);
					return;
				}
			}

			// Give one full STACK (20) by COUNT, splitting bag stacks as needed to assemble it. All conjured
			// drinks in bag count (stale ranks get purged lazily).
			int given = Lua.GetReturnVal<int>(
				"local reserve=5 local giveMax=20 local total=0 " +
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
			WaitFrame(() => !TradeOpen(), 3000);
			_partyWater.Served(requester.Guid);
			Logging.Write(System.Drawing.Color.Aqua, "[VibeParty] handed {0} water to {1}.", given, requester.Name);
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
							uint shown = QuestFrame.Instance.CurrentShownQuestId;
							if (QuestBlocked(shown))
							{
								Logging.Write("VibeParty: declining shared quest {0} — the leader abandoned it.", shown);
								Lua.DoString("DeclineQuest()");
							}
							else
							{
								if (shown != 0 && !IsLeaderQuest(shown))
								{
									// Not one of the leader's quests — a random "!" the follower stumbled onto. The
									// party only does the leader's quests (completing anything already held is fine).
									Logging.Write("VibeParty: declining quest {0} - not one of the leader's quests.", shown);
									Lua.DoString("DeclineQuest()");
								}
								else
								{
									Logging.Write("VibeParty: Accepting shared quest");
									Lua.DoString("AcceptQuest()");
								}
							}
							return RunStatus.Success;
						}),
						new TreeSharp.Action(ctx => _pendingQuestAccept = false)
					)
				)
			);
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
					new TreeSharp.Action(ctx => { VisitQuestNpc(_turnInNpc!); return RunStatus.Success; })
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
				if (QuestBlocked((uint)qid)) continue;   // leader abandoned it — don't restart it from the item
				if (!IsLeaderQuest((uint)qid)) continue;   // only start quests the leader is doing (accept whitelist)
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
		private static void VisitQuestNpc(WoWUnit npc)
		{
			LocalPlayer me = StyxWoW.Me;
			if (me.IsMoving) WoWMovement.MoveStop();

			// The worklist: ledger business (turn-ins first), plus any complete log quest the ledger
			// didn't map when the NPC shows OUR "?" (DB gaps) — the core's NotOffered answer is cheap.
			List<QuestWork> work = FollowerQuestLedger.BusinessAt(npc.Entry);
			if (GiverHasTurnIn(npc))
			{
				foreach (var kv in CompletedQuestsLua())
				{
					bool mapped = false;
					foreach (QuestWork w in work)
						if (w.TurnIn && w.QuestId == kv.Key) { mapped = true; break; }
					if (!mapped) work.Add(new QuestWork { QuestId = kv.Key, Title = kv.Value, TurnIn = true });
				}
			}

			if (work.Count == 0)
			{
				TiDebug("visit {0} [{1}] status={2}: no ledger business — cooldown.", npc.Name, npc.Entry, npc.QuestGiverStatus);
				SetTurnInCooldown(npc.Guid);
				return;
			}

			TiDebug("visit {0} [{1}] status={2}: {3}", npc.Name, npc.Entry, npc.QuestGiverStatus,
				string.Join(", ", work.Select(w => (w.TurnIn ? "turnin " : "pickup ") + w.QuestId + " '" + w.Title + "'")));

			int turnedIn = 0, pickedUp = 0, retries = 0, refused = 0;
			foreach (QuestWork w in work)
			{
				if (me.Combat) break;   // combat reactivity between per-quest transactions
				QuestInteractOutcome outcome = w.TurnIn
					? QuestInteractionCore.TurnIn(npc, (int)w.QuestId, w.Title, "VibeParty[quest]",
						chained => IsLeaderQuest(chained) && !QuestBlocked(chained))
					: QuestInteractionCore.PickUp(npc, (int)w.QuestId, w.Title, "VibeParty[quest]");
				switch (outcome)
				{
					case QuestInteractOutcome.Success:
						if (w.TurnIn) turnedIn++; else pickedUp++;
						break;
					case QuestInteractOutcome.Retry:
						retries++;
						break;
					case QuestInteractOutcome.NotOffered:
						refused++;
						TiDebug("q{0} '{1}' refused by {2} — a server gate the ledger can't see (exclusive group / hidden prereq).", w.QuestId, w.Title, npc.Name);
						break;
					case QuestInteractOutcome.NoGiverFlag:
						_turnInDeadNpc.Add(npc.Guid);
						Logging.Write("VibeParty: {0} carries no questgiver flag — giving up on it this session.", npc.Name);
						SetTurnInCooldown(npc.Guid);
						return;
				}
			}

			if (GossipFrame.Instance.IsVisible) GossipFrame.Instance.Close();
			else if (QuestFrame.Instance.IsVisible) QuestFrame.Instance.Close();

			if (turnedIn > 0) Logging.Write("VibeParty: Turned in {0} quest(s) at {1}.", turnedIn, npc.Name);
			if (pickedUp > 0) Logging.Write("VibeParty: Picked up {0} quest(s) at {1}.", pickedUp, npc.Name);

			if (turnedIn == 0 && pickedUp == 0 && retries > 0)
			{
				// Transient (frame never opened / log lag): escalate like the old no-frame ladder so a
				// ghost NPC can't orbit us forever, while a lag blip still retries quickly.
				int fails = _turnInFrameFails.TryGetValue(npc.Guid, out int f) ? f + 1 : 1;
				_turnInFrameFails[npc.Guid] = fails;
				Logging.Write(System.Drawing.Color.Orange,
					"VibeParty: quest visit at {0} made no progress (attempt {1}) — will retry.", npc.Name, fails);
				if (fails >= 6)
				{
					_turnInDeadNpc.Add(npc.Guid);
					Logging.Write("VibeParty: {0} has made no progress in {1} visits — giving up on it this session.", npc.Name, fails);
				}
				int cooldownSec = fails >= 2 ? 120 : 15;
				_turnInCooldown[npc.Guid] = DateTime.UtcNow.AddSeconds(cooldownSec);
				// The retry must approach tighter (NeedTightApproach); +12s of travel budget past the
				// cooldown covers the ≤1yd close-in plus a stuck-jiggle before the fallback kicks in.
				_turnInCloseUntil[npc.Guid] = DateTime.UtcNow.AddSeconds(cooldownSec + 12);
				return;
			}
			_turnInFrameFails.Remove(npc.Guid);
			_turnInCloseUntil.Remove(npc.Guid);

			if (turnedIn == 0 && pickedUp == 0 && refused > 0)
			{
				// Everything the ledger predicted got a server "no" — the ledger re-screens as our
				// state changes, so a longer cooldown (not a session latch) is enough.
				_turnInCooldown[npc.Guid] = DateTime.UtcNow.AddSeconds(60);
				return;
			}
			SetTurnInCooldown(npc.Guid);
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
		private const int WaterStackGive = 20;    // one full stack per delivery (refill-on-empty model)
		private const int MageWaterStock = 25;    // reserve (5) + one full stack handout (20)
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
		private static WoWUnit? _turnInNpc;                       // resolved once per tick by WantTurnIn(), committed while usable
		private const float TurnInVicinity = 40f;                 // hub radius for the nearest-giver fallback
		private static readonly Dictionary<uint, DateTime> _starterCooldown = new Dictionary<uint, DateTime>();
		private static WoWItem? _starterItemCache;
		private static DateTime _starterCheckAt = DateTime.MinValue;
	}
}
