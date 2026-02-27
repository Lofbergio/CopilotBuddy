using System;
using System.Collections.Generic;
using System.Linq;
using Styx;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Bots.DungeonBuddy.Avoidance
{
    /// <summary>
    /// Gestionnaire global des zones à éviter
    /// </summary>
    public static class AvoidanceManager
    {
        public static readonly List<AvoidInfo> AvoidInfos = new();
        public static readonly List<Avoid> Avoids = new();
        
        public static void Add(AvoidInfo avoid)
        {
            AvoidInfos.Add(avoid);
        }

        public static void AddRange(IEnumerable<AvoidInfo> avoids)
        {
            AvoidInfos.AddRange(avoids);
        }

        public static void Remove(AvoidInfo avoid)
        {
            AvoidInfos.Remove(avoid);
        }

        public static void RemoveAll(Predicate<AvoidInfo> match)
        {
            AvoidInfos.RemoveAll(match);
        }

        public static void Clear()
        {
            AvoidInfos.Clear();
            Avoids.Clear();
        }

        /// <summary>
        /// Met à jour les zones d'évitement actives.
        /// Appelé à chaque pulse du bot.
        /// </summary>
        public static void Update()
        {
            // Supprimer les avoids invalides
            Avoids.RemoveAll(a => !a.IsValid);

            // Parcourir les objets du monde pour créer de nouveaux avoids
            foreach (var obj in ObjectManager.ObjectList)
            {
                foreach (var avoidInfo in AvoidInfos.Where(ai => ai.ObjectSelector != null))
                {
                    if (avoidInfo.ObjectSelector(obj) && avoidInfo.CanRun(obj))
                    {
                        // Vérifier si cet objet n'a pas déjà un avoid
                        if (!Avoids.Any(a => a is AvoidObject ao && ao.Info == avoidInfo))
                        {
                            Avoids.Add(new AvoidObject(avoidInfo, obj));
                        }
                    }
                }
            }

            // Ajouter les avoids de position
            foreach (var avoidInfo in AvoidInfos.Where(ai => ai.LocationSelector != null))
            {
                if (avoidInfo.CanRun(null))
                {
                    if (!Avoids.Any(a => a is AvoidLocation al && al.Info == avoidInfo))
                    {
                        Avoids.Add(new AvoidLocation(avoidInfo));
                    }
                }
            }

            // Mettre à jour tous les avoids
            foreach (var avoid in Avoids)
            {
                avoid.Update();
            }
        }

        /// <summary>
        /// Vérifie si une position est dans une zone d'évitement
        /// </summary>
        public static bool IsInAvoidance(WoWPoint location)
        {
            return Avoids.Any(a => a.Location.DistanceSqr(location) < a.RadiusSqr);
        }

        /// <summary>
        /// Trouve un point sûr pour fuir
        /// </summary>
        public static WoWPoint GetSafePoint(WoWPoint from, float minDistance = 10f)
        {
            // Trouver la direction opposée à la menace la plus proche
            var nearestAvoid = Avoids
                .Where(a => a.Location.DistanceSqr(from) < (a.Radius + 20) * (a.Radius + 20))
                .OrderBy(a => a.Location.DistanceSqr(from))
                .FirstOrDefault();

            if (nearestAvoid == null)
                return from;

            // Direction opposée
            var directionAway = (from - nearestAvoid.Location);
            directionAway.Normalize();

            // Point de fuite
            var safePoint = from + (directionAway * (nearestAvoid.Radius + minDistance));

            // Vérifier que le point est navigable
            if (Navigator.CanNavigateFully(from, safePoint))
                return safePoint;

            // Essayer d'autres directions
            for (float angle = 45f; angle <= 315f; angle += 45f)
            {
                var rotated = RotatePoint(directionAway, angle);
                var testPoint = from + (rotated * (nearestAvoid.Radius + minDistance));
                
                if (Navigator.CanNavigateFully(from, testPoint) && !IsInAvoidance(testPoint))
                    return testPoint;
            }

            return from;
        }

        private static WoWPoint RotatePoint(WoWPoint direction, float angleDegrees)
        {
            float angleRadians = angleDegrees * (float)Math.PI / 180f;
            float cos = (float)Math.Cos(angleRadians);
            float sin = (float)Math.Sin(angleRadians);
            
            return new WoWPoint(
                direction.X * cos - direction.Y * sin,
                direction.X * sin + direction.Y * cos,
                direction.Z
            );
        }
    }
}