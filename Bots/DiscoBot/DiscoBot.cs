using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Bots.Grind;
using CommonBehaviors;
using CommonBehaviors.Actions;
using CommonBehaviors.Decorators;
using PartyBot.Forms;
using PartyBot.IPC;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Plugins;
using Styx.RemotableObjects;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace PartyBot
{
	public class DiscoBot : BotBase
	{
		// ──────────────────────────────────────────────────────────────────────
		// BotBase overrides
		// ──────────────────────────────────────────────────────────────────────

		public override string Name => "Party Bot";

		public override Composite Root
		{
			get
			{
				if (_root != null) return _root;
				_root = new PrioritySelector(
					new Decorator(ctx => _waiting || PartyBotSettings.Instance.DoNothing, new ActionIdle()),
					CreateDeathBehavior(),
					CreateCombatBehavior(),                           // smethod_5
					CreateEventBehavior(),                            // method_5
					CreateLootBehavior(),                             // method_3
					new Decorator(ctx => !StyxWoW.Me.IsInInstance && !Battlegrounds.IsInsideBattleground, LevelBot.CreateVendorBehavior()),
					CreateFollowBehavior()                            // smethod_0
				);
				return _root;
			}
		}

		public override PulseFlags PulseFlags => PulseFlags.All;

		public override Form ConfigurationForm => new FormConfig();

		// ──────────────────────────────────────────────────────────────────────
		// Start / Stop
		// ──────────────────────────────────────────────────────────────────────

		public override void Start()
		{
			if (PartyBotSettings.Instance.DoNothing)
				return;

			// Disable LeaderPlugin if it is running on this slave instance
			PluginContainer? leaderPlugin = PluginManager.Plugins.FirstOrDefault(p => p.Name == "LeaderPlugin");
			if (leaderPlugin != null && leaderPlugin.Enabled)
			{
				Logging.Write("PartyBot: Disabling leader plugin. Slaves should not have it running.");
				leaderPlugin.Enabled = false;
			}

			if (_remotingClient == null)
		{
			try
			{
				_remotingClient = new RemotingClient();
			}
			catch (Exception ex)
			{
				Logging.Write(System.Drawing.Color.Red,
					"[DiscoBot] Cannot connect to leader: {0}", ex.Message);
				Logging.Write(System.Drawing.Color.Orange,
					"[DiscoBot] Start the LeaderPlugin on the leader instance first, then click Start again.");
				return;
			}
		}

			if (!_hooked)
			{
				_remotingClient.ClientRecievedBotMessage += OnBotMessageReceived;
				AttachLuaEvents();
				LootTargeting.Instance.IncludeTargetsFilter += LevelBot.LevelbotIncludeLootsFilter;
				Targeting.Instance.IncludeTargetsFilter += new IncludeTargetsFilterDelegate(IncludeTargetsFilter);
				Targeting.Instance.WeighTargetsFilter += new WeighTargetsDelegate(WeighTargetsFilter);
				WoWChat.Party += OnPartyChat;
				WoWChat.PartyLeader += OnPartyChat;
				_hooked = true;
			}
		}

		public override void Stop()
		{
			if (PartyBotSettings.Instance.DoNothing)
				return;

			if (_remotingClient != null)
				_remotingClient.ClientRecievedBotMessage -= OnBotMessageReceived;
			DetachLuaEvents();
			LootTargeting.Instance.IncludeTargetsFilter -= LevelBot.LevelbotIncludeLootsFilter;
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

		private static void OnBotMessageReceived(BotMessage message)
		{
			_botMessage = message;
			RaFHelper.SetLeader(message.LeaderGuid);
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
			Lua.Events.AttachEvent("QUEST_DETAIL",         new LuaEventHandlerDelegate(OnQuestDetail));
		}

		private void DetachLuaEvents()
		{
			Lua.Events.DetachEvent("LFG_PROPOSAL_SHOW",   new LuaEventHandlerDelegate(OnLfgProposalShow));
			Lua.Events.DetachEvent("LFG_OFFER_CONTINUE",  new LuaEventHandlerDelegate(OnLfgOfferContinue));
			Lua.Events.DetachEvent("LFG_ROLE_CHECK_SHOW", new LuaEventHandlerDelegate(OnLfgRoleCheckShow));
			Lua.Events.DetachEvent("QUEST_DETAIL",         new LuaEventHandlerDelegate(OnQuestDetail));
		}

		// method_6 — PARTY_INVITE_REQUEST
		private void OnPartyInviteRequest(object sender, LuaEventArgs e)
		{
			if (_botMessage != null && e.Args.Length > 0)
			{
				string? name = e.Args[0] as string;
				_pendingGroupInvite = PartyBotSettings.Instance.AcceptGroupInvitesFromLeader && name == _botMessage.LeaderName;
			}
		}

		// method_7 — LFG_PROPOSAL_SHOW
		private void OnLfgProposalShow(object sender, LuaEventArgs e)
		{
			_pendingDungeonProposal = PartyBotSettings.Instance.AcceptDungeonInvites;
		}

		// method_8 — LFG_OFFER_CONTINUE
		private void OnLfgOfferContinue(object sender, LuaEventArgs e)
		{
			_pendingDungeonOfferContinue = PartyBotSettings.Instance.AcceptDungeonInvites;
		}

		// method_9 — LFG_ROLE_CHECK_SHOW
		private void OnLfgRoleCheckShow(object sender, LuaEventArgs e)
		{
			_pendingRoleCheck = PartyBotSettings.Instance.AcceptDungeonInvites;
		}

		// method_10 — QUEST_DETAIL
		private void OnQuestDetail(object sender, LuaEventArgs e)
		{
			_pendingQuestAccept = PartyBotSettings.Instance.AutoAcceptSharedQuests;
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
						Logging.Write("PartyBot: Dancing");
						Lua.DoString("DoEmote('Dance')");
						break;
					case "leavedungeon":
						Logging.Write("PartyBot: Leaving Dungeon");
						Lua.DoString("LFGTeleport(1)");
						break;
					case "enterdungeon":
						Logging.Write("PartyBot: Entering Dungeon");
						Lua.DoString("LFGTeleport(0)");
						break;
					case "clearpoi":
						BotPoi.Clear("Leader said so");
						break;
					case "leavebattleground":
						Logging.Write("PartyBot: Leaving Battleground");
						Battlegrounds.LeaveBattlefield();
						break;
					case "forcetrain":
						Logging.Write("PartyBot: Someone told me to train.");
						Vendors.ForceTrainer = true;
						break;
					case "forcesell":
						Logging.Write("PartyBot: Someone told me to go sell.");
						Vendors.ForceSell = true;
						break;
					case "forcerepair":
						Logging.Write("PartyBot: Someone told me to repair.");
						Vendors.ForceRepair = true;
						break;
					case "forcemail":
						Logging.Write("PartyBot: Someone told me to mail.");
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
									Logging.Write("PartyBot: Vendoring");
									if (_botMessage != null)
									{
										WoWUnit? vendor = ObjectManager.GetObjectByGuid<WoWUnit>(_botMessage.TargetGuid);
										if (vendor != null)
											BotPoi.Current = new BotPoi(vendor, vendor.IsRepairMerchant ? PoiType.Repair : PoiType.Sell);
									}
								})),
							new SwitchArgument<string>("FollowLeader",
								new Sequence(
									new TreeSharp.Action(ctx => Logging.Write("PartyBot: Following Leader")),
									new TreeSharp.Action(ctx => FollowLeader())
								)),
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
												new TreeSharp.Action(ctx => Logging.Write("PartyBot: Killing something"))
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
			WoWPlayer? leader = ObjectManager.GetObjectByGuid<WoWPlayer>(_botMessage.LeaderGuid);
			if (leader != null && leader.Distance <= PartyBotSettings.Instance.FollowDistance)
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
				if ((leader.IsFlying || leader.IsSwimming) && leader.InLineOfSight)
					WoWMovement.ClickToMove(LeaderLocation);

				if (leader.Distance <= 20.0 && (WoWMovement.ActiveInputControl.Flags & WoWMovement.MovementDirection.IsCTMing) == WoWMovement.MovementDirection.None)
				{
					StyxWoW.Me.SetFocus(leader);
					Lua.DoString("FollowUnit('focus')");
				}
				else if (leader.IsMoving && leader.Distance >= PartyBotSettings.Instance.FollowDistance + 3)
				{
					Navigator.MoveTo(LeaderLocation);
				}
				else if (leader.Distance >= PartyBotSettings.Instance.FollowDistance)
				{
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
											new TreeSharp.Action(ctx => Logging.Write("Setting target to pull")),
											new TreeSharp.Action(ctx => Targeting.Instance.FirstUnit!.Target())
										)
									),
									// Move to pull target if too far
									new Decorator(ctx => BotPoi.Current.AsObject != null && BotPoi.Current.AsObject.ToUnit() != null && BotPoi.Current.AsObject.ToUnit().Distance > Targeting.PullDistance,
										new Sequence(
											new TreeSharp.Action(ctx => Logging.Write("Moving to pull target")),
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
							// Dismount if needed
							new Decorator(ctx => StyxWoW.Me.Mounted,
								new TreeSharp.Action(ctx => Mount.Dismount("Combat"))),
							// Move to POI target if too far
							new Decorator(ctx => BotPoi.Current.AsObject != null && BotPoi.Current.AsObject.Distance > Targeting.PullDistance,
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
				new Decorator(ctx => PartyBotSettings.Instance.WaitForRessInDungeons
									&& StyxWoW.Me.IsInInstance
									&& StyxWoW.Me.Dead
									&& StyxWoW.Me.PartyMembers.Any(p => p.Class == WoWClass.Priest && p.IsAlive),
					new PrioritySelector(
						// If 3 minutes elapsed: release — smethod_73 fixed (was incorrectly !IsFinished in original)
						new Decorator(ctx => _waitTimer1.IsFinished,
							new Sequence(
								new TreeSharp.Action(ctx => Logging.Write("PartyBot: Waited 3 minutes and we got no ress. Releasing from corpse.")),
								new TreeSharp.Action(ctx => Lua.DoString("RepopMe()"))
							)
						),
						// Else: still waiting — log + keep timer running
						new Sequence(
							new TreeSharp.Action(ctx => Logging.Write("PartyBot: Waiting for ress.")),
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
				&& (!StyxWoW.Me.IsInInstance || PartyBotSettings.Instance.LootInDungeons);
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
						new TreeSharp.Action(ctx => Logging.Write("PartyBot: Accepting group invite")),
						new TreeSharp.Action(ctx => Lua.DoString("AcceptGroup()")),
						new TreeSharp.Action(ctx => _pendingGroupInvite = false),
						new WaitLuaEvent("PARTY_MEMBERS_CHANGED", 3,
							new TreeSharp.Action(ctx => Lua.DoString("StaticPopup_Hide(\"PARTY_INVITE\")")))
					)
				),
				// Dungeon proposal
				new Decorator(ctx => _pendingDungeonProposal,
					new Sequence(
						new TreeSharp.Action(ctx => Logging.Write("PartyBot: Accepting dungeon invite")),
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
						new TreeSharp.Action(ctx => Logging.Write("PartyBot: Role Check is in progress")),
						new TreeSharp.Action(ctx => Lua.DoString("LFDRoleCheckPopupAcceptButton:Click() StaticPopup1Button1:Click()")),
						new TreeSharp.Action(ctx => _pendingRoleCheck = false)
					)
				),
				// Quest accept
				new Decorator(ctx => _pendingQuestAccept,
					new Sequence(
						new TreeSharp.Action(ctx => Logging.Write("PartyBot: Accepting shared quest")),
						new TreeSharp.Action(ctx => Lua.DoString("AcceptQuest()")),
						new TreeSharp.Action(ctx => _pendingQuestAccept = false)
					)
				)
			);
		}

		// ──────────────────────────────────────────────────────────────────────
		// Fields
		// ──────────────────────────────────────────────────────────────────────

		private Composite? _root;
		private RemotingClient? _remotingClient;
		private bool _hooked;
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
	}
}
