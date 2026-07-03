using System.Diagnostics;
using System.Text.Json;

namespace CopilotBuddy.Watchdog;

/// <summary>
/// External process supervisor for CopilotBuddy — deliberately dumb: it knows nothing
/// about the game, only processes and the heartbeat.json CB writes every 5s.
///
///  - WoW dead  → kill orphaned CB, relaunch WoW, relaunch CB with /pid=
///    (CB attaches at the login screen; its Relogger does the actual login).
///  - CB dead, or heartbeat stale (hang) → relaunch CB against the running WoW.
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
        int InWorldResetMinutes = 10);

    private sealed record HeartbeatData(DateTime TimestampUtc, string State, bool BotRunning, int Pid, int WowPid, string GaveUpReason);

    private static readonly string[] WowProcessNames = { "Wow", "WoW", "Luacct" };
    private static readonly List<DateTime> _wowRestarts = new();
    private static readonly List<DateTime> _cbRestarts = new();
    private static DateTime _inWorldSinceUtc = DateTime.MinValue;
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

        bool cbDead = cb == null;
        bool cbHung = cb != null && (heartbeat == null || !IsFresh(heartbeat, config));

        // A fresh "Relogging" heartbeat means a login is in progress (server boot can take
        // minutes) — that is healthy, and cbHung is already false by the freshness check.
        if (cbDead || cbHung)
        {
            if (!TrySpendBudget(_cbRestarts, config.MaxRestartsPerHour, "CB"))
                return;

            Log(cbDead
                ? "CB is not running — relaunching against the live WoW."
                : $"CB heartbeat stale (last: {heartbeat?.TimestampUtc:HH:mm:ss}Z) — presumed hung, restarting it.");
            KillIfAlive(cb, "CB (hung)");
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
                    Log($"WoW up (pid {wow.Id}) — settling 15s before attaching CB.");
                    Thread.Sleep(15000);
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
                Arguments = $"/pid={wowPid}",
                WorkingDirectory = Path.GetDirectoryName(config.CbPath)!,
                UseShellExecute = true,
            });
            Log($"CB launched with /pid={wowPid}.");
        }
        catch (Exception ex)
        {
            Log($"CB launch failed: {ex.Message}");
        }
    }

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
