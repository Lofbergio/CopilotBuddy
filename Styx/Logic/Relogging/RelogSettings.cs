using System;
using System.IO;
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
        public static readonly RelogSettings Instance = new RelogSettings();

        // Ties the blob to this app so another DPAPI consumer can't silently decrypt it.
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CopilotBuddy.Relogger");

        public RelogSettings()
            : base(Path.Combine(SettingsDirectory, "RelogSettings.xml"))
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
