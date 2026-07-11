using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Bots.Grind;
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

				// Phase 1: auto-share each quest we accept with the party (native /share).
				if (VibePartySettings.Instance.ShareQuestsToParty && !_leaderHooked)
				{
					Lua.Events.AttachEvent("QUEST_ACCEPTED", new LuaEventHandlerDelegate(OnQuestAcceptedShare));
					_leaderHooked = true;
				}
				return;
			}

			// Follower: connect to the leader's hub (:1338) and mirror. The client reconnects on its own, so
			// starting before the leader is up is fine — it just retries (fail degraded, never blocks).
			DisableLeaderPluginIfEnabled();
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

			DetachLuaEvents();
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
				LeaderGuid = me.Guid,
				LeaderName = me.Name,
				Timestamp = DateTime.Now,
				LeaderTargetGuid = me.CurrentTargetGuid,
				LeaderInCombat = me.Combat
			};
		}

		// Follower (bot thread): report per-quest completion. Sent even OUTSIDE a party — the report
		// doubles as the liveness/name beacon AutoInviteTick's roster is built from (the old in-party
		// early-return was a chicken-and-egg: no heartbeat until invited, no invite without a heartbeat).
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

		// Leader (bot thread): invite every LIVE bus member that isn't in the group yet — press Start on
		// five toons and the party forms itself. Roster = _partyProgress (each follower's heartbeat carries
		// its name); the follower side auto-accepts via AcceptGroupInvitesFromLeader. Safe by construction:
		// the hub is localhost, so only OUR OWN toons can ever be on the roster. Declines just re-invite
		// after the per-toon cooldown; a full party (4 + me) stops inviting; raids are out of scope.
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
				_pendingGroupInvite = VibePartySettings.Instance.AcceptGroupInvitesFromLeader && name == _botMessage.LeaderName;
			}
		}

		// method_7 — LFG_PROPOSAL_SHOW
		private void OnLfgProposalShow(object sender, LuaEventArgs e)
		{
			_pendingDungeonProposal = VibePartySettings.Instance.AcceptDungeonInvites;
		}

		// method_8 — LFG_OFFER_CONTINUE
		private void OnLfgOfferContinue(object sender, LuaEventArgs e)
		{
			_pendingDungeonOfferContinue = VibePartySettings.Instance.AcceptDungeonInvites;
		}

		// method_9 — LFG_ROLE_CHECK_SHOW
		private void OnLfgRoleCheckShow(object sender, LuaEventArgs e)
		{
			_pendingRoleCheck = VibePartySettings.Instance.AcceptDungeonInvites;
		}

		// method_10 — QUEST_DETAIL
		private void OnQuestDetail(object sender, LuaEventArgs e)
		{
			_pendingQuestAccept = VibePartySettings.Instance.AutoAcceptSharedQuests;
		}

		// Phase 1 (follower) — auto-confirm quests the leader shares. Escort / auto-accept shares
		// raise QUEST_ACCEPT_CONFIRM instead of QUEST_DETAIL; gated on the same opt-in.
		private void OnQuestAcceptConfirm(object sender, LuaEventArgs e)
		{
			if (VibePartySettings.Instance.AutoAcceptSharedQuests)
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
			if (_botMessage.LeaderInCombat)
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
							// Assist the leader — focus-fire its target so the routine always has an
							// enemy. One-tick target switch, then falls through to Heal/Buff/Combat.
							new Decorator(ctx => ShouldAssistLeaderTarget(),
								new TreeSharp.Action(ctx => AssistLeaderTarget())),
							// Dismount if needed
							new Decorator(ctx => StyxWoW.Me.Mounted,
								new TreeSharp.Action(ctx => Mount.Dismount("Combat"))),
							// Combat positioning — hold at the routine's engagement range from the ASSIST
							// TARGET (not the tank): ranged stay at spell range, melee close to melee, and
							// nobody chases a Charge into the pack. Approach when beyond range; stop the
							// instant we're within it so ranged don't coast into melee. Not while casting.
							// In range + stopped, this falls through to Heal/Buff/Combat below.
							new Decorator(ctx => LeaderAssistTarget() != null && !StyxWoW.Me.IsCasting,
								new PrioritySelector(
									new Decorator(ctx => AssistTargetBeyondRange(),
										new Sequence(
											new ActionSetActivity("Positioning"),
											new NavigationAction(ctx => LeaderAssistTargetLocation())
										)
									),
									new Decorator(ctx => StyxWoW.Me.IsMoving,
										new TreeSharp.Action(ctx => WoWMovement.MoveStop()))
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
				// Role check
				new Decorator(ctx => _pendingRoleCheck,
					new Sequence(
						new TreeSharp.Action(ctx => Logging.Write("VibeParty: Role Check is in progress")),
						new TreeSharp.Action(ctx => Lua.DoString("LFDRoleCheckPopupAcceptButton:Click() StaticPopup1Button1:Click()")),
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
			_turnInNpc = null;
			if (!VibePartySettings.Instance.AutoTurnInQuests || StyxWoW.Me.Combat) return false;
			if (!LeaderAtQuestGiver() || !HasCompletableQuest()) return false;
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

		private static bool HasCompletableQuest()
		{
			List<PlayerQuest> quests = StyxWoW.Me.QuestLog.GetAllQuests();
			return quests != null && quests.Any(q => q != null && q.IsCompleted);
		}

		// Prefer the leader's own giver (it knows the right ender); else the nearest giver in the hub, so a follower
		// that fell behind still offloads. Both skip givers we just poked (per-NPC cooldown).
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

		private static bool IsUsableGiver(WoWUnit? u)
			=> u != null && !u.Dead && u.IsQuestGiver
			   && !(_turnInCooldown.TryGetValue(u.Guid, out DateTime until) && DateTime.UtcNow < until);

		// ──────────────────────────────────────────────────────────────────────
		// Quest-starter items (Phase 1) — accept drop-only, item-started quests
		// ──────────────────────────────────────────────────────────────────────

		// Some quests begin by USING a drop-only item (ItemInfo.BeginQuestId), which /share can't push — so a
		// follower that looted the same item must use ITS OWN copy. Using it raises QUEST_DETAIL, which the existing
		// accept handler auto-confirms. Gated on AutoAcceptSharedQuests (same mirror-intent opt-in), never while
		// combat / casting / a frame is open. Per-quest cooldown so a slow accept or a level-gated item can't spam.
		private static Composite CreateUseQuestStarterBehavior()
		{
			return new Decorator(
				ctx => VibePartySettings.Instance.AutoAcceptSharedQuests
					&& !StyxWoW.Me.Combat && !StyxWoW.Me.IsCasting
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
					SetTurnInCooldown(npc.Guid);
					return;
				}
			}

			bool handedIn = false;

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
						if (WaitFrame(() => QuestFrame.Instance.IsVisible, 2500) && CompleteShownQuest((uint)q.Id))
							handedIn = true;
						if (!GossipFrame.Instance.IsVisible) break;
					}
				}
			}
			// Single-quest giver: detail frame straight away.
			else if (QuestFrame.Instance.IsVisible)
			{
				uint shown = QuestFrame.Instance.CurrentShownQuestId;
				PlayerQuest held = shown != 0 ? me.QuestLog.GetQuestById(shown) : null;
				if (held != null && held.IsCompleted && CompleteShownQuest(shown))
					handedIn = true;
			}

			if (GossipFrame.Instance.IsVisible) GossipFrame.Instance.Close();
			else if (QuestFrame.Instance.IsVisible) QuestFrame.Instance.Close();

			if (handedIn)
				Logging.Write("VibeParty: Turned in quest(s) at {0}.", npc.Name);
			SetTurnInCooldown(npc.Guid);
		}

		private static bool CompleteShownQuest(uint questId)
		{
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
			QuestFrame.Instance.CompleteQuest();
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
		private bool _pendingQuestAccept;

		// Static state
		private static BotMessage? _botMessage;
		private static readonly WaitTimer _waitTimer0 = WaitTimer.TenSeconds;
		private static readonly WaitTimer _waitTimer1 = new WaitTimer(TimeSpan.FromMinutes(3.0));
		private static readonly Dictionary<ulong, DateTime> _turnInCooldown = new Dictionary<ulong, DateTime>();
		private static WoWUnit? _turnInNpc;                       // resolved once per tick by WantTurnIn()
		private const float TurnInVicinity = 40f;                 // hub radius for the nearest-giver fallback
		private static readonly Dictionary<uint, DateTime> _starterCooldown = new Dictionary<uint, DateTime>();
		private static WoWItem? _starterItemCache;
		private static DateTime _starterCheckAt = DateTime.MinValue;
	}
}
