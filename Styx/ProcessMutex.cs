using System;
using System.Threading;

namespace Styx
{
    /// <summary>
    /// Creates named mutexes to claim ownership of a WoW process.
    /// Prevents multiple CopilotBuddy instances from attaching to the same WoW.
    /// Deobfuscated from HB 4.3.4 ns18.Class21.
    /// </summary>
    internal static class ProcessMutex
    {
        /// <summary>
        /// Attempts to create and acquire a named mutex for the given WoW process ID.
        /// If <paramref name="createdNew"/> is true, this instance now owns the mutex
        /// (i.e. no other CopilotBuddy is attached to that PID).
        /// If false, another instance already claimed it.
        /// </summary>
        /// <param name="processId">The WoW process ID to claim.</param>
        /// <param name="createdNew">True if this call created (and owns) the mutex; false if already held.</param>
        /// <returns>The Mutex handle. Caller must Close/Dispose when done.</returns>
        public static Mutex Create(int processId, out bool createdNew)
        {
            // .NET Core randomizes string.GetHashCode PER PROCESS — the HB-era hash-mixed name (HB 6.2.3
            // XOR+salt pattern) computed a DIFFERENT value in every CB instance, so the guard never collided
            // and two CBs could claim the same WoW (2026-07-06 00:30: dual EndScene injection crashed the
            // client). Deterministic name; Local\ = per logon session, where competing CBs live.
            string name = "Local\\CopilotBuddy_WoW_" + processId;

            return new Mutex(true, name, out createdNew);
        }
    }
}
