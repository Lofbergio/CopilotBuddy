#nullable disable
using System;
using System.Threading;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

namespace Styx.Helpers
{
    /// <summary>
    /// Detects player inactivity and can trigger automatic logout.
    /// </summary>
    public static class InactivityDetector
    {
        private static WaitTimer _inactivityTimer;
        private static WoWPoint _lastPosition;
        private static bool _initialized;

        /// <summary>
        /// Initializes the inactivity detector with current settings.
        /// </summary>
        public static void Initialize()
        {
            _inactivityTimer = new WaitTimer(TimeSpan.FromMinutes(StyxSettings.Instance.LogoutInactivityTimer));
            _lastPosition = StyxWoW.Me.Location;
            _inactivityTimer.Reset();
            _initialized = true;
        }

        /// <summary>
        /// Gets the time remaining until automatic logout.
        /// </summary>
        public static TimeSpan TimeUntilLogout => _inactivityTimer?.TimeLeft ?? TimeSpan.Zero;

        /// <summary>
        /// Gets the scheduled logout time.
        /// </summary>
        public static DateTime LogoutTime => _inactivityTimer?.EndTime ?? DateTime.MaxValue;

        /// <summary>
        /// Forces the player to logout.
        /// </summary>
        /// <param name="useForceQuit">If true, uses ForceQuit() instead of Logout().</param>
        public static void ForceLogout(bool useForceQuit)
        {
            if (!useForceQuit)
            {
                Lua.DoString("Logout()");
                Thread.Sleep(30000);
            }
            else
            {
                Lua.DoString("ForceQuit()");
                Thread.Sleep(10000);
            }
        }

        /// <summary>
        /// Called on each bot pulse to check for inactivity.
        /// </summary>
        public static void OnPulse()
        {
            if (!_initialized || _inactivityTimer == null)
                return;

            if (StyxSettings.Instance.LogoutForInactivity && TreeRoot.IsRunning)
            {
                // Reset timer if player has moved significantly
                if (_lastPosition.Distance(StyxWoW.Me.Location) > 15f)
                {
                    _inactivityTimer.Reset();
                    _lastPosition = StyxWoW.Me.Location;
                }
                else if (_inactivityTimer.IsFinished)
                {
                    Logging.Write("INACTIVITY TIMER TRIPPED! LOGGING OUT!");
                    ForceLogout(StyxSettings.Instance.LogoutInactivityUseForceQuit);
                }
            }
            else
            {
                _inactivityTimer.Reset();
            }
        }
    }
}
