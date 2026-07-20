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
        // Originals of the global CharacterSettings we seed, so Stop() can restore them (don't
        // permanently overwrite a user's deliberate 0 = "never buy consumables").
        private int _origPullDistance = -1;
        private bool? _origFindVendors;
        private bool? _origRessAtSpiritHealers;
        private bool? _origKillBetweenHotspots;

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
            //
            // Sell mask is wide (grey→blue, NEVER purple); VibeGrinder's OnVendorItems hook then protects
            // every item whose disposition isn't Vendor, so only true junk is sold (ItemDisposition decides).
            // Mail flags are all OFF: mailing is driven entirely by the OnMailItems hook, not by quality.
            // NeedToMail() doesn't depend on these flags, so mail runs still trigger. See Loot/CLAUDE.md.
            var vg = VibeGrinderSettings.Instance;
            // Durability is now an int percent (0-100) — no float-locale round-trip risk. Project it
            // onto the profile as the 0-1 fraction LevelBot's repair gate expects.
            var xml = new XElement("HBProfile",
                new XElement("MinFreeBagSlots", vg.MinFreeBagSlots),
                new XElement("MinDurability", vg.MinDurabilityFraction.ToString(CultureInfo.InvariantCulture)),
                new XElement("SellGrey", true),
                new XElement("SellWhite", true),
                new XElement("SellGreen", true),
                new XElement("SellBlue", true),
                new XElement("SellPurple", false),
                new XElement("MailGrey", false),
                new XElement("MailWhite", false),
                new XElement("MailGreen", false),
                new XElement("MailBlue", false),
                new XElement("MailPurple", false));
            _profile = new Profile(xml, null) { Name = "VibeGrinder (synthetic)" };
            // Empty VendorManager + this flag => vendor tree auto-resolves sell/repair/food from data.bin.
            // Captured/restored like the others — a profile-driven botbase run after us may rely on its
            // authored <Vendor> entries only (this leaked true permanently before; audit 2026-07-05).
            _origFindVendors ??= CharacterSettings.Instance.FindVendorsAutomatically;
            CharacterSettings.Instance.FindVendorsAutomatically = true;

            // Food/water restock amounts are USER-controlled (CharacterSettings.FoodAmount/DrinkAmount). We no
            // longer auto-seed 0 -> 20: a deliberate 0 is honored — no buying, and with empty bags no drinking
            // (a pure self-sustain run). The old seeding silently overrode that, so it's gone.

            // Pull-range dead band: LevelBot only walks to a target while dist >= PullDistance, then hands
            // off to the routine to engage. If PullDistance (default 45/48) exceeds the routine's cast range
            // (~30yd for a low-level caster), a mob at 30-48yd reads as "in pull range" so nobody walks
            // closer, but the routine can't cast that far and won't move pre-combat → the bot stares at a
            // distant/stationary mob (e.g. a Kolkar Stormer) forever. Cap it DOWN (never raise a deliberate
            // melee value) so the walk-in always closes the gap before the routine casts. Restored on Stop.
            _origPullDistance = CharacterSettings.Instance.PullDistance;
            int pullCap = VibeGrinderSettings.Instance.MaxPullDistance;
            if (CharacterSettings.Instance.PullDistance > pullCap)
                CharacterSettings.Instance.PullDistance = pullCap;

            // Unattended corpse-camp escape. With this OFF (the default) LevelBot's 3-strike camp
            // protection is dead code, so a corpse camped by a hostile pack spins forever: it can't res
            // (mobs in range), never escalates to the spirit healer, and the death subtree keeps the tree
            // busy so the supervisor can't relocate. Enabling it lets a *camped* corpse res at the
            // graveyard (normal deaths still corpse-run) — which then frees the supervisor to relocate.
            _origRessAtSpiritHealers = CharacterSettings.Instance.RessAtSpiritHealers;
            CharacterSettings.Instance.RessAtSpiritHealers = true;

            // Route-killing while MOUNTED. ApplyPullCommitment deliberately commits to on-route mobs, but
            // stock DecoratorNeedToFindTarget refuses to convert an in-range FirstUnit to a Kill POI while
            // mounted unless the mob is near the DESTINATION hotspot — or KillBetweenHotspots is on. With it
            // off, a mid-trek commit oscillates around PullDistance forever: approach while >27yd, hotspot-
            // move once <27yd, re-path each flip (Witherbark/Boulderfist treks, log 2026-07-03_1458 15:15+).
            // The find-target level filter still applies, paired with the band-bounded surfacing in
            // IncludeNearbyHostiles so the two can't disagree.
            _origKillBetweenHotspots = StyxSettings.Instance.KillBetweenHotspots;
            StyxSettings.Instance.KillBetweenHotspots = true;

            ProfileManager.UseSyntheticProfile(_profile);
        }

        /// <summary>Refill the persistent GrindArea with a spot's hotspots/factions/band and activate it.</summary>
        public void Install(GrindSpot spot, int playerLevel)
        {
            EnsureProfile();

            // Feed the map's mailboxes into the synthetic profile (reload only on map change) so the
            // reused vendor behaviour can mail to the bank. Skipped when no recipient is set.
            if (Bots.Vibes.Shared.MailboxService.MailingConfigured && MapChanged(spot.Map))
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
            if (mgr == null)
            {
                Logging.WriteDebug("[VibeGrinder] Mailbox load skipped: no MailboxManager on the current profile yet.");
                return;
            }
            mgr.ForcedMailboxes = _mailboxes.LoadSafeMailboxes(map);
        }

        /// <summary>Restore the global CharacterSettings we seeded in EnsureProfile. Call from Stop().</summary>
        public void RestoreCharacterSettings()
        {
            if (_origPullDistance >= 0) { CharacterSettings.Instance.PullDistance = _origPullDistance; _origPullDistance = -1; }
            if (_origRessAtSpiritHealers.HasValue) { CharacterSettings.Instance.RessAtSpiritHealers = _origRessAtSpiritHealers.Value; _origRessAtSpiritHealers = null; }
            if (_origKillBetweenHotspots.HasValue) { StyxSettings.Instance.KillBetweenHotspots = _origKillBetweenHotspots.Value; _origKillBetweenHotspots = null; }
            if (_origFindVendors.HasValue) { CharacterSettings.Instance.FindVendorsAutomatically = _origFindVendors.Value; _origFindVendors = null; }
        }
    }
}
