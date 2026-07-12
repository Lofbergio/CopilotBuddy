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
				// Relay follower→follower water requests: a requester's WaterRequest reaches us (target 0); re-
				// broadcast it so the mage follower(s) hear it. (Targeted WaterOffers are auto-forwarded by the bus.)
				_bus.Subscribe("WaterRequest", m => _bus!.Publish("WaterRequest", m.Payload));

				// Phase 1: auto-share each quest we accept with the party (no-ops solo/unshareable).
				if (!_leaderHooked)
				{
					Lua.Events.AttachEvent("QUEST_ACCEPTED", new LuaEventHandlerDelegate(OnQuestAcceptedShare));
					_leaderHooked = true;
				}
				return;
			}

			// Follower: connect to the leader's hub (:1338) and mirror. The client reconnects on its own, so
			// starting before the leader is up is fine — it just retries (fail degraded, never blocks).
			DisableLeaderPluginIfEnabled();
			EnsurePartyProfile();
			if (_bus == null)
				_bus = new PartyBus(isLeader: false, StyxWoW.Me.Guid, StyxWoW.Me.Name);
			if (_partyLoot == null)
				_partyLoot = new PartyLoot(_bus, isLeader: false);   // Phase 5: the lease client
			if (_partyWater == null)
				_partyWater = new PartyWater(_bus);                  // water service (requester + mage)

			if (!_hooked)
			{
				_bus.Subscribe("Command", OnCommandReceived);
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
				// Re-arm the per-fight movement one-shots the moment we drop out of combat state.
				if (!IsInCombatState()) { _combatEntryStopDone = false; _posApproaching = false; }
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
				LeaderInCombat = me.Combat
			};
		}

		// Follower (bot thread): report per-quest completion. Sent even OUTSIDE a party — the report
		// is also the liveness/name beacon AutoInviteTick's roster is built from.
		private static void ReportProgress(PartyBus bus)
		{
			List<PlayerQuest> quests = StyxWoW.Me.QuestLog.GetAllQuests();
			if (quests == null) return;
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			foreach (PlayerQuest q in quests)
			{
				if (q == null || q.Id == 0) continue;
				sb.Append(q.Id).Append(':').Append(q.IsCompleted ? '1' : '0').Append(';');
			}
			bus.Publish("Progress", sb.ToString());
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

		// Follower — auto-accept a trade from the party LEADER (gear/consumables/quest-item handoff). Always on
		// (no toggle): it's safe by construction. Receive-only — the bot never places items on ITS side, so
		// accepting can only take what the leader offers, never give anything away — and gated to the leader BY
		// NAME (UnitName('npc') is the trade partner while a trade window is open, per 3.3.5a TradeFrame), so a
		// random world player can't auto-trade us. Fires on every trade change (show / accept-update / item /
		// money) because WoW clears our accept flag whenever the leader adds an item; re-accepting each change
		// converges by the time the leader clicks. AcceptTrade() when already accepted is a no-op → can't loop.
		private void OnTradeShouldAccept(object sender, LuaEventArgs e)
		{
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
			Lua.DoString("SelectQuestLogEntry(" + e.Args[0] + ") QuestLogPushQuest()");
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
				if (StyxWoW.Me.IsMoving)
					WoWMovement.MoveStop();
				return;
			}

			// (2) Combat initiated → NEVER native-/follow. Native follow is a client-side persistent glue to
			// the LEADER's position; it fights the routine's target-relative positioning, and a melee follower
			// in the approach window (not yet in combat state) trails the tank right PAST its own target. Instead
			// mesh-navigate toward the fight for LoS/range — bot-driven nav the combat branch's positioning takes
			// over the instant we're in combat state (which the approach into pull-range of the leader triggers).
			// Was ranged-only (a Charge/Intercept peel); 2026-07-07 user directive: stop follow for ALL roles the
			// moment combat starts. No assist target yet (leader lining up / friendly target) → close on the leader.
			// Me.Combat too: a follower with its own aggro (leader already done) must never glue itself to the
			// leader mid-fight — a ranged toon dragged sub-melee loses its whole kit (hunter dead zone).
			if (_botMessage.LeaderInCombat || StyxWoW.Me.Combat)
			{
				WoWUnit? assist = LeaderAssistTarget();
				WoWPoint dest = assist != null ? assist.Location : LeaderLocation;
				double stopRange = assist != null ? Targeting.PullDistance : VibePartySettings.Instance.FollowDistance;
				if (dest.Distance(StyxWoW.Me.Location) > stopRange)
					Navigator.MoveTo(dest);
				else if (StyxWoW.Me.IsMoving)
					WoWMovement.MoveStop();
				return;
			}

			// (3) Resting (eat/drink/regen) → hold position; native follow would drag us off the drink the moment
			// the leader drifts. Downtime only — combat above already took priority. 2026-07-07 user directive.
			if (RoutineManager.Current != null && RoutineManager.Current.NeedRest)
			{
				if (StyxWoW.Me.IsMoving)
					WoWMovement.MoveStop();
				return;
			}

			// (4) Awaiting a water handoff → hold still so the party mage can reach and trade us.
			if (_partyWater != null && _partyWater.AwaitingWater)
			{
				if (StyxWoW.Me.IsMoving)
					WoWMovement.MoveStop();
				return;
			}

			WoWPlayer? leader = ObjectManager.GetObjectByGuid<WoWPlayer>(_botMessage.LeaderGuid);
			if (leader != null && leader.Distance <= VibePartySettings.Instance.FollowDistance)
			{
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
					WoWMovement.ClickToMove(LeaderLocation);
				}
				else if (leader.Distance <= 20.0 && leader.InLineOfSight)
				{
					if (!ctmActive)
						Lua.DoString(string.Format("FollowUnit('{0}', true)", leader.Name));
				}
				else if (leader.Distance >= VibePartySettings.Instance.FollowDistance)
				{
					// Out of range or no LoS: mesh-nav to catch up; native follow resumes in range.
					Navigator.MoveTo(LeaderLocation);
				}
			}
			else
			{
				Navigator.MoveTo(LeaderLocation);
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
			// Commit only when the leader is actually IN combat (pull is on) — not while it's just
			// targeting a mob to mark/inspect/line up a pull. This is what keeps organized pulls real.
			if (_botMessage == null || !_botMessage.LeaderInCombat || _botMessage.LeaderTargetGuid == 0) return null;
			WoWUnit? t = ObjectManager.GetObjectByGuid<WoWUnit>(_botMessage.LeaderTargetGuid);
			if (t == null || t.Dead || !t.Attackable || !(t.IsHostile || t.IsNeutral)) return null;
			return t;
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

		private static bool AssistTargetBeyondRange()
		{
			WoWUnit? t = LeaderAssistTarget();
			return t != null && t.Distance > Targeting.PullDistance;
		}

		// Location of the assist target, or our own spot (a no-op move) when there isn't one.
		private static WoWPoint LeaderAssistTargetLocation()
		{
			WoWUnit? t = LeaderAssistTarget();
			return t != null ? t.Location : StyxWoW.Me.Location;
		}

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
							// Combat entry, ONE SHOT: kill any live native-/follow glue (it's client-persistent —
							// left alive it drags us all fight and fights the routine's positioning). After this,
							// movement belongs to the positioning below and to the ROUTINE (backpedal, AoE dodge,
							// Blink) — nothing here may stop it again. Flag resets out of combat state (Pulse).
							new Decorator(ctx => !_combatEntryStopDone,
								new TreeSharp.Action(ctx =>
								{
									_combatEntryStopDone = true;
									if (StyxWoW.Me.IsMoving)
									{
										WoWMovement.MoveStop();
										Logging.WriteDebug("VibeParty: combat entry — cancelling follow/movement");
									}
									return RunStatus.Failure;   // free action — never eats the pass
								})),
							// Assist the leader — focus-fire its target so the routine always has an
							// enemy. One-tick target switch, then falls through to Heal/Buff/Combat.
							new Decorator(ctx => ShouldAssistLeaderTarget(),
								new TreeSharp.Action(ctx => AssistLeaderTarget())),
							// Dismount if needed
							new Decorator(ctx => StyxWoW.Me.Mounted,
								new TreeSharp.Action(ctx => Mount.Dismount("Combat"))),
							// Combat positioning — hold at the routine's engagement range from the ASSIST
							// TARGET (not the tank): ranged stay at spell range, melee close to melee, and
							// nobody chases a Charge into the pack. Approach when beyond range; stop ONCE
							// when OUR OWN approach crosses into range so ranged don't coast into melee.
							// EDGE-triggered on _posApproaching: movement we didn't start (routine backpedal,
							// AoE dodge, Blink reposition) is the routine's commitment — leave it alone; a
							// level-triggered stop here pinned a hunter in its dead zone. Not while casting.
							// In range + stopped, this falls through to Heal/Buff/Combat below.
							new Decorator(ctx => LeaderAssistTarget() != null && !StyxWoW.Me.IsCasting,
								new PrioritySelector(
									new Decorator(ctx => AssistTargetBeyondRange(),
										new Sequence(
											new ActionSetActivity("Positioning"),
											new TreeSharp.Action(ctx => { _posApproaching = true; }),
											new NavigationAction(ctx => LeaderAssistTargetLocation())
										)
									),
									new Decorator(ctx => _posApproaching && StyxWoW.Me.IsMoving,
										new TreeSharp.Action(ctx =>
										{
											_posApproaching = false;
											WoWMovement.MoveStop();
											Logging.WriteDebug("VibeParty: in position — stopping approach");
											return RunStatus.Success;
										})),
									// Approach ended without coasting (nav stopped itself) — just clear the flag.
									new Decorator(ctx => _posApproaching,
										new TreeSharp.Action(ctx => { _posApproaching = false; return RunStatus.Failure; }))
								)
							),
							// Move to POI target if too far (grind-style; skipped when assisting a leader —
							// the target-relative positioning above owns movement in that case).
							new Decorator(ctx => LeaderAssistTarget() == null && BotPoi.Current.AsObject != null && BotPoi.Current.AsObject.Distance > Targeting.PullDistance,
								new Sequence(
									new TreeSharp.Action(ctx => TreeRoot.StatusText = "Moving to target"),
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
				// In instance and dead with alive priest: wait for ress or release after 3 minutes — smethod_72
				new Decorator(ctx => VibePartySettings.Instance.WaitForRessInDungeons
									&& StyxWoW.Me.IsInInstance
									&& StyxWoW.Me.Dead
									&& StyxWoW.Me.PartyMembers.Any(p => p.Class == WoWClass.Priest && p.IsAlive),
					new PrioritySelector(
						// If 3 minutes elapsed: release — smethod_73 fixed (was incorrectly !IsFinished in original)
						new Decorator(ctx => _waitTimer1.IsFinished,
							new Sequence(
								new TreeSharp.Action(ctx => Logging.Write("VibeParty: Waited 3 minutes and we got no ress. Releasing from corpse.")),
								new TreeSharp.Action(ctx => Lua.DoString("RepopMe()"))
							)
						),
						// Else: still waiting — log + keep timer running
						new Sequence(
							new TreeSharp.Action(ctx => Logging.Write("VibeParty: Waiting for ress.")),
							new TreeSharp.Action(ctx => { if (_waitTimer1.IsFinished) _waitTimer1.Reset(); })
						),
						LevelBot.CreateDeathBehavior()
					)
				),
				// Not in instance: normal death behavior
				new Decorator(ctx => !StyxWoW.Me.IsInInstance,
					LevelBot.CreateDeathBehavior())
			);
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
			// Mage only, out of combat, and someone actually asked. Top up our own conjured stock first, then
			// carry a stack to the nearest requester and trade it. Requesters hold still (AwaitingWater) so the
			// handoff is a stationary trade. Sits low in the tree → downtime only, never preempts combat/loot.
			return new Decorator(
				ctx => _partyWater != null && StyxWoW.Me.Class == WoWClass.Mage
					&& !StyxWoW.Me.Combat && _partyWater.HasRequests,
				new PrioritySelector(
					// 1. Stock up (non-blocking cast; each conjure makes a stack).
					new Decorator(
						ctx => PartyWater.ConjuredWaterCount() < MageWaterStock && !StyxWoW.Me.IsCasting && FirstConjureSpell() != null,
						new TreeSharp.Action(ctx => { SpellManager.Cast(FirstConjureSpell()); return RunStatus.Success; })),
					// 2. Deliver to the nearest requester.
					new Decorator(ctx => WantDeliverWater(),
						new PrioritySelector(
							new Decorator(ctx => _waterTarget!.Distance > WaterTradeRange,
								new Sequence(
									new TreeSharp.Action(ctx => { _partyWater!.SendOffer(_waterTarget!.Guid); TreeRoot.StatusText = "Bringing water to " + _waterTarget!.Name; return RunStatus.Success; }),
									new NavigationAction(ctx => _waterTarget!.Location))),
							new Decorator(ctx => !_waterTarget!.IsMoving,
								new TreeSharp.Action(ctx => { TryDeliverWater(_waterTarget!); return RunStatus.Success; }))
						))
				));
		}

		// Resolve the delivery target once per tick (needs water in bags + a servable requester).
		private bool WantDeliverWater()
		{
			_waterTarget = null;
			if (_partyWater == null || PartyWater.ConjuredWaterCount() <= PartyWater.ReserveForSelf) return false;   // keep our reserve
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

		// Synchronous trade: open with the requester, drop one conjured stack, accept. The requester auto-accepts
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
				if (!WaitFrame(TradeOpen, 2500)) { _partyWater.Served(requester.Guid); return; }
			}

			// Give conjured water by COUNT (a conjure doesn't always make a stack), keeping a 5-water self-reserve
			// and handing over up to ~10. All conjured drinks in bag are current rank (stale ones were deleted).
			Lua.DoString(
				"local reserve=5 local giveMax=10 local total=0 " +
				"for b=0,4 do for s=1,GetContainerNumSlots(b) do local l=GetContainerItemLink(b,s) if l and string.find(l,'Conjured') and string.find(l,'Water') then local _,c=GetContainerItemInfo(b,s) total=total+(c or 1) end end end " +
				"local given=0 " +
				"for b=0,4 do for s=1,GetContainerNumSlots(b) do local l=GetContainerItemLink(b,s) if l and string.find(l,'Conjured') and string.find(l,'Water') then local _,c=GetContainerItemInfo(b,s) c=c or 1 if given<giveMax and total-c>=reserve then UseContainerItem(b,s) given=given+c total=total-c end end end end");
			StyxWoW.Sleep(300);
			Lua.DoString("AcceptTrade()");
			WaitFrame(() => !TradeOpen(), 3000);
			_partyWater.Served(requester.Guid);
			Logging.Write(System.Drawing.Color.Aqua, "[VibeParty] handed water to {0}.", requester.Name);
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
				// Quest accept
				new Decorator(ctx => _pendingQuestAccept,
					new Sequence(
						new TreeSharp.Action(ctx => Logging.Write("VibeParty: Accepting shared quest")),
						new TreeSharp.Action(ctx => Lua.DoString("AcceptQuest()")),
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
					new Decorator(ctx => !_turnInNpc!.WithinInteractRange,
						new Sequence(
							new TreeSharp.Action(ctx => TreeRoot.StatusText = "Moving to turn in at " + _turnInNpc!.Name),
							new NavigationAction(ctx => _turnInNpc!.Location))),
					new TreeSharp.Action(ctx => { TryTurnInAt(_turnInNpc!); return RunStatus.Success; })
				));
		}

		// Gate + NPC resolution in ONE pass: sets _turnInNpc for the children this tick (avoids re-scanning the
		// object manager per child lambda). Cheap checks first so most ticks bail before the OM scan.
		private static bool WantTurnIn()
		{
			if (StyxWoW.Me.Combat) { _turnInNpc = null; return false; }
			if (!LeaderAtQuestGiver()) { _turnInNpc = null; return false; }

			// Completed set + its fingerprint, once per tick. The fingerprint keys the per-NPC
			// "nothing to turn in here" verdicts: the quest-giver FLAG is DB data and can't be
			// trusted (an NPC can carry it yet offer nothing live — Merissa Stilwell / 'Welcome!');
			// only the frames a visit actually opens are truth, and a no-hand-in visit is remembered
			// until our completed set changes.
			_completedFp = 17;
			bool any = false;
			List<PlayerQuest> quests = StyxWoW.Me.QuestLog.GetAllQuests();
			if (quests != null)
				foreach (PlayerQuest q in quests.Where(q => q != null && q.IsCompleted).OrderBy(q => q.Id))
				{
					any = true;
					unchecked { _completedFp = _completedFp * 31 + (int)q.Id; }
				}
			// COMMIT to the chosen NPC while it stays usable — re-picking "nearest" every tick
			// flip-flops between givers mid-travel (observed: Eagan→Merissa 1.7s apart).
			if (_turnInNpc != null && _turnInNpc.IsValid && !_turnInNpc.Dead
				&& IsUsableGiver(_turnInNpc) && _turnInNpc.Distance <= TurnInVicinity * 1.5)
				return true;

			if (!any)
			{
				// Nothing to turn in — but the visit can still PICK UP: a share pushes exactly one
				// quest and unshareable follow-ups never arrive at all. Only the leader's own giver
				// (never the nearest-fallback), and the verdict latch (keyed on the empty fingerprint
				// here) makes it one visit per quest-state, not an orbit.
				WoWUnit? lg = _botMessage != null && _botMessage.LeaderTargetGuid != 0
					? ObjectManager.GetObjectByGuid<WoWUnit>(_botMessage.LeaderTargetGuid) : null;
				_turnInNpc = IsUsableGiver(lg) ? lg : null;
				return _turnInNpc != null;
			}

			_turnInNpc = ResolveTurnInNpc();
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

		// Prefer the leader's own giver (it knows the right ender); else the nearest giver in the hub, so a follower
		// that fell behind still offloads. Both skip givers we just poked (per-NPC cooldown) and givers that proved
		// to have nothing for our current completed set (verdict latch).
		private static WoWUnit? ResolveTurnInNpc()
		{
			if (_botMessage != null && _botMessage.LeaderTargetGuid != 0)
			{
				WoWUnit? lt = ObjectManager.GetObjectByGuid<WoWUnit>(_botMessage.LeaderTargetGuid);
				if (IsUsableGiver(lt)) return lt;
			}
			return ObjectManager.GetObjectsOfType<WoWUnit>(false, false)
				.Where(u => IsUsableGiver(u) && u.Distance <= TurnInVicinity)
				.OrderBy(u => u.Distance)
				.FirstOrDefault();
		}

		// The CLIENT already knows whether an NPC has business with us — the server pushes per-NPC
		// quest-giver status (what renders the "!"/"?" markers) and the port exposes it as a memory
		// read (WoWObject.QuestGiverStatus), no interaction needed. This is the pre-filter that stops
		// "walk to a flagged giver and learn it has nothing" entirely: Merissa (deprecated 'Welcome!'
		// relation) reads None and is never visited. Incomplete (grey "?") is excluded too — we're on
		// the quest but not done, nothing to do there. The visit-and-learn latches below stay as
		// backstops for stale or lying status.
		private static bool GiverHasBusiness(WoWUnit u)
		{
			switch (u.QuestGiverStatus)
			{
				case QuestGiverStatus.TurnIn:
				case QuestGiverStatus.TurnInRepeatable:
				case QuestGiverStatus.TurnInInvisible:
				case QuestGiverStatus.LowLevelTurnInRepeatable:
				case QuestGiverStatus.Available:
				case QuestGiverStatus.AvailableRepeatable:
				case QuestGiverStatus.LowLevelAvailable:
				case QuestGiverStatus.LowLevelAvailableRepeatable:
					return true;
				default:
					return false;
			}
		}

		private static bool IsUsableGiver(WoWUnit? u)
			=> u != null && !u.Dead && u.IsQuestGiver
			   && GiverHasBusiness(u)
			   && !_turnInDeadNpc.Contains(u.Guid)
			   && !(_turnInCooldown.TryGetValue(u.Guid, out DateTime until) && DateTime.UtcNow < until)
			   && !(_turnInNothingHere.TryGetValue(u.Guid, out int fp) && fp == _completedFp);

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
				if (me.QuestLog.ContainsQuest((uint)qid)) continue;
				if (completed != null && completed.Contains((uint)qid)) continue;
				if (_starterCooldown.TryGetValue((uint)qid, out DateTime until) && DateTime.UtcNow < until) continue;
				return item;
			}
			return null;
		}

		private static void TryTurnInAt(WoWUnit npc)
		{
			LocalPlayer me = StyxWoW.Me;
			if (me.IsMoving) WoWMovement.MoveStop();

			if (!GossipFrame.Instance.IsVisible && !QuestFrame.Instance.IsVisible)
			{
				npc.Target();
				npc.Interact();
				if (!WaitFrame(() => GossipFrame.Instance.IsVisible || QuestFrame.Instance.IsVisible, 2500))
				{
					// No frame at all. For a pure quest-giver that IS the server saying "nothing for
					// you" — but it could also be range/lag, so latch the verdict only on the second
					// consecutive silent visit; escalate the cooldown meanwhile.
					int fails = _turnInFrameFails.TryGetValue(npc.Guid, out int f) ? f + 1 : 1;
					_turnInFrameFails[npc.Guid] = fails;
					Logging.WriteDebug("VibeParty: {0} opened no frame (attempt {1}).", npc.Name, fails);
					if (fails >= 2) _turnInNothingHere[npc.Guid] = _completedFp;
					// The fingerprint latch re-opens on every quest-state change — but an NPC that has
					// stayed SILENT four times total (never a frame, e.g. Merissa Stilwell holding only
					// the deprecated 'Welcome!' relation) is dead: give it up for the session.
					if (fails >= 4)
					{
						_turnInDeadNpc.Add(npc.Guid);
						Logging.Write("VibeParty: {0} has never opened a frame ({1} visits) — giving up on it this session.", npc.Name, fails);
					}
					_turnInCooldown[npc.Guid] = DateTime.UtcNow.AddSeconds(fails >= 2 ? 120 : 15);
					return;
				}
			}
			_turnInFrameFails.Remove(npc.Guid);

			bool handedIn = false;
			bool attempted = false;   // we FOUND our quest in a frame and tried to complete it

			// Multi-quest giver: hand in each active quest our LOG marks complete (truth = the quest
			// leaving the log, never frame flow — same rule as VibeQuester2's QuestInteraction).
			if (GossipFrame.Instance.IsVisible && !QuestFrame.Instance.IsVisible)
			{
				List<GossipQuestEntry> active = GossipFrame.Instance.ActiveQuests;
				if (active != null)
				{
					foreach (GossipQuestEntry q in active)
					{
						if (q == null || q.Id == 0) continue;
						PlayerQuest held = me.QuestLog.GetQuestById((uint)q.Id);
						if (held == null || !held.IsCompleted) continue;

						GossipFrame.Instance.SelectActiveQuest(q.Index);
						if (WaitFrame(() => QuestFrame.Instance.IsVisible, 2500))
						{
							attempted = true;
							if (CompleteShownQuest((uint)q.Id)) handedIn = true;
						}
						if (!GossipFrame.Instance.IsVisible) break;
					}
				}
			}
			// QUEST GREETING panel (multi-quest ender without gossip): QuestFrame is up but no quest is
			// "shown" — the old code read CurrentShownQuestId=0 as "nothing here" and every greeting NPC
			// silently no-op'd. The 3.3.5 greeting Lua has no isComplete flag, so match TITLES against
			// our own completed log entries and select only those; completing returns to the greeting
			// (or closes it), so rescan until no completed title remains.
			else if (QuestFrame.Instance.IsVisible && QuestFrame.Instance.CurrentShownQuestId == 0)
			{
				for (int guard = 0; guard < 8 && QuestFrame.Instance.IsVisible
					 && QuestFrame.Instance.CurrentShownQuestId == 0; guard++)
				{
					var completedTitles = new HashSet<string>(
						me.QuestLog.GetAllQuests().Where(q => q != null && q.IsCompleted).Select(q => q.Name),
						StringComparer.OrdinalIgnoreCase);
					int n = Lua.GetReturnVal<int>("return GetNumActiveQuests()", 0U);
					int pick = 0;
					for (int i = 1; i <= n; i++)
					{
						string title = Lua.GetReturnVal<string>("return (GetActiveTitle(" + i + "))", 0U);
						if (!string.IsNullOrEmpty(title) && completedTitles.Contains(title)) { pick = i; break; }
					}
					if (pick == 0) break;
					Lua.DoString("SelectActiveQuest({0})", pick);
					if (!WaitFrame(() => QuestFrame.Instance.CurrentShownQuestId != 0, 2500)) break;
					uint shownId = QuestFrame.Instance.CurrentShownQuestId;
					PlayerQuest heldQ = shownId != 0 ? me.QuestLog.GetQuestById(shownId) : null;
					if (heldQ == null || !heldQ.IsCompleted) break;
					attempted = true;
					if (CompleteShownQuest(shownId)) handedIn = true;
					else break;
				}
			}
			// Single-quest giver: detail frame straight away.
			else if (QuestFrame.Instance.IsVisible)
			{
				uint shown = QuestFrame.Instance.CurrentShownQuestId;
				PlayerQuest held = shown != 0 ? me.QuestLog.GetQuestById(shown) : null;
				if (held != null && held.IsCompleted)
				{
					attempted = true;
					if (CompleteShownQuest(shown)) handedIn = true;
				}
			}

			// ---- Pickup phase: hoover every AVAILABLE quest while we're here. The frame's available
			// list already excludes quests we're on, so "we have quest 1, grab quest 2" needs no
			// special skipping — the list IS the skip. Guards: never re-take a finished quest, verify
			// acceptance by the quest ENTERING the log (never frame flow).
			int pickedUp = 0;
			var doneBefore = me.QuestLog.GetCompletedQuests();

			if (GossipFrame.Instance.IsVisible)
			{
				for (int guard = 0; guard < 8 && GossipFrame.Instance.IsVisible; guard++)
				{
					GossipQuestEntry pick = null;
					List<GossipQuestEntry> avail = GossipFrame.Instance.AvailableQuests;
					if (avail != null)
						foreach (GossipQuestEntry q in avail)
						{
							if (q == null || q.Id == 0) continue;
							if (me.QuestLog.ContainsQuest((uint)q.Id)) continue;
							if (doneBefore != null && doneBefore.Contains((uint)q.Id)) continue;
							pick = q;
							break;
						}
					if (pick == null) break;
					GossipFrame.Instance.SelectAvailableQuest(pick.Index);
					if (!WaitFrame(() => QuestFrame.Instance.IsVisible, 2500)) break;
					QuestFrame.Instance.AcceptQuest();
					if (WaitFrame(() => me.QuestLog.ContainsQuest((uint)pick.Id), 2500)) pickedUp++;
					else break;
				}
			}
			else if (QuestFrame.Instance.IsVisible && QuestFrame.Instance.CurrentShownQuestId == 0)
			{
				// Greeting panel: always select available slot 1 — an accepted quest leaves the
				// available list, so slot 1 is always "the next one".
				for (int guard = 0; guard < 8; guard++)
				{
					if (!QuestFrame.Instance.IsVisible || QuestFrame.Instance.CurrentShownQuestId != 0) break;
					if (Lua.GetReturnVal<int>("return GetNumAvailableQuests()", 0U) < 1) break;
					Lua.DoString("SelectAvailableQuest(1)");
					if (!WaitFrame(() => QuestFrame.Instance.CurrentShownQuestId != 0, 2500)) break;
					uint offered = QuestFrame.Instance.CurrentShownQuestId;
					if (me.QuestLog.ContainsQuest(offered)) break;   // shouldn't happen — bail, don't loop
					if (doneBefore != null && doneBefore.Contains(offered)) break;
					QuestFrame.Instance.AcceptQuest();
					if (WaitFrame(() => me.QuestLog.ContainsQuest(offered), 2500)) pickedUp++;
					else break;
				}
			}
			else if (QuestFrame.Instance.IsVisible)
			{
				// Straight detail frame with an un-held quest = a single-quest giver OFFERING it.
				uint offered = QuestFrame.Instance.CurrentShownQuestId;
				if (offered != 0 && !me.QuestLog.ContainsQuest(offered)
					&& (doneBefore == null || !doneBefore.Contains(offered)))
				{
					QuestFrame.Instance.AcceptQuest();
					if (WaitFrame(() => me.QuestLog.ContainsQuest(offered), 2500)) pickedUp++;
				}
			}

			if (GossipFrame.Instance.IsVisible) GossipFrame.Instance.Close();
			else if (QuestFrame.Instance.IsVisible) QuestFrame.Instance.Close();

			if (handedIn)
				Logging.Write("VibeParty: Turned in quest(s) at {0}.", npc.Name);
			if (pickedUp > 0)
				Logging.Write("VibeParty: Picked up {0} quest(s) at {1}.", pickedUp, npc.Name);
			if (!handedIn && pickedUp == 0)
			{
				if (attempted)
				{
					// We FOUND our quest here and the completion flow failed — that's OUR bug or lag,
					// NOT "nothing here". A latch would block this quest until the fingerprint changes
					// (the Militia Hammer poisoning, first run); retry on the normal cooldown and shout.
					Logging.Write(System.Drawing.Color.Orange,
						"VibeParty: turn-in FAILED at {0} — the quest did not leave the log; will retry.", npc.Name);
				}
				else
				{
					// The frames were the Lua truth and offered nothing for us either way — latch that
					// verdict for the CURRENT completed set so we never orbit this NPC again until our
					// quest state changes (the DB's quest-giver flag alone proved untrustworthy).
					_turnInNothingHere[npc.Guid] = _completedFp;
					Logging.WriteDebug("VibeParty: nothing to do at {0} — skipping until quest state changes.", npc.Name);
				}
			}
			SetTurnInCooldown(npc.Guid);
		}

		private static bool CompleteShownQuest(uint questId)
		{
			// PROGRESS panel first ("Continue" — the Lua CompleteQuest() advances it): reward choices are
			// only CLICKABLE on the REWARD panel. The old order selected the reward first — the quest
			// CACHE made the choice look selectable (ActionSelectReward logged "Choosing …"), but its
			// QuestInfoItem click no-ops on the progress panel — then raced the panel flip, so every
			// reward-CHOICE quest silently failed to hand in (Militia Hammer at Deputy Willem, first run).
			if (Lua.GetReturnVal<int>("return (QuestFrameProgressPanel and QuestFrameProgressPanel:IsShown()) and 1 or 0", 0U) == 1)
			{
				Lua.DoString("CompleteQuest()");
				WaitFrame(() => Lua.GetReturnVal<int>(
					"return (QuestFrameRewardPanel and QuestFrameRewardPanel:IsShown()) and 1 or 0", 0U) == 1, 2000);
			}
			if (Lua.GetReturnVal<int>("return GetNumQuestChoices()", 0U) >= 1)
			{
				try
				{
					Bots.Quest.Actions.ActionSelectReward pick = new Bots.Quest.Actions.ActionSelectReward();
					pick.Start(null); pick.Tick(null); pick.Stop(null);
				}
				catch { QuestFrame.Instance.SelectQuestReward(0); }
				StyxWoW.Sleep(300);
			}
			// The reward-panel button routes GetQuestReward(itemChoice) with the selection made above.
			Lua.DoString("if QuestFrameCompleteQuestButton and QuestFrameCompleteQuestButton:IsVisible() then QuestFrameCompleteQuestButton:Click() end");
			return WaitFrame(() => !StyxWoW.Me.QuestLog.ContainsQuest(questId), 2500);
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
		private const int MageWaterStock = 25;    // mage conjures until it holds this (~5 reserve + a couple handouts)
		private const float WaterTradeRange = 8f;
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
		private static bool _posApproaching;        // our positioning approach is the active movement
		private static readonly WaitTimer _waitTimer0 = WaitTimer.TenSeconds;
		private static readonly WaitTimer _waitTimer1 = new WaitTimer(TimeSpan.FromMinutes(3.0));
		private static readonly Dictionary<ulong, DateTime> _turnInCooldown = new Dictionary<ulong, DateTime>();
		private static readonly Dictionary<ulong, int> _turnInNothingHere = new Dictionary<ulong, int>();   // guid → completed-set fingerprint at verdict time
		private static readonly Dictionary<ulong, int> _turnInFrameFails = new Dictionary<ulong, int>();    // guid → no-frame interacts (reset only on a frame opening)
		private static readonly HashSet<ulong> _turnInDeadNpc = new HashSet<ulong>();                       // 4+ silent visits → dead for the session
		private static int _completedFp;                          // fingerprint of our completed quest ids (per tick, WantTurnIn)
		private static WoWUnit? _turnInNpc;                       // resolved once per tick by WantTurnIn(), committed while usable
		private const float TurnInVicinity = 40f;                 // hub radius for the nearest-giver fallback
		private static readonly Dictionary<uint, DateTime> _starterCooldown = new Dictionary<uint, DateTime>();
		private static WoWItem? _starterItemCache;
		private static DateTime _starterCheckAt = DateTime.MinValue;
	}
}
