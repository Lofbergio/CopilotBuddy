using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CopilotBuddy.Watchdog;

/// <summary>
/// External process supervisor for CopilotBuddy — deliberately dumb: it knows nothing
/// about the game, only processes and the heartbeat.json CB writes every 5s.
///
///  - WoW dead  → kill orphaned CB, relaunch WoW, relaunch CB with /pid= /autostart
///    (CB attaches at the login screen; its Relogger logs in; /autostart resumes the bot —
///    a manual double-click has no flag, so it never auto-starts).
///  - WoW frozen (window "Not Responding" for WowHungSeconds) → power-cycle the pair. The
///    process is alive and CB's heartbeat stays fresh, so no other check can see this.
///  - CB dead, heartbeat stale (hang), or ZOMBIE (alive + fresh heartbeat but no main window
///    for CbZombieSeconds — a wedged worker that ate its stop) → relaunch CB against the running WoW.
///  - Heartbeat state "GaveUp" (fatal login error / give-up window expired) → stand down;
///    power-cycling a credential error is exactly the churn this design forbids.
///  - Restart budget per target per rolling hour; 10 min of "InWorld" clears the books.
/// </summary>
internal static class Program
{
    private sealed record Config(
        string WowPath,
        string CbPath,
        int CheckIntervalSeconds = 10,
        int HeartbeatStaleSeconds = 60,
        int MaxRestartsPerHour = 3,
        int InWorldResetMinutes = 10,
        int WowHungSeconds = 60,    // sustained "Not Responding" before a power-cycle; 0 = off
        int CbZombieSeconds = 60);  // sustained windowless-but-alive CB before a restart; 0 = off

    private sealed record HeartbeatData(DateTime TimestampUtc, string State, bool BotRunning, int Pid, int WowPid, string GaveUpReason)
    {
        // Escalation flag from the Relogger: "I can't recover this glue state in-process — restart
        // the client." Missing in heartbeats from older CB builds → defaults false.
        public bool WantsClientRestart { get; init; }
    }

    private static readonly string[] WowProcessNames = { "Wow", "WoW", "Luacct" };
    private static readonly List<DateTime> _wowRestarts = new();
    private static readonly List<DateTime> _cbRestarts = new();
    private static DateTime _inWorldSinceUtc = DateTime.MinValue;
    private static DateTime _wowHungSinceUtc = DateTime.MinValue;
    private static DateTime _cbWindowlessSinceUtc = DateTime.MinValue;
    private static bool _gaveUpLogged;
    private static bool _budgetLogged;
    private static string _logPath = "";

    private static int Main(string[] args)
    {
        string baseDir = AppContext.BaseDirectory;
        _logPath = Path.Combine(baseDir, "watchdog.log");
        string configPath = args.Length > 0 ? args[0] : Path.Combine(baseDir, "watchdog.json");

        if (!File.Exists(configPath))
        {
            Log($"Config not found: {configPath}");
            Log("Expected JSON: { \"wowPath\": \"...\\\\Wow.exe\", \"cbPath\": \"...\\\\CopilotBuddy.exe\" }");
            return 1;
        }

        Config config;
        try
        {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        catch (Exception ex)
        {
            Log($"Config parse failed: {ex.Message}");
            return 1;
        }
        if (config == null || !File.Exists(config.WowPath) || !File.Exists(config.CbPath))
        {
            Log("Config invalid: wowPath/cbPath missing or files do not exist.");
            return 1;
        }

        string heartbeatPath = Path.Combine(Path.GetDirectoryName(config.CbPath)!, "heartbeat.json");
        Log($"Watchdog up. WoW='{config.WowPath}' CB='{config.CbPath}' heartbeat='{heartbeatPath}'");

        while (true)
        {
            try
            {
                Supervise(config, heartbeatPath);
            }
            catch (Exception ex)
            {
                Log($"Supervision pass failed: {ex.Message}");
            }
            Thread.Sleep(TimeSpan.FromSeconds(config.CheckIntervalSeconds));
        }
    }

    private static void Supervise(Config config, string heartbeatPath)
    {
        var heartbeat = TryReadHeartbeat(heartbeatPath);
        var wow = FindWow(heartbeat?.WowPid ?? 0);
        var cb = FindCb(heartbeat?.Pid ?? 0, config.CbPath);

        // Terminal state: CB alive and reporting a fatal condition. Do NOT power-cycle it.
        if (cb != null && heartbeat != null && IsFresh(heartbeat, config) && heartbeat.State == "GaveUp")
        {
            if (!_gaveUpLogged)
            {
                _gaveUpLogged = true;
                Log($"CB reports GaveUp ({heartbeat.GaveUpReason}) — standing down. Fix the cause and re-save Relogger settings.");
            }
            return;
        }
        _gaveUpLogged = false;

        // Sustained healthy play clears the restart books.
        if (heartbeat != null && IsFresh(heartbeat, config) && heartbeat.State == "InWorld")
        {
            if (_inWorldSinceUtc == DateTime.MinValue)
                _inWorldSinceUtc = DateTime.UtcNow;
            else if ((DateTime.UtcNow - _inWorldSinceUtc).TotalMinutes >= config.InWorldResetMinutes
                     && (_wowRestarts.Count > 0 || _cbRestarts.Count > 0))
            {
                _wowRestarts.Clear();
                _cbRestarts.Clear();
                _budgetLogged = false;
                Log($"InWorld for {config.InWorldResetMinutes}+ min — restart budgets reset.");
            }
        }
        else
        {
            _inWorldSinceUtc = DateTime.MinValue;
        }

        // Relogger escalation: CB is alive and honest about being beaten — the glue state is one it
        // cannot recover in-process (stale auth session, wedged dialog, half-up realm). A fresh client
        // resets all of it. Spend the WoW budget: this is a full power-cycle, same crash-loop cap.
        if (cb != null && heartbeat != null && IsFresh(heartbeat, config) && heartbeat.WantsClientRestart)
        {
            if (!TrySpendBudget(_wowRestarts, config.MaxRestartsPerHour, "WoW"))
                return;

            Log("CB requests a full client restart (relogger cannot recover the glue state) — killing CB + WoW, relaunching both.");
            KillIfAlive(cb, "CB (escalation)");
            KillIfAlive(wow, "WoW (escalation)");

            var freshWow = LaunchAndWaitForWow(config);
            if (freshWow == null)
            {
                Log("WoW did not come up within 120s — will retry next pass.");
                return;
            }

            LaunchCb(config, freshWow.Id);
            return;
        }

        if (wow == null)
        {
            if (!TrySpendBudget(_wowRestarts, config.MaxRestartsPerHour, "WoW"))
                return;

            Log("WoW is not running — full restart: kill CB, launch WoW, launch CB.");
            KillIfAlive(cb, "CB (orphaned)");

            var newWow = LaunchAndWaitForWow(config);
            if (newWow == null)
            {
                Log("WoW did not come up within 120s — will retry next pass.");
                return;
            }

            LaunchCb(config, newWow.Id);
            return;
        }

        // A FROZEN client is invisible to every other check: the process is alive and CB's heartbeat
        // stays fresh (its writer thread doesn't need the game's render thread), but EndScene never
        // fires again — the bot is dead in the water until a human notices (35-min hangs observed
        // 2026-07-04; the 3.3.5a client freezes even unattended, ~10 hang events in the WER history).
        // IsHungAppWindow is the Task-Manager "Not Responding" test (no message pump for >5s); require
        // it SUSTAINED so a long loading hitch can't false-positive, then power-cycle the PAIR on the
        // WoW budget — same doctrine as the relogger escalation: restart beats decoding the state.
        if (config.WowHungSeconds > 0 && wow.MainWindowHandle != IntPtr.Zero && IsHungAppWindow(wow.MainWindowHandle))
        {
            if (_wowHungSinceUtc == DateTime.MinValue)
            {
                _wowHungSinceUtc = DateTime.UtcNow;
                Log($"WoW window (pid {wow.Id}) is not responding — power-cycling in {config.WowHungSeconds}s unless it recovers.");
            }
            else if ((DateTime.UtcNow - _wowHungSinceUtc).TotalSeconds >= config.WowHungSeconds)
            {
                if (!TrySpendBudget(_wowRestarts, config.MaxRestartsPerHour, "WoW"))
                    return;
                _wowHungSinceUtc = DateTime.MinValue;
                Log($"WoW hung for {config.WowHungSeconds}+s — killing CB + WoW, relaunching both.");
                KillIfAlive(cb, "CB (client hung)");
                KillIfAlive(wow, "WoW (hung)");

                var revivedWow = LaunchAndWaitForWow(config);
                if (revivedWow == null)
                {
                    Log("WoW did not come up within 120s — will retry next pass.");
                    return;
                }
                LaunchCb(config, revivedWow.Id);
                return;
            }
        }
        else
        {
            _wowHungSinceUtc = DateTime.MinValue;
        }

        // A ZOMBIE CB: main window closed but the process survived (a wedged worker thread that also
        // swallowed its stop interrupt — observed 2026-07-04 10:58). Its heartbeat THREAD keeps writing
        // fresh beats, so staleness cannot see this; the process list cannot either. Windowless-but-
        // alive sustained past CbZombieSeconds = dead to the user AND holding the EndScene hook —
        // restart it. (CB has no window for a moment during startup; the sustain gate covers that.)
        bool cbZombie = false;
        if (config.CbZombieSeconds > 0 && cb != null && cb.MainWindowHandle == IntPtr.Zero)
        {
            if (_cbWindowlessSinceUtc == DateTime.MinValue)
                _cbWindowlessSinceUtc = DateTime.UtcNow;
            cbZombie = (DateTime.UtcNow - _cbWindowlessSinceUtc).TotalSeconds >= config.CbZombieSeconds;
        }
        else
        {
            _cbWindowlessSinceUtc = DateTime.MinValue;
        }

        bool cbDead = cb == null;
        bool cbHung = cb != null && (heartbeat == null || !IsFresh(heartbeat, config));

        // A fresh "Relogging" heartbeat means a login is in progress (server boot can take
        // minutes) — that is healthy, and cbHung is already false by the freshness check.
        if (cbDead || cbHung || cbZombie)
        {
            if (!TrySpendBudget(_cbRestarts, config.MaxRestartsPerHour, "CB"))
                return;
            _cbWindowlessSinceUtc = DateTime.MinValue;

            Log(cbDead ? "CB is not running — relaunching against the live WoW."
                : cbZombie ? $"CB is a zombie (alive, heartbeat fresh, no main window for {config.CbZombieSeconds}+s) — restarting it."
                : $"CB heartbeat stale (last: {heartbeat?.TimestampUtc:HH:mm:ss}Z) — presumed hung, restarting it.");
            KillIfAlive(cb, cbZombie ? "CB (zombie)" : "CB (hung)");
            LaunchCb(config, wow.Id);
        }
    }

    private static HeartbeatData? TryReadHeartbeat(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<HeartbeatData>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null; // partial write or malformed — treat as absent, next pass re-reads
        }
    }

    private static bool IsFresh(HeartbeatData hb, Config config)
        => (DateTime.UtcNow - hb.TimestampUtc).TotalSeconds <= config.HeartbeatStaleSeconds;

    private static Process? FindWow(int knownPid)
    {
        var byPid = FindAlive(knownPid);
        if (byPid != null)
            return byPid;
        return WowProcessNames.SelectMany(Process.GetProcessesByName).FirstOrDefault(p => !p.HasExited);
    }

    private static Process? FindCb(int knownPid, string cbPath)
    {
        var byPid = FindAlive(knownPid);
        if (byPid != null)
            return byPid;
        string name = Path.GetFileNameWithoutExtension(cbPath);
        return Process.GetProcessesByName(name).FirstOrDefault(p => !p.HasExited);
    }

    private static Process? FindAlive(int pid)
    {
        if (pid <= 0)
            return null;
        try
        {
            var p = Process.GetProcessById(pid);
            return p.HasExited ? null : p;
        }
        catch
        {
            return null;
        }
    }

    private static bool TrySpendBudget(List<DateTime> restarts, int maxPerHour, string target)
    {
        restarts.RemoveAll(t => (DateTime.UtcNow - t).TotalHours > 1);
        if (restarts.Count >= maxPerHour)
        {
            if (!_budgetLogged)
            {
                _budgetLogged = true;
                Log($"{target} restart budget exhausted ({maxPerHour}/hour) — standing down to avoid a crash loop.");
            }
            return false;
        }
        restarts.Add(DateTime.UtcNow);
        return true;
    }

    private static void KillIfAlive(Process? p, string label)
    {
        if (p == null)
            return;
        try
        {
            if (!p.HasExited)
            {
                Log($"Killing {label} (pid {p.Id}).");
                p.Kill(entireProcessTree: true);
                p.WaitForExit(10000);
            }
        }
        catch (Exception ex)
        {
            Log($"Kill {label} failed: {ex.Message}");
        }
    }

    private static Process? LaunchAndWaitForWow(Config config)
    {
        Process wow;
        try
        {
            wow = Process.Start(new ProcessStartInfo
            {
                FileName = config.WowPath,
                WorkingDirectory = Path.GetDirectoryName(config.WowPath)!,
                UseShellExecute = true,
            })!;
        }
        catch (Exception ex)
        {
            Log($"WoW launch failed: {ex.Message}");
            return null;
        }

        // Wait for the client window (login screen renders → D3D device exists → CB can hook EndScene).
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                wow.Refresh();
                if (wow.HasExited)
                    return null;
                if (wow.MainWindowHandle != IntPtr.Zero)
                {
                    // 5s, not 15: the window handle means the D3D device already exists — the client
                    // needs a beat to reach the login screen, not a coffee break (user-verified 2026-07-04).
                    Log($"WoW up (pid {wow.Id}) — settling 5s before attaching CB.");
                    Thread.Sleep(5000);
                    return wow;
                }
            }
            catch { /* transient process-state read failure */ }
            Thread.Sleep(2000);
        }
        return null;
    }

    private static void LaunchCb(Config config, int wowPid)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = config.CbPath,
                Arguments = $"/pid={wowPid} /autostart",
                WorkingDirectory = Path.GetDirectoryName(config.CbPath)!,
                UseShellExecute = true,
            });
            Log($"CB launched with /pid={wowPid} /autostart.");
        }
        catch (Exception ex)
        {
            Log($"CB launch failed: {ex.Message}");
        }
    }

    [DllImport("user32.dll")]
    private static extern bool IsHungAppWindow(IntPtr hWnd);

    private static void Log(string message)
    {
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        Console.WriteLine(line);
        try
        {
            File.AppendAllText(_logPath, line + Environment.NewLine);
        }
        catch { /* log file locked — console output still stands */ }
    }
}
