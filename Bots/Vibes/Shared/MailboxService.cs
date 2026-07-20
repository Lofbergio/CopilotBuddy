using System.Collections.Generic;
using System.Diagnostics;
using Styx;
using Styx.Database;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Bots.Vibes.Shared.GrindData;

namespace Bots.Vibes.Shared
{
    /// <summary>
    /// Faction-safe mailbox selection shared by the Vibe botbases (VibeGrinder, VibeQuester). Two
    /// layers of defence:
    ///   1. Offline filter — Mailboxes.db (via core MailboxQueries) stores the factions of guards
    ///      spawned near each mailbox; this drops any the player is hostile to, so the bot never even
    ///      routes to enemy territory. Resolved live per player faction, so one DB serves both sides.
    ///   2. Runtime backstop — when actually approaching a mailbox, scan for a live hostile by it and
    ///      blacklist it on the spot. Catches what the static DB can't see: reputation-gated guards
    ///      (Aldor/Scryer in Shattrath), griefed neutral towns, and roamers.
    /// State is per-instance — one per bot run.
    /// </summary>
    public class MailboxService
    {
        /// <summary>
        /// THE answer to "is mailing set up", for every Vibe bot. The recipient name IS the switch: a name
        /// means "send my valuables there", empty means "don't". A separate on/off flag is a second control
        /// over one decision — and while that flag lived on VibeGrinderSettings it silently governed
        /// VibeParty too (same ItemDisposition), so a VibeParty user could set a recipient, read their own
        /// bot's "an unset MailRecipient already means no mailing" comment, and still never mail anything.
        /// Bot-agnostic on purpose: nothing here may read a concrete bot's settings.
        /// </summary>
        public static bool MailingConfigured
            => !string.IsNullOrEmpty(CharacterSettings.Instance.MailRecipient);

        // Mailboxes found unsafe at runtime — kept out across reloads/map changes.
        private readonly HashSet<WoWPoint> _runtimeUnsafe = new();
        private readonly Stopwatch _backstopThrottle = new();

        /// <summary>
        /// Faction-safe mailboxes for a map, ready to assign to MailboxManager.ForcedMailboxes.
        /// Empty if Mailboxes.db is absent; on an older DB without faction data every mailbox is kept.
        /// </summary>
        public List<Mailbox> LoadSafeMailboxes(uint mapId)
        {
            var result = new List<Mailbox>();
            WoWFactionTemplate myTemplate = StyxWoW.Me?.FactionTemplate;
            int skipped = 0;
            foreach (MailboxRecord mb in MailboxQueries.GetMailboxesWithFactionsOnMap(mapId))
            {
                if (_runtimeUnsafe.Contains(mb.Location)
                    || (myTemplate != null && IsEnemyTerritory(myTemplate, mb.NearbyFactions)))
                {
                    skipped++;
                    continue;
                }
                result.Add(new Mailbox(mb.Location));
            }
            Logging.Write("[Vibes] Loaded {0} safe mailbox(es) for map {1} ({2} skipped as enemy territory).",
                result.Count, mapId, skipped);
            return result;
        }

        /// <summary>
        /// Runtime backstop — call each Pulse (only while the bot is mailing). When the active Mail
        /// POI is within 70yd (so its surroundings are loaded) and a live hostile stands within 25yd,
        /// session-blacklist that mailbox and clear the POI so the engine reroutes to the next-nearest.
        /// Live reaction incorporates reputation, which is what the static DB filter cannot.
        /// </summary>
        public void CheckCurrentMailboxSafety()
        {
            BotPoi poi = BotPoi.Current;
            if (poi == null || poi.Type != PoiType.Mail) return;

            var me = StyxWoW.Me;
            if (me == null) return;

            WoWPoint loc = poi.Location;
            if (me.Location.DistanceSqr(loc) > 70f * 70f) return;
            if (_backstopThrottle.IsRunning && _backstopThrottle.Elapsed.TotalSeconds < 2) return;
            _backstopThrottle.Restart();

            const float guardR2 = 25f * 25f;
            bool hostileNear = false;
            foreach (WoWUnit u in ObjectManager.GetObjectsOfType<WoWUnit>())
            {
                if (u == null || !u.IsValid || u.IsDead) continue;
                if (u.MyReaction < WoWUnitReaction.Neutral && u.Location.DistanceSqr(loc) <= guardR2)
                {
                    hostileNear = true;
                    break;
                }
            }
            if (!hostileNear) return;

            Logging.Write(System.Drawing.Color.Orange,
                "[Vibes] Live hostile by the mailbox at {0} (reputation/roamer the DB can't see) — "
                + "blacklisting for this session, rerouting.", loc);
            BlacklistMailbox(loc);
            BotPoi.Clear("Vibes: unsafe mailbox");
        }

        /// <summary>
        /// Session-blacklist a mailbox found unsafe at runtime. Drops it now and keeps it out of
        /// future reloads. Idempotent.
        /// </summary>
        public void BlacklistMailbox(WoWPoint location)
        {
            if (!_runtimeUnsafe.Add(location))
                return;
            // Tolerance, not exact float equality: a POI location can be a navmesh-snapped copy of the
            // DB point, so == would silently fail to remove it and the bot would reroute back in a loop.
            ProfileManager.CurrentProfile?.MailboxManager?.ForcedMailboxes
                .RemoveAll(m => m.Location.DistanceSqr(location) < 1f);
        }

        /// <summary>
        /// Enemy territory = the player is hostile to a nearby NPC AND none of the player's own
        /// faction stands there. The "own faction present" clause keeps shared sanctuaries like
        /// Dalaran usable (both Horde and Alliance NPCs spawn within range there). Limitation: this
        /// reads only static FactionTemplate.dbc reactions (WoWFaction.CompareFactions ignores
        /// reputation), so reputation-gated hostility — Aldor/Scryer guards turning on the
        /// opposing-rep player — is invisible here; the runtime backstop covers that.
        /// </summary>
        private static bool IsEnemyTerritory(WoWFactionTemplate myTemplate, List<int> nearbyFactions)
        {
            bool hostile = false, friendly = false;
            foreach (int faction in nearbyFactions)
            {
                if (faction <= 0) continue;
                try
                {
                    // Compare via FactionTemplate.dbc (the working path — see FactionResolver: WoWFaction
                    // from me.FactionTemplate.Faction has no template, so WoWFaction.RelationTo always
                    // returned Neutral and no territory was ever flagged enemy). Guard's reaction toward us
                    // = does it attack; ours toward it (Friendly) = our own faction's guards are present.
                    var guard = WoWFactionTemplate.FromId((uint)faction);
                    if (guard == null) continue;
                    if (guard.GetReactionTowards(myTemplate) < WoWUnitReaction.Neutral) hostile = true;
                    else if (myTemplate.GetReactionTowards(guard) > WoWUnitReaction.Neutral) friendly = true;
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
