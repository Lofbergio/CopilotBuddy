using System.Collections.Generic;
using Bots.VibeGrinder.Selection;
using Styx;
using Styx.Database;
using Styx.Helpers;
using Styx.Logic.AreaManagement;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.WoWInternals;

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
        // Mailboxes found unsafe at runtime (live hostile nearby) — kept out across reloads.
        private readonly System.Collections.Generic.HashSet<WoWPoint> _runtimeUnsafeMailboxes = new();

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
        /// Feed the map's mailbox locations (shared Mailboxes.db, via core MailboxQueries) into the
        /// synthetic profile's ForcedMailboxes so LevelBot's reused mail behaviour can find one.
        /// Empty if Mailboxes.db is absent — mailing then just stays idle.
        /// </summary>
        private void LoadMailboxesForMap(uint map)
        {
            var mgr = ProfileManager.CurrentProfile?.MailboxManager;
            if (mgr == null) return;

            mgr.ForcedMailboxes.Clear();

            // Skip mailboxes in enemy-faction territory: one guarded by NPCs the player is hostile to
            // (an Alliance town for a Horde char) would get the bot killed walking in. Nearby factions
            // are precomputed offline; resolve them live so one DB is correct for both factions. On an
            // older Mailboxes.db (no faction data) every record's list is empty → all kept (prior behaviour).
            WoWFaction myFaction = StyxWoW.Me?.FactionTemplate.Faction;
            int skipped = 0;
            foreach (MailboxRecord mb in MailboxQueries.GetMailboxesWithFactionsOnMap(map))
            {
                if (_runtimeUnsafeMailboxes.Contains(mb.Location)
                    || (myFaction != null && IsEnemyTerritory(myFaction, mb.NearbyFactions)))
                {
                    skipped++;
                    continue;
                }
                mgr.ForcedMailboxes.Add(new Mailbox(mb.Location));
            }

            Logging.Write("[VibeGrinder] Loaded {0} mailbox location(s) for map {1} ({2} skipped as enemy territory).",
                mgr.ForcedMailboxes.Count, map, skipped);
        }

        /// <summary>
        /// Session-blacklist a mailbox found unsafe at runtime (a live hostile by it that the static
        /// DB couldn't see — Aldor/Scryer, a griefed town, a roamer). Drops it now and keeps it out
        /// of future reloads. Idempotent.
        /// </summary>
        public void BlacklistMailbox(WoWPoint location)
        {
            if (!_runtimeUnsafeMailboxes.Add(location))
                return;
            ProfileManager.CurrentProfile?.MailboxManager?.ForcedMailboxes.RemoveAll(m => m.Location == location);
        }

        /// <summary>
        /// Enemy territory = the player is hostile to a nearby NPC AND none of the player's own
        /// faction stands there. The "own faction present" clause keeps shared sanctuaries like
        /// Dalaran usable (both Horde and Alliance NPCs spawn within range there). Limitation: this
        /// reads only static FactionTemplate.dbc reactions (WoWFaction.CompareFactions ignores
        /// reputation), so reputation-gated hostility — Aldor/Scryer guards in Shattrath turning on
        /// the opposing-rep player — is invisible. Those tier mailboxes fall back to the central
        /// neutral Shattrath mailbox in practice; a rep-aware check would be needed for full cover.
        /// </summary>
        private static bool IsEnemyTerritory(WoWFaction myFaction, List<int> nearbyFactions)
        {
            bool hostile = false, friendly = false;
            foreach (int faction in nearbyFactions)
            {
                if (faction <= 0) continue;
                try
                {
                    WoWUnitReaction r = myFaction.RelationTo(new WoWFaction((uint)faction));
                    if (r < WoWUnitReaction.Neutral) hostile = true;
                    else if (r > WoWUnitReaction.Neutral) friendly = true;   // our own faction's guards are here
                }
                catch
                {
                    // Unknown/invalid faction template — ignore, same as FactionResolver.
                }
            }
            return hostile && !friendly;
        }
    }
}
