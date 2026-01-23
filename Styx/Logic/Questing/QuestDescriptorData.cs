#nullable disable

using System.Runtime.InteropServices;

namespace Styx.Logic.Questing
{
    /// <summary>
    /// Quest data structure read from player unit descriptor fields.
    /// Contains quest identification, state flags, objective progress counters,
    /// and optional failure timer for timed quests.
    /// Size: 20 bytes (5 x uint32 equivalent)
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct QuestDescriptorData
    {
        /// <summary>
        /// The quest ID from quest database.
        /// </summary>
        public uint Id;

        /// <summary>
        /// Quest state flags (completed, failed, etc.).
        /// </summary>
        [MarshalAs(UnmanagedType.U4)]
        public WoWDescriptorQuestFlags Flags;

        /// <summary>
        /// Progress count for each of the 4 possible objectives.
        /// Array of 4 ushort values representing completion counts.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public ushort[] ObjectivesDone;

        /// <summary>
        /// For timed quests: seconds remaining before the quest fails.
        /// Zero for non-timed quests.
        /// </summary>
        public uint SecondsBeforeFailed;

        /// <summary>
        /// Gets whether this quest slot contains valid quest data.
        /// </summary>
        public bool IsValid => Id != 0;

        /// <summary>
        /// Gets whether the quest has been completed.
        /// </summary>
        public bool IsCompleted => (Flags & WoWDescriptorQuestFlags.Completed) != 0;

        /// <summary>
        /// Gets whether the quest has failed.
        /// </summary>
        public bool IsFailed => (Flags & WoWDescriptorQuestFlags.Failed) != 0;

        /// <summary>
        /// Gets the progress for a specific objective index (0-3).
        /// </summary>
        public ushort GetObjectiveProgress(int index)
        {
            if (ObjectivesDone == null || index < 0 || index >= ObjectivesDone.Length)
                return 0;
            return ObjectivesDone[index];
        }
    }
}
