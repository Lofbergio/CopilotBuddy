using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.WoWInternals;

namespace Styx.Logic.Relogging
{
    /// <summary>
    /// Writes heartbeat.json next to the exe every 5s so the external Watchdog can tell
    /// alive-and-working (fresh timestamp) from hung (stale) from terminal (state GaveUp).
    /// States: Attached | Relogging | InWorld | GaveUp. Started in attach Phase A.
    /// </summary>
    public static class Heartbeat
    {
        private static System.Threading.Timer? _timer;

        // Warband runs N CBs from one install → per-pid file so they don't stomp each other.
        // Only under /relog= (Warband mode); a standalone/Watchdog CB keeps the fixed name.
        private static bool WarbandLaunched => Environment.GetCommandLineArgs()
            .Any(a => a.StartsWith("/relog=", StringComparison.OrdinalIgnoreCase));

        public static string FilePath => Path.Combine(Logging.ApplicationPath,
            WarbandLaunched ? $"heartbeat.{Process.GetCurrentProcess().Id}.json" : "heartbeat.json");

        public static void Start()
        {
            if (_timer != null)
                return;
            _timer = new System.Threading.Timer(_ => Write(), null, 0, 5000);
        }

        private static void Write()
        {
            try
            {
                string state;
                if (Relogger.State == RelogState.GaveUp)
                    state = "GaveUp";
                else if (ObjectManager.Wow != null && StyxWoW.IsInWorld)
                    state = "InWorld";
                else if (Relogger.IsActivelyRecovering)
                    state = "Relogging";
                else
                    state = "Attached";

                int wowPid = 0;
                try { wowPid = ObjectManager.WoWProcess?.HasExited == false ? ObjectManager.WoWProcess.Id : 0; }
                catch { /* process gone mid-read */ }

                string json = string.Format(CultureInfo.InvariantCulture,
                    "{{\"timestampUtc\":\"{0:O}\",\"state\":\"{1}\",\"botRunning\":{2},\"pid\":{3},\"wowPid\":{4},\"wantsClientRestart\":{5},\"gaveUpReason\":\"{6}\"}}",
                    DateTime.UtcNow,
                    state,
                    TreeRoot.State == TreeRootState.Running ? "true" : "false",
                    Process.GetCurrentProcess().Id,
                    wowPid,
                    Relogger.WantsClientRestart ? "true" : "false",
                    Relogger.GaveUpReason.Replace("\\", "\\\\").Replace("\"", "\\\""));

                // Atomic replace: WriteAllText truncates-then-writes, and the Watchdog's concurrent read of
                // a half-written file parsed as null → "presumed hung" → it killed a healthy CB mid-grind
                // (2026-07-06 00:53, "heartbeat stale (last: Z)"). Readers now see the old file or the new
                // one, never a torn one. (A reader holding the file at the swap instant fails THIS write —
                // swallowed below, next beat in 5s; the Watchdog's 2-strike rule rides that out.)
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, true);
            }
            catch
            {
                // Never let heartbeat IO take anything down; a stale file is itself the signal.
            }
        }
    }
}
