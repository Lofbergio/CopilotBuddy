using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Styx.Helpers;

namespace Styx.Logic.Relogging
{
    /// <summary>
    /// Relogger configuration. Account-level (not per-character) — it must exist
    /// before a character does. Stored in Settings/RelogSettings.xml.
    /// The password is persisted as a per-user DPAPI blob, never plaintext.
    /// </summary>
    public class RelogSettings : Settings
    {
        private static readonly Lazy<RelogSettings> _instance = new Lazy<RelogSettings>(Create);
        public static RelogSettings Instance => _instance.Value;

        /// <summary>
        /// True when this CB was launched by Warband, i.e. with /relog=&lt;path&gt;.
        ///
        /// Warband runs N CBs from a single install and owns their window placement, so a
        /// managed CB must not restore or persist its window geometry: UISettings.xml is
        /// shared across the whole install, so every box would restore the same rect and
        /// the last box to close would save Warband's zone rect into the settings a
        /// standalone CB later reads.
        /// </summary>
        public static bool IsWarbandManaged { get; } = Environment.GetCommandLineArgs()
            .Any(a => a.StartsWith("/relog=", StringComparison.OrdinalIgnoreCase));

        // Warband launches each CB with /relog=<path> to pick that box's identity; else the default file.
        private static RelogSettings Create()
        {
            string relogArg = Environment.GetCommandLineArgs()
                .FirstOrDefault(a => a.StartsWith("/relog=", StringComparison.OrdinalIgnoreCase));
            string path = relogArg != null
                ? relogArg.Substring("/relog=".Length).Trim('"')
                : Path.Combine(SettingsDirectory, "RelogSettings.xml");
            return new RelogSettings(path);
        }

        // Ties the blob to this app so another DPAPI consumer can't silently decrypt it.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CopilotBuddy.Relogger");

        private RelogSettings(string path) : base(path)
        {
        }

        [Setting]
        [DefaultValue(false)]
        public bool Enabled { get; set; } = false;

        [Setting]
        [DefaultValue("")]
        public string AccountName { get; set; } = "";

        /// <summary>Base64 DPAPI blob. Use <see cref="Password"/> for the cleartext.</summary>
        [Setting]
        [DefaultValue("")]
        public string PasswordProtected { get; set; } = "";

        /// <summary>Realm to select if the client lands on the realm list. Empty = first realm found.</summary>
        [Setting]
        [DefaultValue("")]
        public string RealmName { get; set; } = "";

        /// <summary>Character to enter world with. Empty = whatever character select has selected.</summary>
        [Setting]
        [DefaultValue("")]
        public string CharacterName { get; set; } = "";

        /// <summary>Botbase to auto-start. Empty = the character's last-used botbase (SelectedBotIndex).</summary>
        [Setting]
        [DefaultValue("")]
        public string BotBase { get; set; } = "";

        /// <summary>Start the bot automatically once attached, in-world, and fully initialized.</summary>
        [Setting]
        [DefaultValue(false)]
        public bool AutoStartBot { get; set; } = false;

        /// <summary>Continuous-failure window: give up after this long without reaching the world.</summary>
        [Setting]
        [DefaultValue(120)]
        public int GiveUpAfterMinutes { get; set; } = 120;

        /// <summary>
        /// Escalation: after this long recovering without reaching the world, ask the Watchdog for a
        /// full client restart (fresh WoW = fresh auth session, no stuck dialogs/screens — the blunt
        /// answer to any glue state we can't classify). 0 = never. Requires the Watchdog running.
        /// </summary>
        [Setting]
        [DefaultValue(10)]
        public int ClientRestartAfterMinutes { get; set; } = 10;

        /// <summary>Cleartext password view over the DPAPI blob (current Windows user only).</summary>
        public string Password
        {
            get
            {
                if (string.IsNullOrEmpty(PasswordProtected))
                    return "";
                try
                {
                    byte[] blob = Convert.FromBase64String(PasswordProtected);
                    return Encoding.UTF8.GetString(ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser));
                }
                catch (Exception ex)
                {
                    Logging.Write("[Relogger] Could not decrypt the stored password (different Windows user?): {0}", ex.Message);
                    return "";
                }
            }
            set
            {
                PasswordProtected = string.IsNullOrEmpty(value)
                    ? ""
                    : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser));
            }
        }

        /// <summary>True when relogging is enabled and has usable credentials.</summary>
        public bool IsUsable => Enabled && AccountName.Length > 0 && PasswordProtected.Length > 0;
    }
}
