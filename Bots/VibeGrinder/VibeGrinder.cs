using System.Windows.Forms;
using Bots.Grind;
using Bots.VibeGrinder.Data;
using Bots.VibeGrinder.Selection;
using Bots.VibeGrinder.Supervision;
using Bots.VibeGrinder.Synthesis;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Combat;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using TreeSharp;
using Action = TreeSharp.Action;

namespace Bots.VibeGrinder
{
    /// <summary>
    /// VibeGrinder — a profile-free overnight grinder. On Start it picks the best safe grind spot
    /// near the character's live position/level/faction from GrindMobs.db, synthesizes a GrindArea +
    /// minimal Profile, and runs LevelBot's reused combat/loot/death/vendor trees plus a Supervisor
    /// that relocates when the spot goes bad. Every Start recomputes from live state — stop, play
    /// your toon, start again later, and it re-picks cleanly with no carried-over state.
    /// </summary>
    public class VibeGrinder : BotBase
    {
        private FactionResolver _factions;
        private SpotSelector _selector;
        private GrindAreaSynthesizer _synth;
        private GrindSupervisor _supervisor;
        private PrioritySelector _root;
        private BotEvents.Player.MobKilledDelegate _onKill;
        private bool _spotInstalled;
        private readonly System.Diagnostics.Stopwatch _navWait = new System.Diagnostics.Stopwatch();

        public override string Name => "VibeGrinder";

        public override bool IsPrimaryType => true;

        public override bool RequiresProfile => false;

        public override bool RequirementsMet => GrindMobsRepository.IsAvailable;

        public override PulseFlags PulseFlags => PulseFlags.All;

        public override Form ConfigurationForm => new VibeGrinderSettingsForm();

        public override Composite Root
        {
            get
            {
                if (_root == null)
                {
                    _root = new PrioritySelector(
                        // Defer spot selection to the first tick: the navmesh isn't loaded until
                        // RaiseBotStart fires (after BotBase.Start). Holds the tree until a spot
                        // is installed; once installed this gate falls through.
                        new Decorator(ctx => !_spotInstalled, new Action(ctx => EnsureSpotSelected())),
                        LevelBot.CreateDeathBehavior(),
                        LevelBot.CreateCombatBehavior(),
                        LevelBot.CreateLootBehavior(),
                        LevelBot.CreateVendorBehavior(),
                        _supervisor != null ? _supervisor.RelocationCheck() : new Action(ctx => RunStatus.Failure),
                        LevelBot.CreateRoamBehavior(),
                        new Action(ctx => RunStatus.Success) // idle
                    );
                }
                return _root;
            }
        }

        public override void Start()
        {
            // --- Start contract: clean slate. Spot selection is deferred to the first tick
            // (EnsureSpotSelected) because the navmesh isn't loaded yet during Start(). ---
            LevelBot.ResetState();
            BotPoi.Clear("VibeGrinder start");
            _root = null;
            _spotInstalled = false;
            _navWait.Reset();

            _factions = new FactionResolver();
            _synth = new GrindAreaSynthesizer();
            _selector = new SpotSelector(_factions);
            _supervisor = new GrindSupervisor(_selector, _synth, _factions);

            // Reuse LevelBot's target/loot filters (faction + blackspot + loot rules).
            Targeting.Instance.IncludeTargetsFilter += LevelBot.LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter += LevelBot.LevelbotIncludeLootsFilter;

            _onKill = args => _supervisor.RecordKill();
            BotEvents.Player.OnMobKilled += _onKill;

            Bots.DungeonBuddy.Avoidance.WorldObstacleManager.Initialize();
        }

        /// <summary>
        /// First-tick spot selection. Waits for the navmesh to load (it isn't ready during Start),
        /// then selects + installs a spot once. Stops cleanly if no spot is found or the mesh never
        /// loads. Returns Success so nothing else in the tree runs until a spot is installed.
        /// </summary>
        private RunStatus EnsureSpotSelected()
        {
            if (!Navigator.IsNavigatorLoaded)
            {
                if (!_navWait.IsRunning) _navWait.Restart();
                if (_navWait.Elapsed.TotalSeconds > 30)
                {
                    Logging.Write(System.Drawing.Color.Orange,
                        "[VibeGrinder] Navmesh did not load within 30s — cannot path. Stopping.");
                    TreeRoot.Stop();
                    return RunStatus.Success;
                }
                TreeRoot.StatusText = "VibeGrinder: waiting for navmesh to load...";
                return RunStatus.Success;
            }
            _navWait.Reset();

            var me = StyxWoW.Me;
            uint map = me.MapId;
            _factions.Build(map);

            GrindSpot spot = _selector.SelectBest(map, me.Location, me.Level, _supervisor.ActiveBlacklist());
            if (spot == null)
            {
                Logging.Write(System.Drawing.Color.Orange,
                    "[VibeGrinder] No grindable mobs within {0} yards of your level on this map. " +
                    "Move closer to appropriate mobs, raise the level band, or increase MaxTravelDistance.",
                    VibeGrinderSettings.Instance.MaxTravelDistance);
                TreeRoot.Stop();
                return RunStatus.Success;
            }

            _synth.Install(spot, me.Level);
            _supervisor.OnInstalled(spot);
            _spotInstalled = true;
            return RunStatus.Success;
        }

        public override void Stop()
        {
            Targeting.Instance.IncludeTargetsFilter -= LevelBot.LevelBotIncludeTargetsFilter;
            LootTargeting.Instance.IncludeTargetsFilter -= LevelBot.LevelbotIncludeLootsFilter;
            if (_onKill != null)
            {
                BotEvents.Player.OnMobKilled -= _onKill;
                _onKill = null;
            }
            Bots.DungeonBuddy.Avoidance.WorldObstacleManager.Shutdown();
        }

        public override void Pulse()
        {
            float speed = StyxWoW.Me.MovementInfo.CurrentSpeed;
            Navigator.PathPrecision = System.Math.Clamp(speed * 0.15f, 1.5f, 10f);
            _supervisor?.Pulse();
        }
    }
}
