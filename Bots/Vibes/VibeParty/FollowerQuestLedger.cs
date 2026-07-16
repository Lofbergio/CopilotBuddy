using System;
using System.Collections.Generic;
using Styx;
using Styx.Helpers;
using VibeQuester;

namespace VibeParty
{
	/// <summary>One concrete piece of quest business at an NPC entry.</summary>
	internal struct QuestWork
	{
		public uint QuestId;
		public string Title;
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
		private static Dictionary<int, List<QuestEnderEntry>>? _endersByQuest;
		private static bool _loadFailed;

		// Refresh outputs: entry → work items; rebuilt when the inputs change.
		private static readonly Dictionary<uint, List<QuestWork>> _businessByEntry = new Dictionary<uint, List<QuestWork>>();
		private static readonly HashSet<uint> _wantMissingFromDb = new HashSet<uint>();
		private static readonly HashSet<uint> _loggedMissing = new HashSet<uint>();
		private static int _inputsFp;
		private static DateTime _refreshedAt = DateTime.MinValue;

		public static bool Loaded => _db != null;

		/// <summary>Any whitelisted-but-unmapped quest ids — the only case the old leader-visit
		/// replay signals still gate travel for.</summary>
		public static bool HasDbMissingWants => _wantMissingFromDb.Count > 0;

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
			foreach (QuestEntry q in _db.Quests)
				_questById[q.Id] = q;
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

				bool anyNpcGiver = false;
				if (_giversByQuest!.TryGetValue((int)id, out var givers))
					foreach (QuestGiverEntry g in givers)
					{
						if (g.GiverType != QuestObjectType.Creature) continue;
						anyNpcGiver = true;
						Add((uint)g.GiverId, new QuestWork { QuestId = id, Title = q.Name, TurnIn = false });
					}
				if (!anyNpcGiver && q.StartItem == 0)
				{
					// GO-started or unmapped: travel can't target it — shares/detail offers still catch it.
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
