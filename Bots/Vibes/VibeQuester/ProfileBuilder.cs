using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace VibeQuester
{
    public class ProfileBuilder
    {
        private static readonly XNamespace Ns = "";
        private readonly string _profilePath;

        public ProfileBuilder() : this(null) { }

        public ProfileBuilder(string profilePath)
        {
            _profilePath = profilePath;
        }

        public string BuildProfileXml(
            List<QuestEntry> quests,
            QuestDatabase db,
            string zoneName,
            string playerName,
            int playerLevel,
            List<VendorEntry> vendors = null,
            int playerMapId = 0,
            HashSet<int> priorityTurninIds = null,
            double playerX = 0,
            double playerY = 0)
        {
            XDocument doc = new XDocument(
                new XElement("HBProfile",
                    new XElement("Name", $"VibeQuester - {zoneName}"),
                    new XElement("MinLevel", 1),
                    new XElement("MaxLevel", 80),
                    new XElement("MinDurability", "0.2"),
                    new XElement("MinFreeBagSlots", "2"),
                    new XElement("AvoidMobs"),
                    new XElement("Blackspots"),
                    new XElement("Mailboxes"),
                    BuildVendorsElement(vendors)
                )
            );

            XElement root = doc.Root;

            foreach (QuestEntry qe in quests)
            {
                XElement questElement = new XElement("Quest",
                    new XAttribute("Id", qe.Id),
                    new XAttribute("Name", qe.Name)
                );

                foreach (QuestObjective obj in qe.Objectives)
                {
                    if (obj.Type == ObjectiveType.KillMob && obj.MobId > 0)
                    {
                        XElement objElement = new XElement("Objective",
                            new XAttribute("Type", "KillMob"),
                            new XAttribute("MobId", obj.MobId),
                            new XAttribute("KillCount", obj.KillCount)
                        );

                        XElement hotspots = new XElement("Hotspots");
                        string mobKey = obj.MobId.ToString();
                        if (db.CreatureSpawns.TryGetValue(mobKey, out List<SpawnPoint> spawns))
                        {
                            foreach (SpawnPoint sp in spawns.Take(10))
                            {
                                hotspots.Add(new XElement("Hotspot",
                                    new XAttribute("X", sp.X),
                                    new XAttribute("Y", sp.Y),
                                    new XAttribute("Z", sp.Z)
                                ));
                            }
                        }
                        objElement.Add(hotspots);
                        questElement.Add(objElement);
                    }
                    else if (obj.Type == ObjectiveType.CollectItem && obj.ItemId > 0)
                    {
                        XElement objElement = new XElement("Objective",
                            new XAttribute("Type", "CollectItem"),
                            new XAttribute("ItemId", obj.ItemId),
                            new XAttribute("CollectCount", obj.CollectCount)
                        );

                        XElement hotspots = new XElement("Hotspots");
                        if (obj.MobId > 0)
                        {
                            string mobKey = obj.MobId.ToString();
                            if (db.CreatureSpawns.TryGetValue(mobKey, out List<SpawnPoint> spawns))
                            {
                                foreach (SpawnPoint sp in spawns.Take(10))
                                {
                                    hotspots.Add(new XElement("Hotspot",
                                        new XAttribute("X", sp.X),
                                        new XAttribute("Y", sp.Y),
                                        new XAttribute("Z", sp.Z)
                                    ));
                                }
                            }
                        }
                        objElement.Add(hotspots);
                        questElement.Add(objElement);
                    }
                    else if (obj.Type == ObjectiveType.CollectFromGameObject && obj.GameObjectId > 0)
                    {
                        XElement objElement = new XElement("Objective",
                            new XAttribute("Type", "CollectItem"),
                            new XAttribute("ItemId", obj.ItemId),
                            new XAttribute("CollectCount", obj.CollectCount)
                        );

                        XElement collectFrom = new XElement("CollectFrom",
                            new XElement("GameObject",
                                new XAttribute("Name", obj.GameObjectName ?? $"GameObject_{obj.GameObjectId}"),
                                new XAttribute("Id", obj.GameObjectId)
                            )
                        );
                        objElement.Add(collectFrom);

                        XElement hotspots = new XElement("Hotspots");
                        string goKey = obj.GameObjectId.ToString();
                        if (db.GameObjectSpawns.TryGetValue(goKey, out List<SpawnPoint> spawns))
                        {
                            foreach (SpawnPoint sp in spawns.Take(10))
                            {
                                hotspots.Add(new XElement("Hotspot",
                                    new XAttribute("X", sp.X),
                                    new XAttribute("Y", sp.Y),
                                    new XAttribute("Z", sp.Z)
                                ));
                            }
                        }
                        objElement.Add(hotspots);
                        questElement.Add(objElement);
                    }
                }

                root.Add(questElement);
            }

            XElement questOrder = new XElement("QuestOrder");

            // Drain already-complete log quests first. QuestOrder executes top-down (the engine pops
            // node[0]), so emitting these turn-ins ahead of every pickup frees log slots before the
            // 25-quest cap blocks new pickups. Their pickup/objective nodes are skipped below.
            if (priorityTurninIds != null && priorityTurninIds.Count > 0)
            {
                foreach (QuestEntry qe in quests.Where(q => priorityTurninIds.Contains(q.Id)))
                {
                    QuestEnderEntry ender = db.QuestEnders.FirstOrDefault(e => e.QuestId == qe.Id);
                    if (ender == null) continue;
                    XElement turnIn = new XElement("TurnIn",
                        new XAttribute("QuestName", qe.Name),
                        new XAttribute("QuestId", qe.Id),
                        new XAttribute("TurnInName", ender.EnderName),
                        new XAttribute("TurnInId", ender.EnderId),
                        new XAttribute("TurnInType", ender.EnderType == QuestObjectType.GameObject ? "GameObject" : "Npc")
                    );
                    AddSpawnXyz(turnIn, db, ender.EnderId, ender.EnderType, playerMapId);
                    questOrder.Add(turnIn);
                }
            }

            // Phases stay batched (pickups, then objectives, then turn-ins) — that's what makes
            // hub-and-spoke questing efficient (grab a hub's quests, clear the shared field, hand them
            // all in). But within each phase, walk the nodes in a greedy nearest-neighbour tour from the
            // player's scan position instead of arbitrary DB order, to kill intra-phase zig-zag.
            List<QuestEntry> routable = quests
                .Where(q => priorityTurninIds == null || !priorityTurninIds.Contains(q.Id))
                .ToList();
            List<QuestEntry> pickupOrder = OrderByNearestTour(routable, q => GiverXY(db, q, playerMapId), playerX, playerY);
            List<QuestEntry> objectiveOrder = OrderByNearestTour(routable, q => FirstObjectiveXY(db, q, playerMapId), playerX, playerY);
            List<QuestEntry> turnInOrder = OrderByNearestTour(routable, q => EnderXY(db, q, playerMapId), playerX, playerY);

            foreach (QuestEntry qe in pickupOrder)
            {
                QuestGiverEntry giver = db.QuestGivers
                    .FirstOrDefault(qg => qg.QuestId == qe.Id);

                if (giver != null)
                {
                    // Emit GiverType + resolved coordinate: creature and gameobject ID spaces overlap
                    // (e.g. entry 3076 is both a creature and the "Dirt-stained Map" gameobject), so
                    // without these the engine resolves the wrong entity and walks to the wrong place.
                    XElement pickup = new XElement("PickUp",
                        new XAttribute("QuestName", qe.Name),
                        new XAttribute("QuestId", qe.Id),
                        new XAttribute("GiverName", giver.GiverName),
                        new XAttribute("GiverId", giver.GiverId),
                        new XAttribute("GiverType", giver.GiverType == QuestObjectType.GameObject ? "GameObject" : "Npc")
                    );
                    AddSpawnXyz(pickup, db, giver.GiverId, giver.GiverType, playerMapId);
                    questOrder.Add(pickup);
                }
            }

            foreach (QuestEntry qe in objectiveOrder)
            {
                foreach (QuestObjective obj in qe.Objectives)
                {
                    if (obj.Type == ObjectiveType.KillMob && obj.MobId > 0)
                    {
                        questOrder.Add(new XElement("Objective",
                            new XAttribute("QuestName", qe.Name),
                            new XAttribute("QuestId", qe.Id),
                            new XAttribute("Type", "KillMob"),
                            new XAttribute("MobId", obj.MobId),
                            new XAttribute("KillCount", obj.KillCount)
                        ));
                    }
                    else if (obj.Type == ObjectiveType.CollectItem && obj.ItemId > 0)
                    {
                        XElement xe = new XElement("Objective",
                            new XAttribute("QuestName", qe.Name),
                            new XAttribute("QuestId", qe.Id),
                            new XAttribute("Type", "CollectItem"),
                            new XAttribute("ItemId", obj.ItemId),
                            new XAttribute("CollectCount", obj.CollectCount)
                        );
                        if (obj.MobId > 0)
                            xe.Add(new XAttribute("MobId", obj.MobId));
                        questOrder.Add(xe);
                    }
                    else if (obj.Type == ObjectiveType.CollectFromGameObject && obj.GameObjectId > 0)
                    {
                        questOrder.Add(new XElement("Objective",
                            new XAttribute("QuestName", qe.Name),
                            new XAttribute("QuestId", qe.Id),
                            new XAttribute("Type", "CollectItem"),
                            new XAttribute("ItemId", obj.ItemId),
                            new XAttribute("CollectCount", obj.CollectCount)
                        ));
                    }
                }
            }

            foreach (QuestEntry qe in turnInOrder)
            {
                QuestEnderEntry ender = db.QuestEnders
                    .FirstOrDefault(qe2 => qe2.QuestId == qe.Id);

                if (ender != null)
                {
                    XElement turnIn = new XElement("TurnIn",
                        new XAttribute("QuestName", qe.Name),
                        new XAttribute("QuestId", qe.Id),
                        new XAttribute("TurnInName", ender.EnderName),
                        new XAttribute("TurnInId", ender.EnderId),
                        new XAttribute("TurnInType", ender.EnderType == QuestObjectType.GameObject ? "GameObject" : "Npc")
                    );
                    AddSpawnXyz(turnIn, db, ender.EnderId, ender.EnderType, playerMapId);
                    questOrder.Add(turnIn);
                }
            }

            root.Add(questOrder);

            return doc.Declaration + Environment.NewLine + doc.ToString();
        }

        // Greedy nearest-neighbour tour over quests by a per-quest coordinate (giver/objective/ender),
        // starting at the player. O(n²) but n ≤ MaxQuestsPerProfile. Quests with no resolvable on-map
        // coordinate keep their original relative order and are emitted last (they can't be routed).
        private static List<QuestEntry> OrderByNearestTour(
            List<QuestEntry> quests,
            Func<QuestEntry, (double x, double y)?> coord,
            double startX, double startY)
        {
            var withCoord = new List<(QuestEntry q, double x, double y)>();
            var noCoord = new List<QuestEntry>();
            foreach (QuestEntry q in quests)
            {
                var c = coord(q);
                if (c.HasValue) withCoord.Add((q, c.Value.x, c.Value.y));
                else noCoord.Add(q);
            }

            var ordered = new List<QuestEntry>(quests.Count);
            double cx = startX, cy = startY;
            while (withCoord.Count > 0)
            {
                int best = 0;
                double bestD = double.MaxValue;
                for (int i = 0; i < withCoord.Count; i++)
                {
                    double dx = withCoord[i].x - cx, dy = withCoord[i].y - cy;
                    double d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = i; }
                }
                ordered.Add(withCoord[best].q);
                cx = withCoord[best].x;
                cy = withCoord[best].y;
                withCoord.RemoveAt(best);
            }
            ordered.AddRange(noCoord);
            return ordered;
        }

        private static (double, double)? GiverXY(QuestDatabase db, QuestEntry qe, int mapId)
        {
            QuestGiverEntry g = db.QuestGivers.FirstOrDefault(x => x.QuestId == qe.Id);
            return g == null ? null : SpawnXY(db, g.GiverId, g.GiverType, mapId);
        }

        private static (double, double)? EnderXY(QuestDatabase db, QuestEntry qe, int mapId)
        {
            QuestEnderEntry e = db.QuestEnders.FirstOrDefault(x => x.QuestId == qe.Id);
            return e == null ? null : SpawnXY(db, e.EnderId, e.EnderType, mapId);
        }

        // First objective with a resolvable spawn — kill/collect mobs are creatures, gather nodes are GOs.
        private static (double, double)? FirstObjectiveXY(QuestDatabase db, QuestEntry qe, int mapId)
        {
            foreach (QuestObjective o in qe.Objectives)
            {
                if (o.Type == ObjectiveType.CollectFromGameObject && o.GameObjectId > 0)
                {
                    var c = SpawnXY(db, o.GameObjectId, QuestObjectType.GameObject, mapId);
                    if (c != null) return c;
                }
                else if (o.MobId > 0)
                {
                    var c = SpawnXY(db, o.MobId, QuestObjectType.Creature, mapId);
                    if (c != null) return c;
                }
            }
            return null;
        }

        // Nearest spawn coordinate on the player's map (falls back to any map so cross-map givers still
        // route rather than dropping to the unordered tail).
        private static (double, double)? SpawnXY(QuestDatabase db, int entry, QuestObjectType type, int mapId)
        {
            var dict = type == QuestObjectType.GameObject ? db.GameObjectSpawns : db.CreatureSpawns;
            if (!dict.TryGetValue(entry.ToString(), out List<SpawnPoint> spawns) || spawns.Count == 0)
                return null;
            SpawnPoint sp = spawns.FirstOrDefault(s => s.Map == mapId) ?? spawns[0];
            return (sp.X, sp.Y);
        }

        // Add X/Y/Z from the giver/ender's spawn on the player's map. Only emits when an on-map spawn
        // exists, so the engine uses our authoritative coordinate (and never a wrong-map one).
        private static void AddSpawnXyz(XElement el, QuestDatabase db, int entry, QuestObjectType type, int playerMapId)
        {
            var dict = type == QuestObjectType.GameObject ? db.GameObjectSpawns : db.CreatureSpawns;
            if (!dict.TryGetValue(entry.ToString(), out List<SpawnPoint> spawns)) return;

            SpawnPoint sp = spawns.FirstOrDefault(s => s.Map == playerMapId);
            if (sp == null) return;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            el.Add(new XAttribute("X", sp.X.ToString(ci)));
            el.Add(new XAttribute("Y", sp.Y.ToString(ci)));
            el.Add(new XAttribute("Z", sp.Z.ToString(ci)));
        }

        public string WriteProfile(string xml)
        {
            if (_profilePath == null)
                return null;
            File.WriteAllText(_profilePath, xml);
            return _profilePath;
        }

        public string BuildEmptyProfile(string zoneName, int playerLevel,
            List<VendorEntry> vendors = null)
        {
            XDocument doc = new XDocument(
                new XElement("HBProfile",
                    new XElement("Name", $"VibeQuester - {zoneName} (empty)"),
                    new XElement("MinLevel", 1),
                    new XElement("MaxLevel", 80),
                    new XElement("MinDurability", "0.2"),
                    new XElement("MinFreeBagSlots", "2"),
                    new XElement("AvoidMobs"),
                    new XElement("Blackspots"),
                    new XElement("Mailboxes"),
                    BuildVendorsElement(vendors),
                    new XElement("QuestOrder")
                )
            );

            string xml = doc.Declaration + Environment.NewLine + doc.ToString();
            return WriteProfile(xml);
        }

        private static XElement BuildVendorsElement(List<VendorEntry> vendors)
        {
            if (vendors == null || vendors.Count == 0)
                return new XElement("Vendors");

            XElement ve = new XElement("Vendors");
            foreach (VendorEntry v in vendors)
            {
                XElement vendor = new XElement("Vendor",
                    new XAttribute("Name", v.Name ?? ""),
                    new XAttribute("Entry", v.Entry),
                    new XAttribute("Type", v.Type ?? ""),
                    new XAttribute("X", v.X),
                    new XAttribute("Y", v.Y),
                    new XAttribute("Z", v.Z)
                );

                if (!string.IsNullOrEmpty(v.TrainClass))
                    vendor.Add(new XAttribute("TrainClass", v.TrainClass));

                ve.Add(vendor);
            }
            return ve;
        }
    }
}
