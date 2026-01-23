using System;
using System.Linq;
using GreenMagic;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Patchables;
using Styx.WoWInternals.WoWObjects;

namespace Styx.WoWInternals.World
{
    /// <summary>
    /// Fournit des méthodes pour interact avec le monde du jeu.
    /// WoW 3.3.5a build 12340.
    /// Ported from HB 4.3.4 with 3.3.5a offsets.
    /// </summary>
    public static class GameWorld
    {
        /// <summary>
        /// HB 3.3.5a exact CGWorldFrameHitFlags enum.
        /// Values verified from HB 3.3.5 obfuscated source (line 420-431).
        /// </summary>
        [Flags]
        public enum CGWorldFrameHitFlags : uint
        {
            HitTestNothing = 0,
            HitTestBoundingModels = 1,         // 0x1
            HitTestWMO = 16,                   // 0x10 - In WotLK, WMO is 0x10 (not 0x20 like Cata)
            HitTestUnknown = 64,               // 0x40
            HitTestGround = 256,               // 0x100
            HitTestLiquid = 65536,             // 0x10000
            HitTestLiquid2 = 131072,           // 0x20000
            HitTestMovableObjects = 1048576,   // 0x100000
            HitTestLOS = 1048593,              // 0x100011 = HitTestMovableObjects | HitTestWMO | HitTestBoundingModels
            HitTestGroundAndStructures = 1048849, // 0x100111 = HitTestMovableObjects | HitTestGround | HitTestWMO | HitTestBoundingModels
        }
        
        /// <summary>
        /// Legacy flags alias for backward compatibility.
        /// </summary>
        [Flags]
        public enum TraceLineHitFlags : uint
        {
            Nothing = 0,
            Terrain = 0x1,
            WMO = 0x10,
            Doodad = 0x8,
            Liquid = 0x10000,
            All = 0x100111
        }

        private static TraceLineHitFlags MapFlags(CGWorldFrameHitFlags flags)
        {
            TraceLineHitFlags mapped = TraceLineHitFlags.Nothing;

            if ((flags & CGWorldFrameHitFlags.HitTestGround) != 0)
                mapped |= TraceLineHitFlags.Terrain;
            if ((flags & CGWorldFrameHitFlags.HitTestWMO) != 0)
                mapped |= TraceLineHitFlags.WMO;
            if ((flags & CGWorldFrameHitFlags.HitTestLiquid) != 0 || (flags & CGWorldFrameHitFlags.HitTestLiquid2) != 0)
                mapped |= TraceLineHitFlags.Liquid;
            if ((flags & CGWorldFrameHitFlags.HitTestBoundingModels) != 0)
                mapped |= TraceLineHitFlags.Doodad;

            if (mapped == TraceLineHitFlags.Nothing && flags != CGWorldFrameHitFlags.HitTestNothing)
                mapped = TraceLineHitFlags.All;

            return mapped;
        }

        /// <summary>
        /// Vérifie si deux points sont en ligne de vue (line of sight).
        /// </summary>
        public static bool IsInLineOfSight(WoWPoint from, WoWPoint to)
        {
            // Ajuste la hauteur pour la tête du personnage
            from = new WoWPoint(from.X, from.Y, from.Z + 1.132f);
            to = new WoWPoint(to.X, to.Y, to.Z + 1.132f);
            
            return !TraceLine(from, to, TraceLineHitFlags.All);
        }

        /// <summary>
        /// Vérifie si deux points sont en ligne de vue pour les sorts.
        /// Similaire à IsInLineOfSight mais avec des flags différents pour les sorts.
        /// Ported from HB 4.3.4.
        /// </summary>
        public static bool IsInLineOfSpellSight(WoWPoint from, WoWPoint to)
        {
            // Pour les sorts, on utilise les mêmes flags que IsInLineOfSight
            // La différence dans HB 4.3.4 est HitTestSpellLoS vs HitTestLOS
            // mais dans notre implémentation navmesh, c'est équivalent
            return IsInLineOfSight(from, to);
        }

        /// <summary>
        /// Trace une ligne entre deux points pour détecter les collisions.
        /// </summary>
        public static bool TraceLine(WoWPoint from, WoWPoint to, TraceLineHitFlags flags)
        {
            return TraceLine(from, to, flags, out _);
        }

        public static bool TraceLine(WoWPoint from, WoWPoint to, CGWorldFrameHitFlags flags)
        {
            return TraceLine(from, to, MapFlags(flags), out _);
        }

        /// <summary>
        /// Trace une ligne entre deux points et retourne le point de collision.
        /// Utilise Tripper.Navigation.Raycast pour le navmesh.
        /// Note: Ce n'est pas identique au TraceLine WoW natif (terrain/WMO), 
        /// mais utilise le navmesh pour détecter les obstacles de navigation.
        /// </summary>
        public static bool TraceLine(WoWPoint from, WoWPoint to, TraceLineHitFlags flags, out WoWPoint hitPoint)
        {
            hitPoint = to;
            
            // Pour les flags terrain/WMO, utiliser le navmesh raycast
            if ((flags & (TraceLineHitFlags.Terrain | TraceLineHitFlags.WMO)) != 0)
            {
                // Utiliser le Navigator de Styx qui wrape Tripper
                return Styx.Logic.Pathing.Navigator.Raycast(from, to, out hitPoint);
            }
            
            // Pas de collision détectée pour autres flags
            return false;
        }

        public static bool TraceLine(WoWPoint from, WoWPoint to, CGWorldFrameHitFlags flags, out WoWPoint hitPoint)
        {
            return TraceLine(from, to, MapFlags(flags), out hitPoint);
        }

        /// <summary>
        /// Trace plusieurs lignes en une seule opération (optimisé).
        /// </summary>
        public static void MassTraceLine(WorldLine[] lines, TraceLineHitFlags flag, out bool[] hitResults)
        {
            MassTraceLine(lines, Enumerable.Repeat(flag, lines.Length).ToArray(), out hitResults);
        }

        public static void MassTraceLine(WorldLine[] lines, CGWorldFrameHitFlags flag, out bool[] hitResults)
        {
            MassTraceLine(lines, Enumerable.Repeat(flag, lines.Length).ToArray(), out hitResults);
        }

        /// <summary>
        /// Trace plusieurs lignes avec flags différents.
        /// </summary>
        public static void MassTraceLine(WorldLine[] lines, TraceLineHitFlags[] flags, out bool[] hitResults)
        {
            MassTraceLine(lines, flags, out hitResults, out _);
        }

        public static void MassTraceLine(WorldLine[] lines, CGWorldFrameHitFlags[] flags, out bool[] hitResults)
        {
            MassTraceLine(lines, flags, out hitResults, out _);
        }

        /// <summary>
        /// Trace plusieurs lignes et retourne les points de collision.
        /// </summary>
        public static void MassTraceLine(WorldLine[] lines, TraceLineHitFlags flag, out bool[] hitResults, out WoWPoint[] hitPoints)
        {
            MassTraceLine(lines, Enumerable.Repeat(flag, lines.Length).ToArray(), out hitResults, out hitPoints);
        }

        public static void MassTraceLine(WorldLine[] lines, CGWorldFrameHitFlags flag, out bool[] hitResults, out WoWPoint[] hitPoints)
        {
            MassTraceLine(lines, Enumerable.Repeat(flag, lines.Length).ToArray(), out hitResults, out hitPoints);
        }

        /// <summary>
        /// Trace plusieurs lignes avec flags différents et retourne les points de collision.
        /// Utilise TraceLine en boucle (pas optimal mais fonctionnel).
        /// </summary>
        public static void MassTraceLine(WorldLine[] lines, TraceLineHitFlags[] flags, out bool[] hitResults, out WoWPoint[] hitPoints)
        {
            if (flags.Length != lines.Length)
                throw new ArgumentException("flags.Length is not the same as lines.Length!");

            hitResults = new bool[lines.Length];
            hitPoints = new WoWPoint[lines.Length];

            for (int i = 0; i < lines.Length; i++)
            {
                hitResults[i] = TraceLine(lines[i].Start, lines[i].End, flags[i], out hitPoints[i]);
            }
        }

        public static void MassTraceLine(WorldLine[] lines, CGWorldFrameHitFlags[] flags, out bool[] hitResults, out WoWPoint[] hitPoints)
        {
            if (flags.Length != lines.Length)
                throw new ArgumentException("flags.Length is not the same as lines.Length!");

            TraceLineHitFlags[] mapped = new TraceLineHitFlags[flags.Length];
            for (int i = 0; i < flags.Length; i++)
                mapped[i] = MapFlags(flags[i]);

            MassTraceLine(lines, mapped, out hitResults, out hitPoints);
        }
    }
}
