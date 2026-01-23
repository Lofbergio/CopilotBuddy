#nullable disable
using System;
using System.Runtime.CompilerServices;
using Styx.Logic;
using Styx.Logic.Combat;
using Styx.WoWInternals;

namespace Styx.Helpers
{
    public delegate void InfoPanelUpdatedDelegate();

    public static class InfoPanel
    {
        private static uint _startXP;
        private static uint _maxXP;
        private static uint _currentXP;
        private static uint _startHonor;
        private static DateTime _startTime;
        private static bool _isRunning;

        public static event InfoPanelUpdatedDelegate OnInfoPanelUpdated;

        static InfoPanel()
        {
            BotEvents.OnBotStart += OnBotStart;
            BotEvents.Player.OnMobKilled += OnMobKilled;
            BotEvents.Player.OnMobLooted += OnMobLooted;
            BotEvents.Player.OnLevelUp += OnLevelUp;
        }

        public static uint MobsKilled { get; private set; }
        public static uint Loots { get; private set; }
        public static uint Deaths { get; private set; }
        public static uint BGsWon { get; private set; }
        public static uint BGsLost { get; private set; }
        public static float XPPerHour { get; private set; }
        public static float MobsPerHour { get; private set; }
        public static float LootsPerHour { get; private set; }
        public static float DeathsPerHour { get; private set; }
        public static float BGsWonPerHour { get; private set; }
        public static float BGsLostPerHour { get; private set; }
        public static float HonorPerHour { get; private set; }

        public static TimeSpan SessionTime
        {
            get
            {
                if (!_isRunning)
                    return TimeSpan.Zero;
                return DateTime.Now - _startTime;
            }
        }

        internal static void Update()
        {
            TimeSpan elapsed = SessionTime;
            
            if (ObjectManager.Me != null)
            {
                XPPerHour = CalculatePerHour(ObjectManager.Me.XP + _startXP - _currentXP, elapsed);
                HonorPerHour = CalculatePerHour(ObjectManager.Me.Honor - _startHonor, elapsed);
            }
            
            MobsPerHour = CalculatePerHour(MobsKilled, elapsed);
            LootsPerHour = CalculatePerHour(Loots, elapsed);
            DeathsPerHour = CalculatePerHour(Deaths, elapsed);
            BGsWonPerHour = CalculatePerHour(BGsWon, elapsed);
            BGsLostPerHour = CalculatePerHour(BGsLost, elapsed);
            
            OnInfoPanelUpdated?.Invoke();
        }

        private static float CalculatePerHour(uint value, TimeSpan elapsed)
        {
            if (elapsed.TotalHours <= 0.0)
                return 0f;
            return value / (float)elapsed.TotalHours;
        }

        private static void OnBotStart(EventArgs args)
        {
            Reset();
            StartMeasuring();
        }

        private static void OnLevelUp(BotEvents.Player.LevelUpEventArgs e)
        {
            // Carry over XP from previous level
            _startXP += _maxXP - _currentXP;
            if (ObjectManager.Me != null)
            {
                _maxXP = ObjectManager.Me.NextLevelXP;
            }
            _currentXP = 0;
        }

        private static void OnMobKilled(BotEvents.Player.MobKilledEventArgs args)
        {
            MobsKilled++;
        }

        private static void OnMobLooted(BotEvents.Player.MobLootedEventArgs args)
        {
            Loots++;
        }

        public static void Reset()
        {
            MobsKilled = 0;
            Loots = 0;
            Deaths = 0;
            BGsWon = 0;
            BGsLost = 0;
            XPPerHour = 0f;
            MobsPerHour = 0f;
            LootsPerHour = 0f;
            DeathsPerHour = 0f;
            BGsWonPerHour = 0f;
            BGsLostPerHour = 0f;
            HonorPerHour = 0f;
            _startXP = 0;
            _currentXP = 0;
            _startHonor = 0;
            _isRunning = false;
        }

        public static void StartMeasuring()
        {
            _startTime = DateTime.Now;
            _isRunning = true;
            
            if (ObjectManager.Me != null)
            {
                _currentXP = ObjectManager.Me.XP;
                _maxXP = ObjectManager.Me.NextLevelXP;
                _startHonor = ObjectManager.Me.Honor;
            }
        }

        public static void StopMeasuring()
        {
            _isRunning = false;
        }

        public static void Died()
        {
            Deaths++;
        }

        public static void KilledMob()
        {
            MobsKilled++;
            LootTargeting.Instance.Pulse();
        }

        public static void LootedMob()
        {
            Loots++;
        }

        public static void BGWon()
        {
            BGsWon++;
        }

        public static void BGLost()
        {
            BGsLost++;
        }

        // Legacy methods for compatibility
        public static void IncrementDeaths()
        {
            Deaths++;
        }

        public static void IncrementBGsWon()
        {
            BGsWon++;
        }

        public static void IncrementBGsLost()
        {
            BGsLost++;
        }
    }
}
