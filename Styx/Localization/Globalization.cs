using System.Globalization;
using System.Resources;
using System.Threading;

namespace Styx.Localization
{
    public static class Globalization
    {
        private static ResourceManager _resourceManager;
        private static CultureInfo _culture;

        public static ResourceManager ResourceManager
        {
            get
            {
                if (_resourceManager == null)
                {
                    _resourceManager = new ResourceManager(
                        "CopilotBuddy.Styx.Localization.Strings",
                        typeof(Globalization).Assembly);
                }
                return _resourceManager;
            }
        }

        public static CultureInfo Culture
        {
            get { return _culture ?? Thread.CurrentThread.CurrentUICulture; }
            set
            {
                _culture = value;
                Thread.CurrentThread.CurrentUICulture = value;
            }
        }

        public static string Get(string key)
        {
            return ResourceManager.GetString(key, Culture) ?? key;
        }

        public static void ApplyLanguage(string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                Culture = CultureInfo.CurrentUICulture;
                return;
            }

            try
            {
                Culture = CultureInfo.GetCultureInfo(language);
            }
            catch (CultureNotFoundException)
            {
                Culture = CultureInfo.InvariantCulture;
            }
        }
    }
}