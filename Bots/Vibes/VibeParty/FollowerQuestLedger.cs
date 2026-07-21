using System;
using System.Collections.Generic;
using Styx;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Bots.Vibes.Shared.QuestData;

namespace VibeParty
{
	/// <summary>One concrete piece of quest business at an NPC entry.</summary>
	internal struct QuestWork
	{
		public uint QuestId;
		public string Title;
		public bool TurnIn;
	}

	/// <summary>One concrete piece of quest business at a game-object entry — the corpse/altar/barrel
	/// enders and givers a follower can't read a "?" off of, so they're resolved from the DB instead.</summary>
	internal struct GoWork
	{
		public uint QuestId;
		public string Title;
		public int GoEntry;
		public bool TurnIn;
	}

	/// <summary>
	/// The follower's quest KNOWLEDGE: QuestData.db (quest_givers / quest_enders / prereqs / gates)
	/// joined against the leader's whitelist and our own log. Replaces the old blind heuristics —
	/// a follower only ever travels to an NPC entry that holds a concrete (questId, action) for it,
	/// and every interaction is id-keyed. The leader-visit signals survive only as a fallback for
	/// whitelisted quests the DB has no NPC-giver row for (custom content, GO-started quests).
	/// </summary>
	internal static class FollowerQuestLedger
	{
		private static QuestDatabase? _db;
		private static Dictionary<int, QuestEntry>? _questById;
		private static Dictionary<int, List<QuestGiverEntry>>? _giversByQuest;
		// Giver ENTRY → the quests it offers, split by entity kind (creature and GO entry ids overlap).
		// Pure DB data, deliberately UNSCREENED: it exists only to map a title the frame presented back to
		// an id. The server already applied its own rules by offering it.
		private static Dictionary<int, List<QuestWork>>? _npcOffers;
		private static Dictionary<int, List<QuestWork>>? _goOffers;
		private static Dictionary<int, List<QuestEnderEntry>>? _endersByQuest;
		private static Dictionary<int, List<int>>? _questsByExclusiveGroup;   // positive groups only ("one of these, ever")
		private static bool _loadFailed;

		// Refresh outputs: entry → work items; rebuilt when the inputs change.
		private static readonly Dictionary<uint, List<QuestWork>> _businessByEntry = new Dictionary<uint, List<QuestWork>>();
		// Game-object business (turn-in of a held completable quest, pickup of a wanted one) — rebuilt with
		// _businessByEntry. Flat list (a handful at most); the caller resolves the live object per entry.
		private static readonly List<GoWork> _goBusiness = new List<GoWork>();
		private static readonly HashSet<uint> _wantMissingFromDb = new HashSet<uint>();
		private static readonly HashSet<uint> _loggedMissing = new HashSet<uint>();
		private static int _inputsFp;
		private static DateTime _refreshedAt = DateTime.MinValue;

		public static bool Loaded => _db != null;

		/// <summary>Any whitelisted-but-unmapped quest ids — the only case the old leader-visit
		/// replay signals still gate travel for.</summary>
		public static bool HasDbMissingWants => _wantMissingFromDb.Count > 0;

		/// <summary>Game-object turn-ins/pickups resolved this Refresh (see <see cref="GoWork"/>).</summary>
		public static IReadOnlyList<GoWork> GoBusiness => _goBusiness;

		// Repeatable ("blue") quests: turning one in re-offers it, so a follower auto-driving GO enders/givers
		// would loop. In THIS QuestData.db the repeatable bit is special_flags 0x40 — verified the ONLY quests
		// carrying it are the canonical "A Donation of <cloth>" turn-ins; the standard 3.3.5a daily bit is
		// flags 0x1000. No GO-linked quest is repeatable today, so this is a deliberate safety net (user, 2026-07-20).
		private const int SpecialFlagsRepeatable = 0x40;
		private const int QuestFlagsDaily = 0x1000;
		public static bool IsRepeatable(int questId)
			=> _questById != null && _questById.TryGetValue(questId, out QuestEntry? q)
			   && ((q.SpecialFlags & SpecialFlagsRepeatable) != 0 || (q.Flags & QuestFlagsDaily) != 0);

		/// <summary>A quest the LOG never flags isComplete on: no objectives, or only synthetic TurnInOnly
		/// rows — the "discover the corpse/site and hand in there" class (e.g. The Lost Pilot). Its turn-in
		/// must be attempted while merely HELD; the interaction with the ender completes it, so gating on
		/// CompletedQuestsLua (isComplete stays nil forever) deadlocks it. Explore/Collect/Kill objectives
		/// DO flip the log flag when done, so they stay on the log-complete path.</summary>
		public static bool NoLogTrackedCompletion(int questId)
		{
			if (_questById == null || !_questById.TryGetValue(questId, out QuestEntry? q)) return false;
			foreach (QuestObjective o in q.Objectives)
				if (o.Type != ObjectiveType.TurnInOnly) return false;
			return true;
		}

		public static void EnsureLoaded()
		{
			if (_db != null || _loadFailed) return;
			try
			{
				var loader = new DataLoader();
				_db = loader.Load();
			}
			catch (Exception ex)
			{
				_db = null;
				Logging.WriteException(ex);
			}
			if (_db == null)
			{
				// Without the DB the follower falls back to the old observable-leader-state model
				// wholesale — degraded but functional. Loud so a missing QuestData.db is a one-grep find.
				_loadFailed = true;
				Logging.Write("VibeParty: QuestData.db unavailable — follower quest targeting degraded to leader-visit replay only.");
				return;
			}
			_questById = new Dictionary<int, QuestEntry>();
			_questsByExclusiveGroup = new Dictionary<int, List<int>>();
			foreach (QuestEntry q in _db.Quests)
			{
				_questById[q.Id] = q;
				if (q.ExclusiveGroup <= 0) continue;   // negative = any-of prereq semantics, not a gate
				if (!_questsByExclusiveGroup.TryGetValue(q.ExclusiveGroup, out var grp))
					_questsByExclusiveGroup[q.ExclusiveGroup] = grp = new List<int>();
				grp.Add(q.Id);
			}
			_giversByQuest = new Dictionary<int, List<QuestGiverEntry>>();
			foreach (QuestGiverEntry g in _db.QuestGivers)
			{
				if (!_giversByQuest.TryGetValue(g.QuestId, out var list)) _giversByQuest[g.QuestId] = list = new List<QuestGiverEntry>();
				list.Add(g);
			}
			_endersByQuest = new Dictionary<int, List<QuestEnderEntry>>();
			foreach (QuestEnderEntry e in _db.QuestEnders)
			{
				if (!_endersByQuest.TryGetValue(e.QuestId, out var list)) _endersByQuest[e.QuestId] = list = new List<QuestEnderEntry>();
				list.Add(e);
			}
			_npcOffers = new Dictionary<int, List<QuestWork>>();
			_goOffers = new Dictionary<int, List<QuestWork>>();
			foreach (QuestGiverEntry g in _db.QuestGivers)
			{
				if (!_questById.TryGetValue(g.QuestId, out QuestEntry? gq)) continue;
				var into = g.GiverType == QuestObjectType.GameObject ? _goOffers : _npcOffers;
				if (!into.TryGetValue(g.GiverId, out var list)) into[g.GiverId] = list = new List<QuestWork>();
				list.Add(new QuestWork { QuestId = (uint)g.QuestId, Title = gq.Name, TurnIn = false });
			}
			Logging.Write("VibeParty: quest ledger loaded ({0} quests, {1} giver rows, {2} ender rows).",
				_db.Quests.Count, _db.QuestGivers.Count, _db.QuestEnders.Count);
		}

		/// <summary>
		/// Rebuild Want/Deliver → entry business. Inputs are what VibeParty already gathers per tick:
		/// the leader whitelist, the abandon-block gate, our log ids, our complete-in-log id→title set,
		/// and the finished-history set. Memoized on an input fingerprint, floor 1s.
		/// </summary>
		public static void Refresh(ICollection<uint> leaderQuests, Func<uint, bool> blocked,
			HashSet<uint> myLogIds, Dictionary<uint, string> completedInLog, HashSet<uint> doneHistory)
		{
			if (_db == null) return;

			int fp = 17;
			unchecked
			{
				foreach (uint id in leaderQuests) fp = fp * 31 + (int)id;
				fp = fp * 31 + myLogIds.Count * 397 + completedInLog.Count * 31 + doneHistory.Count;
				fp = fp * 31 + StyxWoW.Me.Level;
			}
			if (fp == _inputsFp && (DateTime.UtcNow - _refreshedAt).TotalSeconds < 1) return;
			_inputsFp = fp;
			_refreshedAt = DateTime.UtcNow;

			_businessByEntry.Clear();
			_goBusiness.Clear();
			_wantMissingFromDb.Clear();

			int myLevel = StyxWoW.Me.Level;
			int classMask = 1 << ((int)StyxWoW.Me.Class - 1);
			int raceMask = 1 << ((int)StyxWoW.Me.Race - 1);

			// Deliver: our complete log quests → NPC ender entries. A quest with no NPC ender row
			// needs no fallback — the own-"?" QuestGiverStatus trigger covers it without the DB.
			foreach (var kv in completedInLog)
			{
				if (_endersByQuest!.TryGetValue((int)kv.Key, out var enders))
					foreach (QuestEnderEntry e in enders)
					{
						if (e.EnderType != QuestObjectType.Creature) continue;
						Add((uint)e.EnderId, new QuestWork { QuestId = kv.Key, Title = kv.Value, TurnIn = true });
					}
			}

			// GO turn-ins: a HELD quest whose ender is a game object, completable now — either the log flags
			// it complete OR it's a discover quest the log never flags (the deadlock The Lost Pilot hit). A GO
			// carries no "?", so unlike the creature deliver above this can't lean on QuestGiverStatus. Skip
			// repeatables — re-offered on turn-in, they'd loop.
			foreach (uint id in myLogIds)
			{
				if (blocked(id) || IsRepeatable((int)id)) continue;
				if (!_endersByQuest!.TryGetValue((int)id, out var goEnders)) continue;
				bool complete = completedInLog.ContainsKey(id);
				if (!complete && !NoLogTrackedCompletion((int)id)) continue;
				string title = complete ? completedInLog[id]
					: (_questById!.TryGetValue((int)id, out QuestEntry? q) ? q.Name : id.ToString());
				foreach (QuestEnderEntry e in goEnders)
					if (e.EnderType == QuestObjectType.GameObject)
						_goBusiness.Add(new GoWork { QuestId = id, Title = title, GoEntry = e.EnderId, TurnIn = true });
			}

			// Want: whitelisted, not held, not done, not blocked, and takeable by THIS character —
			// class/race/level/prereq screened here, so we never travel to be refused.
			foreach (uint id in leaderQuests)
			{
				if (myLogIds.Contains(id) || doneHistory.Contains(id) || blocked(id)) continue;
				if (!_questById!.TryGetValue((int)id, out QuestEntry? q))
				{
					_wantMissingFromDb.Add(id);
					if (_loggedMissing.Add(id))
						Logging.Write("VibeParty: quest {0} not in QuestData.db — falling back to leader-visit replay for it.", id);
					continue;
				}
				if (q.AllowableClasses != 0 && (q.AllowableClasses & classMask) == 0) continue;
				if (q.AllowableRaces != 0 && (q.AllowableRaces & raceMask) == 0) continue;
				if (q.MinLevel > myLevel) continue;
				if (!PrereqsMet(q, doneHistory)) continue;
				if (!ExclusiveGroupOpen(q, myLogIds, doneHistory)) continue;

				bool anyNpcGiver = false, anyGoGiver = false;
				if (_giversByQuest!.TryGetValue((int)id, out var givers))
					foreach (QuestGiverEntry g in givers)
					{
						if (g.GiverType == QuestObjectType.Creature)
						{
							anyNpcGiver = true;
							Add((uint)g.GiverId, new QuestWork { QuestId = id, Title = q.Name, TurnIn = false });
						}
						else if (g.GiverType == QuestObjectType.GameObject)
						{
							// A GO GIVER is now a first-class pickup target (e.g. A Pilot's Revenge off the same
							// corpse). Repeatables excluded — a re-offered blue quest would loop.
							anyGoGiver = true;
							if (!IsRepeatable((int)id))
								_goBusiness.Add(new GoWork { QuestId = id, Title = q.Name, GoEntry = g.GiverId, TurnIn = false });
						}
					}
				if (!anyNpcGiver && !anyGoGiver && q.StartItem == 0)
				{
					// Unmapped: travel can't target it — shares/detail offers still catch it.
					_wantMissingFromDb.Add(id);
					if (_loggedMissing.Add(id))
						Logging.Write("VibeParty: quest {0} '{1}' has no NPC giver in QuestData.db — relying on share/offer paths.", id, q.Name);
				}
			}
		}

		private static void Add(uint entry, QuestWork work)
		{
			if (!_businessByEntry.TryGetValue(entry, out var list))
				_businessByEntry[entry] = list = new List<QuestWork>();
			list.Add(work);
		}

		/// <summary>
		/// Mirror of the server's SatisfyQuestExclusiveGroup. A POSITIVE `exclusive_group` means "only ONE
		/// quest of this group, ever" — the server refuses the rest the moment a sibling is started or
		/// rewarded (375 quests here: Goblin-vs-Gnome Engineering in group 3526, the level-bracketed
		/// Warsong repeatables in 8372). A NEGATIVE group is "any-of" PREREQUISITE semantics resolved
		/// through the prev-quest chain, never a takeability gate on its own — so it passes here.
		/// Screening this is what stops the follower walking to a giver the server will never serve.
		/// </summary>
		private static bool ExclusiveGroupOpen(QuestEntry q, HashSet<uint> myLogIds, HashSet<uint> done)
		{
			if (q.ExclusiveGroup <= 0) return true;
			if (_questsByExclusiveGroup == null
				|| !_questsByExclusiveGroup.TryGetValue(q.ExclusiveGroup, out var siblings)) return true;
			foreach (int sib in siblings)
			{
				if (sib == q.Id) continue;
				if (myLogIds.Contains((uint)sib) || done.Contains((uint)sib)) return false;
			}
			return true;
		}

		// AC semantics: positive prereq ids must be completed; negative ("active or complete") and
		// zero are treated as met — the leader holding the quest implies the chain state is sane.
		private static bool PrereqsMet(QuestEntry q, HashSet<uint> done)
		{
			foreach (int prev in q.PreviousQuestsIds)
				if (prev > 0 && !done.Contains((uint)prev))
					return false;
			return q.PrevQuestID <= 0 || done.Contains((uint)q.PrevQuestID);
		}

		public static bool EntryHasBusiness(uint entry) => _businessByEntry.ContainsKey(entry);

		/// <summary>Nearest spawn of a specific game-object entry ON the given map (gameobject_spawns, the
		/// same source VibeQuester uses), or false when none exists there. Distance-2D against
		/// <paramref name="from"/> so the caller can bound the trek before scanning the object manager.</summary>
		public static bool TryNearestGoSpawn(int goEntry, int mapId, WoWPoint from, out WoWPoint loc)
		{
			loc = WoWPoint.Empty;
			if (_db == null || !_db.GameObjectSpawns.TryGetValue(goEntry.ToString(), out var spawns)) return false;
			double bestDist = double.MaxValue; bool found = false;
			foreach (SpawnPoint sp in spawns)
			{
				if (sp.Map != mapId) continue;
				var p = new WoWPoint((float)sp.X, (float)sp.Y, (float)sp.Z);
				double d = from.Distance2D(p);
				if (d < bestDist) { bestDist = d; loc = p; found = true; }
			}
			return found;
		}

		/// <summary>Every quest the DB says this ENTRY offers — pure data, no screening. The frame decides
		/// what may actually be taken; this only maps a presented TITLE back to an id (3.3.5a's greeting API
		/// carries no ids). Callers apply OUR policy (leader whitelist, no repeatables) on top.</summary>
		public static List<QuestWork> OffersAt(uint entry, bool isGameObject)
		{
			var src = isGameObject ? _goOffers : _npcOffers;
			return src != null && src.TryGetValue((int)entry, out var list)
				? new List<QuestWork>(list)
				: new List<QuestWork>();
		}

		/// <summary>DB name for a quest id (logging / title fallback), or "" when unknown.</summary>
		public static string QuestName(int questId)
			=> _questById != null && _questById.TryGetValue(questId, out QuestEntry? q) ? q.Name : "";

		/// <summary>Work at this entry, turn-ins first (they unlock chained pickups server-side).</summary>
		public static List<QuestWork> BusinessAt(uint entry)
		{
			if (!_businessByEntry.TryGetValue(entry, out var list)) return new List<QuestWork>();
			var ordered = new List<QuestWork>(list);
			ordered.Sort((a, b) => b.TurnIn.CompareTo(a.TurnIn));
			return ordered;
		}
	}
}
