using Bots.VibeGrinder.Selection;
using Bots.Vibes.Shared;
using Styx;
using Styx.Helpers;
using Styx.Logic.AreaManagement;
using Styx.Logic.Profiles;

namespace Bots.VibeGrinder.Synthesis
{
    /// <summary>
    /// Installs a chosen GrindSpot into the engine so LevelBot's reused trees drive off it:
    /// one persistent GrindArea (refilled, never re-new'd — its ctor self-adds to AreaManager) plus
    /// a minimal synthetic Profile whose vendors come from data.bin via FindVendorsAutomatically.
    /// Construct this from Start (after game attach) so the GrindArea ctor sees a live AreaManager.
    /// </summary>
    public class GrindAreaSynthesizer
    {
        private readonly GrindArea _area = new GrindArea();
        private readonly MailboxService _mailboxes;
        private Profile _profile;
        private uint _installedMap = uint.MaxValue;

        public GrindAreaSynthesizer(MailboxService mailboxes)
        {
            _mailboxes = mailboxes;
        }

        public GrindArea Area => _area;

        public bool MapChanged(uint map) => map != _installedMap;

        /// <summary>Create + install the synthetic profile once (idempotent).</summary>
        public void EnsureProfile()
        {
            if (_profile != null)
                return;

            _profile = new Profile { Name = "VibeGrinder (synthetic)" };
            // Empty VendorManager + this flag => vendor tree auto-resolves sell/repair/food from data.bin.
            CharacterSettings.Instance.FindVendorsAutomatically = true;
            ProfileManager.UseSyntheticProfile(_profile);
        }

        /// <summary>Refill the persistent GrindArea with a spot's hotspots/factions/band and activate it.</summary>
        public void Install(GrindSpot spot, int playerLevel)
        {
            EnsureProfile();

            // Feed the map's mailboxes into the synthetic profile (reload only on map change) so the
            // reused vendor behaviour can mail to the bank. Off unless mailing is enabled.
            if (VibeGrinderSettings.Instance.EnableMailing && MapChanged(spot.Map))
                LoadMailboxesForMap(spot.Map);

            _area.Name = "VibeGrinder";

            _area.Hotspots.Clear();
            _area.CircledHotspots.Clear();
            foreach (var pt in spot.Hotspots)
            {
                var h = (Hotspot)pt;
                _area.Hotspots.Add(h);
                _area.CircledHotspots.Enqueue(h);
            }

            // Scope engagement to exactly the spot's own mob factions, so a stray neutral
            // (e.g. a passing humanoid) won't be proactively pulled.
            _area.Factions.Clear();
            foreach (int f in spot.Factions)
                _area.Factions.Add(f);

            _area.MobIDs.Clear();
            foreach (int id in spot.MobIds)
                _area.MobIDs.Add(id);

            var s = VibeGrinderSettings.Instance;
            _area.TargetMinLevel = playerLevel - s.LevelBandBelow;
            _area.TargetMaxLevel = playerLevel + s.LevelBandAbove;
            // Do NOT set MaxDistance: it doubles as Targeting.CollectionRange (the mounted kill
            // radius), which should stay at its 100yd default. Our travel cap (MaxTravelDistance)
            // is a selection-time concern, not an in-spot kill radius.

            StyxWoW.AreaManager.SetArea(_area);
            _area.CycleToNearest();   // begin the circuit at the nearest hotspot
            _installedMap = spot.Map;

            Logging.Write("[VibeGrinder] Installed spot: {0}", spot);
        }

        /// <summary>
        /// Feed the map's faction-safe mailboxes (shared MailboxService → Mailboxes.db) into the
        /// synthetic profile's ForcedMailboxes so LevelBot's reused mail behaviour can find one.
        /// Empty if Mailboxes.db is absent — mailing then just stays idle.
        /// </summary>
        private void LoadMailboxesForMap(uint map)
        {
            var mgr = ProfileManager.CurrentProfile?.MailboxManager;
            if (mgr == null) return;
            mgr.ForcedMailboxes = _mailboxes.LoadSafeMailboxes(map);
        }
    }
}
