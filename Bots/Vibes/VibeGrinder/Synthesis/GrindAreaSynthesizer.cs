using System;
using System.Globalization;
using System.Xml.Linq;
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

            // Operational thresholds live in the bot settings (VibeGrinderSettings), not the profile.
            // NeedToSell/NeedToRepair read them off the profile though, so project them onto the synthetic
            // profile through the normal profile-config (XML) path — no core Profile setters. Without this the
            // profile keeps its defaults (MinFreeBagSlots 0 = sell only at 100%-full bags → never funds training).
            // SellWhiteJunk (opt-in, default OFF) lets the vendor sweep clear low-value white junk (cooking
            // meat). It's gated behind the setting because selling whites is only safe with the VendorGuard
            // plugin enabled — off by default means greys-only selling, which never touches your gear/cloth.
            var vg = VibeGrinderSettings.Instance;
            // MinDurability is a 0–1 fraction. The Styx settings round-trip can reload a saved "0,35" as 35
            // (comma read as a thousands separator), which would make the bot think it ALWAYS needs repair
            // (LowestDurabilityPercent 1.0 <= 35). Normalize any out-of-range value back into [0,1].
            float minDur = vg.MinDurability;
            if (minDur > 1f) minDur /= 100f;
            minDur = Math.Clamp(minDur, 0f, 1f);
            var xml = new XElement("HBProfile",
                new XElement("MinFreeBagSlots", vg.MinFreeBagSlots),
                new XElement("MinDurability", minDur.ToString(CultureInfo.InvariantCulture)),
                new XElement("SellWhite", vg.SellWhiteJunk));
            _profile = new Profile(xml, null) { Name = "VibeGrinder (synthetic)" };
            // Empty VendorManager + this flag => vendor tree auto-resolves sell/repair/food from data.bin.
            CharacterSettings.Instance.FindVendorsAutomatically = true;

            // Unattended grinder must restock consumables. Default 0 => never buys food/water, so a mana class
            // sit-regens for minutes when OOM (which despawns nearby loot). Seed sane amounts only if the user
            // hasn't set their own. (BuyItems only buys drink for mana classes, so this is safe for all.)
            if (CharacterSettings.Instance.FoodAmount == 0)
                CharacterSettings.Instance.FoodAmount = 20;
            if (CharacterSettings.Instance.DrinkAmount == 0)
                CharacterSettings.Instance.DrinkAmount = 20;

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
