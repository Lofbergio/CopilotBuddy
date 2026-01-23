using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;

namespace Styx.Helpers
{
    /// <summary>
    /// Main bot settings for WoW 3.3.5a build 12340.
    /// Singleton pattern with Instance property.
    /// </summary>
    public class StyxSettings : Settings
    {
        /// <summary>
        /// Singleton instance of StyxSettings.
        /// </summary>
        public static readonly StyxSettings Instance;

        static StyxSettings()
        {
            Instance = new StyxSettings();
        }

        private static string GetAppPath()
        {
            string? location = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(location))
            {
                location = AppDomain.CurrentDomain.BaseDirectory;
            }
            return Path.GetDirectoryName(location) ?? ".";
        }

        public StyxSettings()
            : base(Path.Combine(GetAppPath(), "Settings\\StyxSettings.xml"))
        {
            // Sync LoggingLevel with Logging class after loading settings
            Logging.LoggingLevel = _loggingLevel;
        }

        private string _username = "test";
        private string _password = "t3st";
        private string _meshesFolderPath = "";
        private string[]? _enabledPlugins;
        private int _formLocationX = 20;
        private int _formLocationY = 20;
        private bool _useExperimentalPathFollowing = true;
        private bool _killBetweenHotspots = true;
        private bool _logoutForInactivity = true;
        private int _logoutInactivityTimer = 10;
        private bool _logoutInactivityUseForceQuit = false;
        private bool _profileDebuggingMode = false;

        /// <summary>
        /// Username for login.
        /// </summary>
        [DefaultValue("test")]
        [Setting]
        public string Username
        {
            get { return _username; }
            set { _username = value; }
        }

        /// <summary>
        /// Password for login.
        /// </summary>
        [DefaultValue("t3st")]
        [Setting]
        public string Password
        {
            get { return _password; }
            set { _password = value; }
        }

        /// <summary>
        /// Path to meshes folder.
        /// </summary>
        [DefaultValue("")]
        [Setting]
        public string MeshesFolderPath
        {
            get { return _meshesFolderPath; }
            set { _meshesFolderPath = value; }
        }

        /// <summary>
        /// List of enabled plugins.
        /// </summary>
        [Setting]
        public string[]? EnabledPlugins
        {
            get { return _enabledPlugins; }
            set { _enabledPlugins = value; }
        }

        /// <summary>
        /// Form X location.
        /// </summary>
        [DefaultValue(20)]
        [Setting]
        public int FormLocationX
        {
            get { return _formLocationX; }
            set { _formLocationX = value; }
        }

        /// <summary>
        /// Form Y location.
        /// </summary>
        [DefaultValue(20)]
        [Setting]
        public int FormLocationY
        {
            get { return _formLocationY; }
            set { _formLocationY = value; }
        }

        /// <summary>
        /// Use experimental path following.
        /// </summary>
        [Setting]
        [DefaultValue(true)]
        public bool UseExperimentalPathFollowing
        {
            get { return _useExperimentalPathFollowing; }
            set { _useExperimentalPathFollowing = value; }
        }

        /// <summary>
        /// Kill mobs between hotspots.
        /// </summary>
        [DefaultValue(true)]
        [Setting]
        public bool KillBetweenHotspots
        {
            get { return _killBetweenHotspots; }
            set { _killBetweenHotspots = value; }
        }

        /// <summary>
        /// Log out after detecting inactivity.
        /// </summary>
        [DefaultValue(true)]
        [Setting(Explanation = "Whether or not we should log out after the bot has detected inactivity.")]
        public bool LogoutForInactivity
        {
            get { return _logoutForInactivity; }
            set { _logoutForInactivity = value; }
        }

        /// <summary>
        /// Minutes of inactivity before logout.
        /// </summary>
        [Setting(Explanation = "Logs out after X minutes of inactivity.")]
        [DefaultValue(10)]
        public int LogoutInactivityTimer
        {
            get { return _logoutInactivityTimer; }
            set { _logoutInactivityTimer = value; }
        }

        /// <summary>
        /// Use force quit when logging out for inactivity.
        /// </summary>
        [DefaultValue(false)]
        [Setting]
        public bool LogoutInactivityUseForceQuit
        {
            get { return _logoutInactivityUseForceQuit; }
            set { _logoutInactivityUseForceQuit = value; }
        }

        /// <summary>
        /// Enable profile debugging mode for verbose logging.
        /// </summary>
        [DefaultValue(false)]
        [Setting]
        public bool ProfileDebuggingMode
        {
            get { return _profileDebuggingMode; }
            set { _profileDebuggingMode = value; }
        }

        private LogLevel _loggingLevel = LogLevel.Normal;

        /// <summary>
        /// Log level for UI display. Matches HB WoD: None, Quiet, Normal, Verbose, Diagnostic.
        /// </summary>
        [DefaultValue(LogLevel.Normal)]
        [Setting]
        public LogLevel LoggingLevel
        {
            get { return _loggingLevel; }
            set
            {
                _loggingLevel = value;
                // Sync with Logging class
                Logging.LoggingLevel = value;
            }
        }
    }
}
