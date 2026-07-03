using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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

        public static string FilePath => Path.Combine(Logging.ApplicationPath, "heartbeat.json");

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
                    "{{\"timestampUtc\":\"{0:O}\",\"state\":\"{1}\",\"botRunning\":{2},\"pid\":{3},\"wowPid\":{4},\"gaveUpReason\":\"{5}\"}}",
                    DateTime.UtcNow,
                    state,
                    TreeRoot.State == TreeRootState.Running ? "true" : "false",
                    Process.GetCurrentProcess().Id,
                    wowPid,
                    Relogger.GaveUpReason.Replace("\\", "\\\\").Replace("\"", "\\\""));

                File.WriteAllText(FilePath, json);
            }
            catch
            {
                // Never let heartbeat IO take anything down; a stale file is itself the signal.
            }
        }
    }
}
