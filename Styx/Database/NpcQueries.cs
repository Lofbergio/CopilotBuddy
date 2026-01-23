#nullable disable
using System;
using System.Data.SQLite;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

namespace Styx.Database
{
    /// <summary>
    /// Provides NPC lookup queries from the SQLite database.
    /// </summary>
    public static class NpcQueries
    {
        #region SQL Commands

        // SELECT * FROM npcs WHERE name = @name LIMIT 1
        private static readonly SQLiteCommand _getNpcByNameCmd;

        // SELECT * FROM npcs WHERE entry = @entry LIMIT 1
        private static readonly SQLiteCommand _getNpcByIdCmd;

        // SELECT * FROM npcs WHERE map = @map AND trainer_class = @class AND trainer_type >= @trainerType 
        // ORDER BY VECTORDISTANCE(x, y, z, @x, @y, @z) LIMIT 10
        private static readonly SQLiteCommand _getNearestTrainerCmd;

        // SELECT * FROM npcs WHERE map = @map AND (flag & @flags) != 0 
        // ORDER BY VECTORDISTANCE(x, y, z, @x, @y, @z) LIMIT 10
        private static readonly SQLiteCommand _getNearestNpcCmd;

        #endregion

        #region Static Constructor

        static NpcQueries()
        {
            // Initialize SQL commands
            _getNpcByNameCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE name = @name LIMIT 1");

            _getNpcByIdCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE entry = @entry LIMIT 1");

            _getNearestTrainerCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE map = @map AND trainer_class = @class AND trainer_type >= @trainerType " +
                "ORDER BY VECTORDISTANCE(x, y, z, @x, @y, @z) LIMIT 10");

            _getNearestNpcCmd = Connection.CreateCommand(
                "SELECT * FROM npcs WHERE map = @map AND (flag & @flags) != 0 " +
                "ORDER BY VECTORDISTANCE(x, y, z, @x, @y, @z) LIMIT 10");
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets an NPC by name.
        /// </summary>
        /// <param name="name">The name to search for.</param>
        /// <returns>The NPC result, or null if not found.</returns>
        public static NpcResult GetNpcByName(string name)
        {
            using var reader = Connection.ExecuteReader(_getNpcByNameCmd, name);
            if (reader.Read())
            {
                return new NpcResult(reader);
            }
            return null;
        }

        /// <summary>
        /// Gets an NPC by entry ID.
        /// </summary>
        /// <param name="id">The entry ID.</param>
        /// <returns>The NPC result, or null if not found.</returns>
        public static NpcResult GetNpcById(uint id)
        {
            using var reader = Connection.ExecuteReader(_getNpcByIdCmd, id);
            if (reader.Read())
            {
                return new NpcResult(reader);
            }
            return null;
        }

        /// <summary>
        /// Gets the nearest trainer of a specific class.
        /// </summary>
        /// <param name="myFaction">The player's faction.</param>
        /// <param name="mapId">The map ID.</param>
        /// <param name="searchLocation">The search location.</param>
        /// <param name="searchClass">The class to search for.</param>
        /// <returns>The nearest trainer, or null if not found.</returns>
        public static NpcResult GetNearestTrainer(WoWFaction myFaction, uint mapId, WoWPoint searchLocation, WoWClass searchClass)
        {
            // Trainer type 5 for level > 10 (class trainer), 0 for low level
            int trainerType = StyxWoW.Me.Level > 10 ? 5 : 0;

            using var reader = Connection.ExecuteReader(_getNearestTrainerCmd,
                mapId,
                (int)searchClass,
                trainerType,
                searchLocation.X,
                searchLocation.Y,
                searchLocation.Z);

            NpcResult result = null;
            while (reader.Read())
            {
                result = new NpcResult(reader);
                // Check faction compatibility
                if (myFaction.RelationTo(new WoWFaction(result.Faction)) >= WoWUnitReaction.Neutral)
                {
                    return result;
                }
            }
            return result;
        }

        /// <summary>
        /// Gets the nearest NPC with specific flags.
        /// </summary>
        /// <param name="myFaction">The player's faction.</param>
        /// <param name="mapId">The map ID.</param>
        /// <param name="searchLocation">The search location.</param>
        /// <param name="npcFlags">The NPC flags to search for.</param>
        /// <returns>The nearest NPC, or null if not found.</returns>
        public static NpcResult GetNearestNpc(WoWFaction myFaction, uint mapId, WoWPoint searchLocation, UnitNPCFlags npcFlags)
        {
            WoWClass myClass = StyxWoW.Me.Class;

            // If looking for class trainer, use specialized query
            if ((npcFlags & UnitNPCFlags.ClassTrainer) != UnitNPCFlags.None)
            {
                return GetNearestTrainer(myFaction, mapId, searchLocation, myClass);
            }

            using var reader = Connection.ExecuteReader(_getNearestNpcCmd,
                mapId,
                (uint)npcFlags,
                searchLocation.X,
                searchLocation.Y,
                searchLocation.Z);

            NpcResult result = null;
            while (reader.Read())
            {
                result = new NpcResult(reader);
                // Check if class trainer matches our class (if applicable) and faction is friendly
                if (((npcFlags & UnitNPCFlags.ClassTrainer) == UnitNPCFlags.None || result.TrainerClass == (int)myClass) &&
                    myFaction.RelationTo(new WoWFaction(result.Faction)) >= WoWUnitReaction.Neutral)
                {
                    return result;
                }
            }
            return result;
        }

        #endregion
    }
}
