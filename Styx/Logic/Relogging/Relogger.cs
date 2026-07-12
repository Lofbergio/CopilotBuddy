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

        // World-entry attempt tracking. An EnterWorld that lands back at character select is a
        // SERVER REJECTION (classically "character already logged in" for ~1-2 min after an
        // unclean disconnect) — but the bounce goes CharSelect → Unknown(loading) → CharSelect,
        // and the screen-transition handler read each leg as "progress" and reset the backoff
        // ladder. Net effect: full-speed EnterWorld retries every ~6s forever, silently
        // (observed live 2026-07-10 11:47, warband-19440.log). These fields tie the attempt to
        // its outcome so a bounce fails the attempt and climbs the ladder like any other failure.
        private static DateTime _enterWorldSentUtc = DateTime.MinValue;
        private static int _worldEntryBounces;
        private static string _worldEntryDialog = "";

        // Diagnostic: the last observed glue+world summary and when we last logged it, so we
        // report exactly what CB sees on every change (and periodically while stuck) instead
        // of leaving a 120s "Unknown" span as an opaque black box.
        private static string _lastSeen = "";
        private static DateTime _lastSeenLogUtc = DateTime.MinValue;

        // Watch on the which=CANCEL (connection-progress) dialog: waiting on it is right, waiting FOREVER
        // is not — a half-up server left "Connecting…" on screen for 3.5 HOURS while the wait refreshed the
        // dwell clock every tick, so neither the dwell nor the give-up window (both FailAttempt-driven)
        // could ever fire (log 2026-07-04_0013, 03:32→07:06). Queue dialogs are exempt (legit long waits).
        private const int CancelDialogTimeoutSeconds = 90;
        private static DateTime _cancelDialogSinceUtc = DateTime.MinValue;
        private static string _cancelDialogText = "";

        // In-world stability watcher (for deferred character init)
        private static DateTime _inWorldSinceUtc = DateTime.MinValue;
        private static bool _worldEnteredRaised;

        // First rung 5s (was 15): the client needs ~2s from dialog-dismissed to login-sent — a fast first
        // retry costs nothing and a real outage climbs the ladder anyway (verified live 2026-07-04).
        private static readonly int[] BackoffLadderSeconds = { 5, 15, 30, 60, 120, 300 };
        private const int WorldStableSeconds = 5;

        /// <summary>
        /// Short settle after reaching character select (or after a SelectCharacter) before we press
        /// Enter, so the list has populated and the target is the current selection. Kept small — we
        /// enter via a keypress (the human path), which needs only a beat, not the long wait the old
        /// injected EnterWorld() needed to outlast the char-list enumeration.
        /// </summary>
        private const int CharSelectSettleMs = 300;

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

        /// <summary>
        /// Escalation flag, published via heartbeat.json: recovery has run ClientRestartAfterMinutes
        /// without reaching the world — whatever the glue state is, we can't fix it from in-process
        /// (fresh client = fresh auth session, no stuck dialog/screen). The WATCHDOG does the deed
        /// (CB cannot re-attach to a new WoW process); we keep trying meanwhile. Cleared on success.
        /// </summary>
        public static bool WantsClientRestart { get; private set; }

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
            WantsClientRestart = false;
        }

        private static bool _lastInWorldLogged;

        /// <summary>
        /// Logs the in-world edge on every 2s timer tick, independent of the recovery driver —
        /// so the "character enters the world then logs straight back out" symptom is captured
        /// as an explicit OUT-OF-WORLD edge (with a note when it follows an EnterWorld), rather
        /// than only showing up as an unexplained bounce back to a glue screen.
        /// </summary>
        private static void LogWorldEdges()
        {
            if (ObjectManager.Wow == null)
                return;
            bool inWorld = StyxWoW.IsInWorld;
            if (inWorld == _lastInWorldLogged)
                return;
            _lastInWorldLogged = inWorld;
            if (inWorld)
                Logging.Write(Colors.Lime, "[Relogger] World state → IN WORLD (inGame={0}).", StyxWoW.IsInGame);
            else
                Logging.Write(Colors.Orange, "[Relogger] World state → OUT OF WORLD{0}.",
                    _enterWorldSentUtc != DateTime.MinValue
                        ? " — dropped shortly after an EnterWorld (the 'enter then logout' symptom)"
                        : "");
        }

        private static void TimerTick()
        {
            try
            {
                LogWorldEdges();
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
                    _enterWorldSentUtc = DateTime.MinValue;
                    _worldEntryBounces = 0;
                    _worldEntryDialog = "";
                    WantsClientRestart = false;
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
                _cancelDialogSinceUtc = DateTime.MinValue;
                _enterWorldSentUtc = DateTime.MinValue;
                _worldEntryBounces = 0;
                _worldEntryDialog = "";
                _lastSeen = "";
                WantsClientRestart = false;
                Logging.Write(Colors.Orange, "[Relogger] Not in world — relogger engaged.");
            }

            // ABSOLUTE give-up window — evaluated every tick, not only in FailAttempt: a stuck
            // in-progress state starved FailAttempt (and thus the window) for 3.5h straight
            // (log 2026-07-04_0013). A queue longer than the window is knowingly sacrificed —
            // raise GiveUpAfterMinutes on queue-heavy servers.
            if ((DateTime.UtcNow - _recoveryStartedUtc).TotalMinutes >= RelogSettings.Instance.GiveUpAfterMinutes)
            {
                GiveUp(string.Format("no successful login for {0} minutes", RelogSettings.Instance.GiveUpAfterMinutes));
                return;
            }

            // ESCALATION: past this point we stop pretending to understand the glue state — whatever
            // it is (stale session, wedged dialog, half-up realm), a FRESH CLIENT fixes it. Flag the
            // watchdog (heartbeat) for a full WoW+CB restart and keep trying meanwhile. Don't try to
            // enumerate broken states — every 3am produced a novel one; time-without-world is the
            // only classifier that covers them all.
            int restartAfter = RelogSettings.Instance.ClientRestartAfterMinutes;
            if (restartAfter > 0 && !WantsClientRestart
                && (DateTime.UtcNow - _recoveryStartedUtc).TotalMinutes >= restartAfter)
            {
                WantsClientRestart = true;
                Logging.Write(Colors.Orange,
                    "[Relogger] No world for {0} min — requesting a full client restart from the Watchdog (glue attempts continue meanwhile).",
                    restartAfter);
            }

            if (DateTime.UtcNow < _backoffUntilUtc)
                return;

            var snap = GlueSession.Query();

            // Ground truth: report exactly what CB observes — the real screen, any dialog text
            // (this is where "Character already logged in" / "Disconnected" would surface), the
            // selected character, and the in-game/in-world flags — on every change and at least
            // every 20s while nothing changes. This is what turns an opaque "stuck at Unknown"
            // into a readable trace.
            string seen = string.Format("{0} | inGame={1} inWorld={2}",
                snap.Describe(), StyxWoW.IsInGame, StyxWoW.IsInWorld);
            if (seen != _lastSeen || (DateTime.UtcNow - _lastSeenLogUtc).TotalSeconds >= 20)
            {
                Logging.Write("[Relogger] Sees: {0}", seen);
                _lastSeen = seen;
                _lastSeenLogUtc = DateTime.UtcNow;
            }

            // A dialog seen while a world-entry attempt is in flight is very likely the server's
            // rejection reason — keep its text so the eventual bounce report can quote it.
            // CANCEL dialogs are the connection-progress box, not a reason.
            if (snap.DialogShown && _enterWorldSentUtc != DateTime.MinValue && snap.DialogWhich != "CANCEL")
                _worldEntryDialog = snap.DialogText;

            // Screen transition handling. A transition is only "progress" (ladder reset) when no
            // world-entry attempt is in flight: the rejection bounce goes CharSelect → Unknown
            // (loading) → CharSelect, and treating each leg as progress is what kept the ladder
            // at zero and produced silent full-speed retries, forever.
            if (snap.Screen != _lastScreen)
            {
                bool bounce = snap.Screen == GlueScreen.CharSelect && _enterWorldSentUtc != DateTime.MinValue;
                if (_lastScreen != GlueScreen.Unknown || snap.Screen != GlueScreen.Unknown)
                    Logging.WriteDebug("[Relogger] Glue screen: {0} → {1}", _lastScreen, snap.Screen);
                _lastScreen = snap.Screen;
                _screenEnteredUtc = DateTime.UtcNow;
                if (snap.Screen == GlueScreen.Login)
                {
                    // A fall all the way back to login is a new session — world-entry bookkeeping resets.
                    _loginSentUtc = DateTime.MinValue;
                    _enterWorldSentUtc = DateTime.MinValue;
                    _worldEntryBounces = 0;
                    _worldEntryDialog = "";
                }
                if (bounce)
                {
                    // The server refused the world entry and threw us back to character select.
                    _enterWorldSentUtc = DateTime.MinValue;
                    _worldEntryBounces++;
                    string said = _worldEntryDialog.Length > 0
                        ? string.Format(" — server said: \"{0}\"", _worldEntryDialog)
                        : "";
                    _worldEntryDialog = "";
                    if (_worldEntryBounces == 3)
                        Logging.Write(Colors.Orange,
                            "[Relogger] The server keeps refusing world entry — commonly \"character already logged in\" for a minute or two after an unclean disconnect. Backing off between attempts.");
                    FailAttempt(string.Format("world entry rejected, bounced back to character select ({0} in a row){1}",
                        _worldEntryBounces, said));
                    return; // backoff engaged; retry when it expires
                }
                if (_enterWorldSentUtc == DateTime.MinValue)
                    _failedAttempts = 0; // genuine progress only
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
                    // Let the character list settle first. Bouncing back off a rejected EnterWorld also
                    // resets _screenEnteredUtc, so every retry gets the same grace.
                    if ((DateTime.UtcNow - _screenEnteredUtc).TotalMilliseconds < CharSelectSettleMs)
                        break;

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
                        case GlueSession.EnterWorldResult.Selecting:
                            // Selection is a server round-trip. Enter world on the NEXT tick, once it has
                            // settled — sending both together makes the server bounce us back here forever.
                            Logging.Write("[Relogger] Selecting character '{0}'...", RelogSettings.Instance.CharacterName);
                            break;
                        default:
                            // Arm the attempt tracker: if this lands us back at character select
                            // instead of the world, the transition handler fails the attempt.
                            _enterWorldSentUtc = DateTime.UtcNow;
                            Logging.Write("[Relogger] Entering world{0}...",
                                _worldEntryBounces > 0 ? string.Format(" (attempt {0})", _worldEntryBounces + 1) : "");
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

            // Connection-progress dialog: its only button IS Cancel (which=CANCEL — "Connecting…",
            // "Authenticating…", "Success!"). Dismissing it CLICKS CANCEL and aborts a login that was
            // succeeding (2026-07-03 23:50: "Success!" dismissed → "Session Expired" → 8 min of retries).
            // So wait — but BOUNDED: a half-up server can leave it on screen forever (the 3.5h hang,
            // 2026-07-04 03:32). Past the timeout, cancelling is correct — the handshake is dead anyway.
            // Queue dialogs wait indefinitely (a login queue is real progress).
            if (snap.DialogWhich == "CANCEL")
            {
                _screenEnteredUtc = DateTime.UtcNow;
                if (text.Contains("queue"))
                {
                    _cancelDialogSinceUtc = DateTime.MinValue;
                    return true;
                }
                if (_cancelDialogSinceUtc == DateTime.MinValue || snap.DialogText != _cancelDialogText)
                {
                    _cancelDialogSinceUtc = DateTime.UtcNow;   // new dialog (or text changed) — fresh window
                    _cancelDialogText = snap.DialogText;
                }
                else if ((DateTime.UtcNow - _cancelDialogSinceUtc).TotalSeconds > CancelDialogTimeoutSeconds)
                {
                    Logging.Write(Colors.Orange, "[Relogger] Connection dialog \"{0}\" stuck for {1}s — cancelling the dead handshake.",
                        snap.DialogText, CancelDialogTimeoutSeconds);
                    GlueSession.DismissDialog();
                    _cancelDialogSinceUtc = DateTime.MinValue;
                    _loginSentUtc = DateTime.MinValue;
                    FailAttempt(string.Format("connection dialog \"{0}\" timed out", snap.DialogText));
                }
                return true;
            }
            _cancelDialogSinceUtc = DateTime.MinValue;   // any non-CANCEL dialog ends the watch

            foreach (var fragment in InProgressDialogFragments)
            {
                if (text.Contains(fragment))
                {
                    // Status/queue dialog — connection is progressing, keep the dwell clock honest.
                    _screenEnteredUtc = DateTime.UtcNow;
                    return true;
                }
            }

            // Post-DC informational dialog ("You have been disconnected...", which=DISCONNECTED): dismiss
            // for FREE — counting it as a failed attempt made the very FIRST login sit out a full ladder
            // step (observed 18s) before typing credentials, for a dialog that isn't a failure of OURS.
            // LOGGED, and it only re-arms the login when none is in flight: the dialog can pop a tick
            // AFTER we already sent credentials, and the silent reset+resend interrupted that handshake
            // (the 03:04:59/03:05:01 double-send → "Session Expired" churn, log 2026-07-04_0013).
            if (snap.DialogWhich == "DISCONNECTED" || text.Contains("disconnected"))
            {
                Logging.Write("[Relogger] Dismissing disconnect notice (free — no backoff).");
                GlueSession.DismissDialog();
                _screenEnteredUtc = DateTime.UtcNow;
                if ((DateTime.UtcNow - _loginSentUtc).TotalSeconds > 10)
                    _loginSentUtc = DateTime.MinValue;   // no attempt in flight — credentials next tick
                return true;
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
