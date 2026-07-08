using System;
using System.Collections.Generic;
using System.Linq;
using PartyBot.IPC;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Plugins.PluginClass;
using Styx.RemotableObjects;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace PartyBot
{
	/// <summary>
	/// Port of ns4.Class4 — the hardcoded LeaderPlugin from HB 4.3.4.
	/// Runs on the LEADER instance only. On each Pulse it sends a BotMessage
	/// (Kill / Vendor / FollowLeader) to all follower RemotingClients via the
	/// RemotingServer TCP listener on port 1337.
	/// Optionally auto-Greeds loot rolls (START_LOOT_ROLL) — off unless
	/// PartyBotSettings.LeaderAutoRollGreed is set (leave AutoEquip2 to roll otherwise).
	/// </summary>
	public class LeaderPlugin : HBPlugin
	{
		// ──────────────────────────────────────────────────────────────────────
		// HBPlugin overrides
		// ──────────────────────────────────────────────────────────────────────

		public override string Name => "LeaderPlugin";
		public override string Author => "Nesox";
		public override Version Version => new Version(1, 0, 0);

		public override void Pulse()
		{
			// Lazy-init server + hooks on first pulse
			if (!_initialized)
			{
				_server = new RemotingServer();
				Lua.Events.AttachEvent("START_LOOT_ROLL", new LuaEventHandlerDelegate(OnStartLootRoll));
				Targeting.Instance.IncludeTargetsFilter += IncludeTargetsFilter;
				_initialized = true;
			}

			if (StyxWoW.Me.Dead || StyxWoW.Me.IsGhost)
				return;

			WoWPoint location = StyxWoW.Me.Location;
			ulong leaderGuid = StyxWoW.Me.Guid;
			string leaderName = StyxWoW.Me.Name;
			PoiType poiType = BotPoi.Current.Type;

			BotMessage msg;

			if (Targeting.Instance.FirstUnit != null
				&& (poiType == PoiType.Kill || StyxWoW.Me.Combat)
				&& !Targeting.Instance.FirstUnit.Dead)
			{
				msg = new BotMessage
				{
					Message    = "Kill",
					LeaderX    = location.X,
					LeaderY    = location.Y,
					LeaderZ    = location.Z,
					TargetGuid = Targeting.Instance.FirstUnit.Guid,
					LeaderGuid = leaderGuid,
					Timestamp  = DateTime.Now,
					LeaderName = leaderName
				};
			}
			else if ((poiType == PoiType.Repair || poiType == PoiType.Sell)
				&& BotPoi.Current.AsObject != null
				&& BotPoi.Current.AsObject.WithinInteractRange)
			{
				msg = new BotMessage
				{
					Message    = "Vendor",
					LeaderX    = location.X,
					LeaderY    = location.Y,
					LeaderZ    = location.Z,
					TargetGuid = BotPoi.Current.AsObject.Guid,
					LeaderGuid = leaderGuid,
					Timestamp  = DateTime.Now,
					LeaderName = leaderName
				};
			}
			else
			{
				msg = new BotMessage
				{
					Message    = "FollowLeader",
					LeaderX    = location.X,
					LeaderY    = location.Y,
					LeaderZ    = location.Z,
					LeaderGuid = leaderGuid,
					Timestamp  = DateTime.Now,
					LeaderName = leaderName
				};
			}

			msg.LeaderTargetGuid = StyxWoW.Me.CurrentTargetGuid;
			msg.LeaderInCombat = StyxWoW.Me.Combat;
			_server!.SetMessage(msg);
			StyxWoW.ResetAfk();
		}

		// Disabling the plugin (e.g. a follower's botbase turning it off) must free the server, not
		// just leave it running — otherwise port 1337 is stranded and no other instance can lead.
		public override void OnDisable() => Shutdown();

		public override void Dispose() => Shutdown();

		private void Shutdown()
		{
			if (_initialized)
			{
				Lua.Events.DetachEvent("START_LOOT_ROLL", new LuaEventHandlerDelegate(OnStartLootRoll));
				Targeting.Instance.IncludeTargetsFilter -= IncludeTargetsFilter;
				_initialized = false;
			}
			_server?.Stop();
			_server = null;   // re-enable re-creates it cleanly (Pulse lazy-inits when _initialized is false)
		}

		// ──────────────────────────────────────────────────────────────────────
		// IncludeTargetsFilter — same as DiscoBot.smethod_2 / Class4.vmethod_0
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
				if (unit == null) continue;

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
						bool hasThreat = member.GetThreatInfoFor(unit).ThreatStatus >= ThreatStatus.NoobishTank;
						bool minionTargets = member.Minions.Any(m => m.CurrentTargetGuid == unit.Guid);
						if ((hasThreat || minionTargets) && !outgoing.Contains(unit))
						{
							outgoing.Add(unit);
							break;
						}
					}
				}
			}
		}

		// ──────────────────────────────────────────────────────────────────────
		// START_LOOT_ROLL — Class4.smethod_1
		// ──────────────────────────────────────────────────────────────────────

		private static void OnStartLootRoll(object sender, LuaEventArgs e)
		{
			// Off by default. AutoEquip2 (if present) hooks the SAME event and rolls Need on
			// upgrades / Greed otherwise — first roll wins, so blind-greeding here would defeat
			// it. Opt in only for a leader with no loot-rolling plugin.
			if (!PartyBotSettings.Instance.LeaderAutoRollGreed)
				return;

			Logging.Write("Rolling for loot!");
			if (e.Args.Length < 1)
			{
				Logging.WriteDebug("No arguments for START_LOOT_ROLL");
				return;
			}
			string link = Lua.GetReturnVal<string>("return GetLootRollItemLink(" + e.Args[0] + ")", 0U);
			if (!string.IsNullOrEmpty(link))
			{
				Lua.DoString("RollOnLoot(" + e.Args[0] + ", 2) ConfirmLootRoll(" + e.Args[0] + ", 2)");
			}
			else
			{
				Logging.WriteDebug("GetLootRollItemLink lua didn't work");
			}
		}

		// ──────────────────────────────────────────────────────────────────────
		// Fields
		// ──────────────────────────────────────────────────────────────────────

		private bool _initialized;
		private RemotingServer? _server;
	}
}
