using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.Combat;
using Styx.Plugins;

namespace Styx.Logic.Relogging
{
    /// <summary>
    /// Dumps this CB install's capabilities — botbases, combat routines, plugins — to
    /// Settings\warband-manifest.json on every startup, so the Warband hub can populate
    /// its botbase picker without hard-coding names. Self-updating: install a botbase,
    /// run CB once, and it appears in the hub. Best-effort — never throws into startup.
    /// </summary>
    public static class WarbandManifest
    {
        public static void Dump()
        {
            try
            {
                string[] botbases = BotManager.Instance.Bots.Keys
                    .Where(n => !string.IsNullOrEmpty(n))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                IReadOnlyList<CombatRoutine> routineList = RoutineManager.Routines ?? new List<CombatRoutine>();
                string[] routines = routineList
                    .Select(r => r.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                List<PluginContainer> pluginList = PluginManager.Plugins ?? new List<PluginContainer>();
                string[] plugins = pluginList
                    .Select(p => p.Name)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Distinct()
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var sb = new StringBuilder();
                sb.Append('{');
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"botbases\":{0},", JsonArray(botbases));
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"routines\":{0},", JsonArray(routines));
                sb.AppendFormat(CultureInfo.InvariantCulture, "\"plugins\":{0}", JsonArray(plugins));
                sb.Append('}');

                string path = Path.Combine(Settings.SettingsDirectory, "warband-manifest.json");
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                // Atomic replace so a concurrent Warband read never sees a torn file.
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                File.Move(tmp, path, true);

                Logging.WriteDebug("[Warband] manifest written: {0} botbases, {1} routines, {2} plugins",
                    botbases.Length, routines.Length, plugins.Length);
            }
            catch (Exception ex)
            {
                Logging.WriteException(ex);
            }
        }

        private static string JsonArray(string[] items)
        {
            return "[" + string.Join(",", items.Select(Quote)) + "]";
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
