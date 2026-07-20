using System.Collections.Generic;
using System.Linq;
using Styx;
using Styx.Database;
using Styx.Logic.Pathing;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.Helpers;

namespace Bots.Vibes.Shared.Errands
{
    /// <summary>
    /// One place worth walking to, and every errand it can clear there. The unit of planning is the
    /// STOP, not the errand: an errand that owns its own destination re-resolves its own nearest NPC
    /// with no knowledge of the others, and the bot criss-crosses a town it should have walked once.
    ///
    /// <see cref="Serves"/> comes from npcflags — capability, which the server states. It is not a
    /// promise about stock, so it is re-validated against the open frame on arrival.
    /// </summary>
    public sealed class ErrandStop
    {
        public int Entry;                 // 0 for a mailbox
        public string Name;
        public WoWPoint Location;
        public readonly HashSet<ErrandKind> Serves = new HashSet<ErrandKind>();

        public bool IsMailbox => Entry == 0;

        /// <summary>The kind this stop wears as its POI type — the first still-outstanding errand it
        /// serves, in a fixed order so the type cannot flap between ticks.</summary>
        public ErrandKind PrimaryKind(ICollection<ErrandKind> outstanding)
        {
            foreach (ErrandKind k in ErrandKinds.NpcKinds)
                if (Serves.Contains(k) && outstanding.Contains(k))
                    return k;
            return ErrandKind.Mail;
        }

        public BotPoi ToPoi(ErrandKind kind)
        {
            if (IsMailbox)
                return new BotPoi(Location, PoiType.Mail);
            var vendor = new Styx.Logic.Profiles.Vendor(Entry, Name, ErrandKinds.Vendor(kind), Location);
            return new BotPoi(vendor, ErrandKinds.Poi(kind));
        }

        /// <summary>The live NPC this stop names, once it is loaded. Null means "not here" — either not
        /// yet in range or genuinely absent, which the caller distinguishes by its own distance.</summary>
        public WoWUnit LiveUnit()
        {
            if (IsMailbox) return null;
            return ObjectManager.GetObjectsOfType<WoWUnit>(false, false)
                .Where(u => u != null && u.IsValid && !u.IsDead && u.Entry == (uint)Entry
                            && u.Location.DistanceSqr(Location) < 40f * 40f)
                .OrderBy(u => u.DistanceSqr)
                .FirstOrDefault();
        }

        /// <summary>The in-world mailbox bound to this stop's point (15yd, Gatherbuddy's binding — the
        /// globally-nearest one BotPoi.AsObject returns has no distance cap at all).</summary>
        public WoWGameObject LiveMailbox()
        {
            if (!IsMailbox) return null;
            return ObjectManager.GetObjectsOfType<WoWGameObject>()
                .Where(go => go != null && go.IsValid && go.IsMailbox
                             && go.Location.DistanceSqr(Location) < 15f * 15f)
                .OrderBy(go => go.DistanceSqr)
                .FirstOrDefault();
        }

        public override string ToString()
            => string.Format("{0}[{1}] ({2})", IsMailbox ? "mailbox" : Name, Entry,
                             string.Join("+", Serves.Select(k => k.ToString())));
    }

    /// <summary>
    /// Turns a set of demands into a route. Greedy nearest-neighbour over candidate stops: not optimal,
    /// but it stops being stupid, and a stop that clears two demands removes a node from the tour and so
    /// wins on total path cost by itself. There is deliberately no "prefer a combo vendor" bonus — that
    /// would be a magic number pretending to be a policy; the tour cost already produces the answer.
    /// </summary>
    public static class ErrandPlanner
    {
        // How many candidates to consider per errand. The DB query is nearest-first, so the tail is
        // vendors on the far side of the continent; the ceiling exists so a plan is one query and a
        // handful of distance comparisons, not a survey.
        private const int CandidatesPerKind = 8;

        // A mailbox this close to a stop we are already visiting is free to use — the same radius
        // LevelBot's mail gate has always meant by "we're in town anyway", now measured against the
        // ROUTE instead of against where the character happens to be standing.
        private const float MailOnRouteYd = 200f;

        /// <summary>
        /// Builds the tour. Returns an empty list when nothing is reachable — the caller must treat that
        /// as "no trip", never as "suppress the demand".
        /// </summary>
        public static List<ErrandStop> Plan(WoWPoint from, List<ErrandDemand> demands)
        {
            var tour = new List<ErrandStop>();
            LocalPlayer me = StyxWoW.Me;
            Profile profile = ProfileManager.CurrentProfile;
            if (me == null || profile == null || demands.Count == 0) return tour;

            var hard = demands.Where(d => !d.Opportunistic).Select(d => d.Kind).ToHashSet();
            var candidates = BuildCandidates(me, profile, hard);

            // Greedy nearest-neighbour from where we stand. Each pick removes every demand it covers,
            // so a sell+repair NPC further out beats two NPCs in different directions on its own.
            WoWPoint at = from;
            var remaining = new HashSet<ErrandKind>(hard);
            while (remaining.Count > 0)
            {
                ErrandStop best = null;
                double bestDist = double.MaxValue;
                foreach (ErrandStop c in candidates)
                {
                    if (tour.Contains(c) || !c.Serves.Overlaps(remaining)) continue;
                    double d = at.Distance(c.Location);
                    if (d < bestDist) { bestDist = d; best = c; }
                }
                if (best == null) break;   // nothing left can serve what remains — the rest of the tour still stands

                tour.Add(best);
                at = best.Location;
                remaining.ExceptWith(best.Serves);
            }

            InsertMail(demands, profile, from, tour);
            return tour;
        }

        /// <summary>
        /// Mail joins the tour as a peer of sell and repair, never as a step nested inside a merchant
        /// visit — that nesting is what re-fires it after every transaction (sell → mail → repair → mail).
        /// A dedicated mail trip is only justified by bag pressure; otherwise the mailbox must already
        /// lie on the route we are walking anyway.
        /// </summary>
        private static void InsertMail(List<ErrandDemand> demands, Profile profile, WoWPoint from, List<ErrandStop> tour)
        {
            ErrandDemand mail = demands.FirstOrDefault(d => d.Kind == ErrandKind.Mail);
            if (mail.Kind != ErrandKind.Mail || profile.MailboxManager == null) return;

            if (mail.Opportunistic)
            {
                // No other business ⇒ no route to be on ⇒ not worth the walk.
                if (tour.Count == 0) return;

                ErrandStop nearest = null;
                int insertAfter = -1;
                double bestGap = MailOnRouteYd;
                for (int i = 0; i < tour.Count; i++)
                {
                    Mailbox mb = profile.MailboxManager.GetClosestMailbox(tour[i].Location);
                    if (mb == null) continue;
                    double gap = mb.Location.Distance(tour[i].Location);
                    if (gap >= bestGap) continue;
                    bestGap = gap;
                    insertAfter = i;
                    nearest = MailboxStop(mb);
                }
                if (nearest == null) return;
                tour.Insert(insertAfter + 1, nearest);
                return;
            }

            // Bag pressure: the mail stop is business in its own right, so it is placed by route like
            // any other — nearest to whatever we would otherwise walk past last.
            WoWPoint anchor = tour.Count > 0 ? tour[tour.Count - 1].Location : from;
            Mailbox pressed = profile.MailboxManager.GetClosestMailbox(anchor);
            if (pressed != null)
                tour.Add(MailboxStop(pressed));
        }

        private static ErrandStop MailboxStop(Mailbox mb)
        {
            var stop = new ErrandStop { Entry = 0, Name = "mailbox", Location = mb.Location };
            stop.Serves.Add(ErrandKind.Mail);
            return stop;
        }

        /// <summary>
        /// One query per distinct capability flag, merged by NPC entry — so an NPC that carries both the
        /// Vendor and the Repair flag becomes ONE stop serving both, which is the whole point.
        /// </summary>
        private static List<ErrandStop> BuildCandidates(LocalPlayer me, Profile profile, HashSet<ErrandKind> kinds)
        {
            var byEntry = new Dictionary<int, ErrandStop>();
            VendorManager vm = profile.VendorManager;

            foreach (ErrandKind kind in ErrandKinds.NpcKinds)
            {
                if (!kinds.Contains(kind)) continue;

                // A class trainer is not identifiable from npcflags alone (trainer_type/trainer_class
                // discriminate a class trainer from a pet trainer), so that one resolve stays with the
                // resolver that knows how. Everything else plans from flags.
                if (kind == ErrandKind.Train)
                {
                    Styx.Logic.Profiles.Vendor trainer = vm?.GetClosestVendor(Styx.Logic.Profiles.Vendor.VendorType.Train);
                    if (trainer != null)
                        Merge(byEntry, trainer.Entry, trainer.Name, trainer.Location, kind);
                    continue;
                }

                // A profile that names its own vendors is the authority on where to shop; only the
                // profile-less bots fall through to the data.bin sweep. Asking the resolver here costs
                // the combo merge for that case, which is the correct trade — a hand-authored vendor
                // list has no npcflags to merge on.
                if (vm != null && vm.AllVendors != null && vm.AllVendors.Count > 0)
                {
                    Styx.Logic.Profiles.Vendor named = vm.GetClosestVendor(ErrandKinds.Vendor(kind));
                    if (named != null)
                        Merge(byEntry, named.Entry, named.Name, named.Location, kind);
                    continue;
                }

                HashSet<int> excluded = vm?.Blacklist != null && vm.Blacklist.Count > 0
                    ? vm.Blacklist.ExcludedEntries(ErrandKinds.Vendor(kind))
                    : null;

                foreach (NpcResult npc in NpcQueries.GetNearbyNpcs(me.MapId, me.Location, ErrandKinds.Flag(kind),
                                                                   CandidatesPerKind, excluded))
                {
                    ErrandStop stop = Merge(byEntry, npc.Entry, npc.Name, npc.Location, kind);
                    // The row carries the whole mask, so record every OTHER errand this NPC could also
                    // serve. Without this a sell+repair vendor found by the sell query would look like a
                    // sell-only stop and the repair demand would plan a second walk.
                    foreach (ErrandKind other in ErrandKinds.NpcKinds)
                        if (other != ErrandKind.Train && kinds.Contains(other)
                            && (npc.NpcFlags & (uint)ErrandKinds.Flag(other)) != 0)
                            stop.Serves.Add(other);
                }
            }
            return byEntry.Values.ToList();
        }

        private static ErrandStop Merge(Dictionary<int, ErrandStop> byEntry, int entry, string name, WoWPoint loc, ErrandKind kind)
        {
            if (!byEntry.TryGetValue(entry, out ErrandStop stop))
            {
                stop = new ErrandStop { Entry = entry, Name = name, Location = loc };
                byEntry[entry] = stop;
            }
            stop.Serves.Add(kind);
            return stop;
        }
    }
}
