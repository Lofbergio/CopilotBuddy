using System;
using System.Threading;
using System.Windows.Media;
using Styx.Helpers;
using Styx.Logic.BehaviorTree;
using Styx.WoWInternals;

namespace Styx.Logic.Relogging
{
    public enum RelogState
    {
        /// <summary>In world (or relogging disabled) — nothing to do.</summary>
        Idle,
        /// <summary>Out of world and actively driving the glue screens back to a character.</summary>
        Recovering,
        /// <summary>Terminal: a fatal error (bad credentials, missing character) or the give-up window expired.
        /// Never retried — the watchdog reads this from the heartbeat and stands down too.</summary>
        GaveUp,
    }

    /// <summary>
    /// Core relog service: watches for out-of-world state and drives the glue screens
    /// (login → realm → character select → world) via <see cref="GlueSession"/>.
    ///
    /// Two drivers, never concurrently (guarded by <see cref="_tickLock"/>):
    ///  - TreeRoot's InGame_Check calls <see cref="Tick"/> on the worker thread while the bot runs
    ///    (and RunTickBody suppresses its 5-min not-in-world stop while we recover);
    ///  - our own 2s timer drives it while the bot is NOT running (fresh /allowglue launch, manual stop).
    /// The timer also raises <see cref="WorldEntered"/> after 5s of stable in-world — MainWindow uses
    /// that to run deferred character init when CB attached at the glue screen.
    /// </summary>
    public static class Relogger
    {
        private static readonly object _tickLock = new object();
        private static System.Threading.Timer? _timer;

        private static RelogState _state = RelogState.Idle;
        private static string _gaveUpReason = "";

        // Recovery bookkeeping
        private static DateTime _recoveryStartedUtc;
        private static int _failedAttempts;
        private static DateTime _backoffUntilUtc = DateTime.MinValue;
        private static GlueScreen _lastScreen = GlueScreen.Unknown;
        private static DateTime _screenEnteredUtc;
        private static DateTime _loginSentUtc = DateTime.MinValue;
        private static readonly Random _jitter = new Random();

        // In-world stability watcher (for deferred character init)
        private static DateTime _inWorldSinceUtc = DateTime.MinValue;
        private static bool _worldEnteredRaised;

        private static readonly int[] BackoffLadderSeconds = { 15, 30, 60, 120, 300 };
        private const int WorldStableSeconds = 5;

        // Dialog text classification (English client — same caveat as UI_ERROR_MESSAGE matching).
        // Fatal: never retried. In-progress: the connecting/queue status dialog — wait, don't dismiss.
        private static readonly string[] FatalDialogFragments = { "is not valid", "banned", "suspended" };
        private static readonly string[] InProgressDialogFragments =
            { "queue", "connecting", "authenticating", "handshaking", "negotiating", "retrieving", "logging in" };

        // Per-screen max dwell before the attempt counts as failed (seconds).
        private static int MaxDwellSeconds(GlueScreen s) => s switch
        {
            GlueScreen.Login => 60,
            GlueScreen.RealmList => 30,
            GlueScreen.CharSelect => 45,
            _ => 120, // Unknown covers loading/"connecting" screens with no glue frame shown
        };

        public static RelogState State => _state;
        public static string GaveUpReason => _gaveUpReason;

        /// <summary>True while enabled and driving recovery — TreeRoot suppresses its not-in-world auto-stop on this.</summary>
        public static bool IsActivelyRecovering => _state == RelogState.Recovering;

        /// <summary>Raised (on the timer thread) after the player has been stably in world for a few seconds.</summary>
        public static event Action? WorldEntered;

        /// <summary>Phase A init — safe at the glue screen. Starts the service timer.</summary>
        public static void Initialize()
        {
            if (_timer != null)
                return;
            _timer = new System.Threading.Timer(_ => TimerTick(), null, 2000, 2000);
            if (RelogSettings.Instance.IsUsable)
                Logging.Write("[Relogger] Enabled (account '{0}', character '{1}').",
                    RelogSettings.Instance.AccountName,
                    RelogSettings.Instance.CharacterName.Length > 0 ? RelogSettings.Instance.CharacterName : "<current selection>");
        }

        /// <summary>Clears a GaveUp latch — called when the user re-saves relogger settings.</summary>
        public static void Reset()
        {
            _state = RelogState.Idle;
            _gaveUpReason = "";
            _failedAttempts = 0;
            _backoffUntilUtc = DateTime.MinValue;
        }

        private static void TimerTick()
        {
            try
            {
                WatchWorldStability();

                // While the bot runs, TreeRoot's InGame_Check is the driver — don't double-drive.
                if (TreeRoot.State == TreeRootState.Running)
                    return;

                Tick();
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[Relogger] Timer tick failed: {0}", ex.Message);
            }
        }

        private static void WatchWorldStability()
        {
            if (ObjectManager.Wow == null || _worldEnteredRaised)
                return;

            if (!StyxWoW.IsInWorld)
            {
                _inWorldSinceUtc = DateTime.MinValue;
                return;
            }

            // The bot pulsator (which normally drives ObjectManager.Update → populates Me) isn't
            // running yet, and Update() was a no-op at the glue attach. Pump it here so Me becomes
            // live after a fresh login — otherwise Phase B's LoadSettings early-returns on a null Me
            // and CharacterSettings.Instance stays null (plugin init + bot selector then NRE).
            try { ObjectManager.Update(); } catch { /* transient read during zone-in */ }

            // Gate on Me, not just the InGame byte: that flag flips true before the object manager
            // has enumerated the local player.
            if (ObjectManager.Me == null)
            {
                _inWorldSinceUtc = DateTime.MinValue;
                return;
            }

            if (_inWorldSinceUtc == DateTime.MinValue)
                _inWorldSinceUtc = DateTime.UtcNow;
            else if ((DateTime.UtcNow - _inWorldSinceUtc).TotalSeconds >= WorldStableSeconds)
            {
                _worldEnteredRaised = true;
                WorldEntered?.Invoke();
            }
        }

        /// <summary>
        /// One recovery step. Called by TreeRoot's InGame_Check (worker thread, bot running)
        /// or by the service timer (bot stopped). Cheap when idle; internally throttled while recovering.
        /// </summary>
        public static void Tick()
        {
            if (!Monitor.TryEnter(_tickLock))
                return;
            try
            {
                TickBody();
            }
            finally
            {
                Monitor.Exit(_tickLock);
            }
        }

        private static void TickBody()
        {
            if (_state == RelogState.GaveUp || ObjectManager.Wow == null)
                return;

            if (StyxWoW.IsInGame)
            {
                if (_state == RelogState.Recovering)
                {
                    Logging.Write(Colors.Lime, "[Relogger] Back in world after {0:F0}s ({1} failed attempt(s)).",
                        (DateTime.UtcNow - _recoveryStartedUtc).TotalSeconds, _failedAttempts);
                    _state = RelogState.Idle;
                    _failedAttempts = 0;
                    _backoffUntilUtc = DateTime.MinValue;
                }
                return;
            }

            if (!RelogSettings.Instance.IsUsable)
                return;

            if (_state == RelogState.Idle)
            {
                _state = RelogState.Recovering;
                _recoveryStartedUtc = DateTime.UtcNow;
                _lastScreen = GlueScreen.Unknown;
                _screenEnteredUtc = DateTime.UtcNow;
                _loginSentUtc = DateTime.MinValue;
                Logging.Write(Colors.Orange, "[Relogger] Not in world — relogger engaged.");
            }

            if (DateTime.UtcNow < _backoffUntilUtc)
                return;

            var snap = GlueSession.Query();

            // Screen transition = progress: reset the backoff ladder, restart the dwell clock.
            if (snap.Screen != _lastScreen)
            {
                if (_lastScreen != GlueScreen.Unknown || snap.Screen != GlueScreen.Unknown)
                    Logging.WriteDebug("[Relogger] Glue screen: {0} → {1}", _lastScreen, snap.Screen);
                _lastScreen = snap.Screen;
                _screenEnteredUtc = DateTime.UtcNow;
                _failedAttempts = 0;
                if (snap.Screen == GlueScreen.Login)
                    _loginSentUtc = DateTime.MinValue;
            }

            if (snap.DialogShown && HandleDialog(snap))
                return;

            switch (snap.Screen)
            {
                case GlueScreen.Login:
                    // Send credentials once per visit to the login screen; the dwell timeout re-fails if nothing moves.
                    if (_loginSentUtc == DateTime.MinValue)
                    {
                        Logging.Write("[Relogger] Logging in as '{0}'...", RelogSettings.Instance.AccountName);
                        GlueSession.Login(RelogSettings.Instance.AccountName, RelogSettings.Instance.Password);
                        _loginSentUtc = DateTime.UtcNow;
                    }
                    break;

                case GlueScreen.RealmList:
                    if (!GlueSession.SelectRealm(RelogSettings.Instance.RealmName))
                        FailAttempt(string.Format("realm '{0}' not found on realm list",
                            RelogSettings.Instance.RealmName.Length > 0 ? RelogSettings.Instance.RealmName : "<first>"));
                    break;

                case GlueScreen.CharSelect:
                    switch (GlueSession.EnterWorld(RelogSettings.Instance.CharacterName))
                    {
                        case GlueSession.EnterWorldResult.NotFound:
                            // Populated list without our name — a real config error, never a boot race.
                            GiveUp(string.Format("character '{0}' not found at character select", RelogSettings.Instance.CharacterName));
                            return;
                        case GlueSession.EnterWorldResult.ListEmpty:
                            // Worldserver still booting — char list not populated yet. Wait; the charselect
                            // dwell (45s) paces retries via the normal backoff, never a give-up.
                            break;
                        default:
                            Logging.Write("[Relogger] Entering world...");
                            break;
                    }
                    break;

                case GlueScreen.CharCreate:
                    // Shouldn't be here; back out to character select.
                    Lua.DoString("if CharacterCreate_Back then CharacterCreate_Back() end");
                    break;
            }

            CheckDwell(snap);
        }

        /// <summary>Returns true when the dialog fully handles this tick (wait or backoff).</summary>
        private static bool HandleDialog(GlueSnapshot snap)
        {
            string text = snap.DialogText.ToLowerInvariant();

            foreach (var fragment in FatalDialogFragments)
            {
                if (text.Contains(fragment))
                {
                    GiveUp(string.Format("fatal login error: \"{0}\"", snap.DialogText));
                    return true;
                }
            }

            foreach (var fragment in InProgressDialogFragments)
            {
                if (text.Contains(fragment))
                {
                    // Status/queue dialog — connection is progressing, keep the dwell clock honest.
                    _screenEnteredUtc = DateTime.UtcNow;
                    return true;
                }
            }

            // Unknown dialog — log the EXACT text (this is how the fragment lists grow), dismiss, back off.
            Logging.Write(Colors.Orange, "[Relogger] Login dialog: \"{0}\" (which={1}) — dismissing.", snap.DialogText, snap.DialogWhich);
            GlueSession.DismissDialog();
            FailAttempt(string.Format("dialog \"{0}\"", snap.DialogText));
            return true;
        }

        private static void CheckDwell(GlueSnapshot snap)
        {
            int dwell = MaxDwellSeconds(snap.Screen);
            if ((DateTime.UtcNow - _screenEnteredUtc).TotalSeconds > dwell)
            {
                _screenEnteredUtc = DateTime.UtcNow;
                if (snap.Screen == GlueScreen.Login)
                    _loginSentUtc = DateTime.MinValue; // allow a fresh DefaultServerLogin next attempt
                FailAttempt(string.Format("stuck at {0} for {1}s", snap.Screen, dwell));
            }
        }

        private static void FailAttempt(string reason)
        {
            // Give-up window: continuous failure since recovery started, not per-attempt.
            if ((DateTime.UtcNow - _recoveryStartedUtc).TotalMinutes >= RelogSettings.Instance.GiveUpAfterMinutes)
            {
                GiveUp(string.Format("no successful login for {0} minutes (last: {1})",
                    RelogSettings.Instance.GiveUpAfterMinutes, reason));
                return;
            }

            _failedAttempts++;
            int ladderIdx = Math.Min(_failedAttempts - 1, BackoffLadderSeconds.Length - 1);
            double seconds = BackoffLadderSeconds[ladderIdx] * (0.8 + _jitter.NextDouble() * 0.4);
            _backoffUntilUtc = DateTime.UtcNow.AddSeconds(seconds);
            Logging.Write(Colors.Orange, "[Relogger] Attempt {0} failed ({1}) — retrying in {2:F0}s.",
                _failedAttempts, reason, seconds);
        }

        /// <summary>Terminal stop — fatal error or give-up window expired. Loud, and never retried.</summary>
        public static void GiveUp(string reason)
        {
            if (_state == RelogState.GaveUp)
                return;
            _state = RelogState.GaveUp;
            _gaveUpReason = reason;
            Logging.Write(Colors.Red, "[Relogger] GIVING UP: {0}. Relogging is now dormant until settings are re-saved.", reason);
            // Do not stop the bot from here (may be on the worker thread mid-tick) — with
            // IsActivelyRecovering now false, TreeRoot's own not-in-world stop fires on its next tick.
        }
    }
}
