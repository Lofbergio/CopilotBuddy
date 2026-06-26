using System.Collections.Generic;
using Bots.VibeGrinder.Selection;
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
        private Profile _profile;
        private uint _installedMap = uint.MaxValue;

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
            _area.MaxDistance = s.MaxTravelDistance;

            StyxWoW.AreaManager.SetArea(_area);
            _installedMap = spot.Map;

            Logging.Write("[VibeGrinder] Installed spot: {0}", spot);
        }
    }
}
