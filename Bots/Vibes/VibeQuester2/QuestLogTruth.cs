using System;
using System.Collections.Generic;
using Styx.WoWInternals;

namespace Bots.Vibes.VibeQuester2
{
    /// <summary>
    /// The Lua quest log's isComplete flag is the ONLY trustworthy "ready to turn in" read —
    /// PlayerQuest.IsCompleted false-positives on IN-PROGRESS quests (quest 15, live 2026-07-12;
    /// docs/gotchas.md) and would plan turn-ins the server then refuses, striking innocent quests
    /// toward abandon. One GetQuestLogTitle sweep, cached ~1s. Only quests currently IN the log can
    /// appear here (server-completed history is the planner's separate GetCompletedQuests set).
    /// </summary>
    public static class QuestLogTruth
    {
        private static DateTime _at = DateTime.MinValue;
        private static readonly HashSet<uint> _complete = new HashSet<uint>();

        public static bool IsCompleteInLog(uint questId)
        {
            return CompleteSet().Contains(questId);
        }

        public static HashSet<uint> CompleteSet()
        {
            if ((DateTime.Now - _at).TotalMilliseconds < 1000) return _complete;
            _at = DateTime.Now;
            _complete.Clear();
            string res = Lua.GetReturnVal<string>(
                "local r = '' for i = 1, GetNumQuestLogEntries() do " +
                "local t, _, _, _, h, _, c, _, q = GetQuestLogTitle(i) " +
                "if not h and c == 1 and q then r = r .. q .. ';' end end return r", 0U) ?? "";
            foreach (string tok in res.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (uint.TryParse(tok, out uint id)) _complete.Add(id);
            return _complete;
        }
    }
}
