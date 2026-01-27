#nullable disable
using System.Data.SQLite;
using Styx.Logic.Pathing;

namespace Styx.Database
{
    /// <summary>
    /// Represents an NPC result from the SQLite database.
    /// </summary>
    public class NpcResult
    {
        /// <summary>
        /// Creates an NpcResult from a SQLiteDataReader (HB API).
        /// </summary>
        internal NpcResult(SQLiteDataReader reader)
        {
            Entry = reader.GetInt32(reader.GetOrdinal("entry"));
            Name = reader.GetString(reader.GetOrdinal("name"));
            if (!reader.IsDBNull(reader.GetOrdinal("title")))
            {
                Title = reader.GetString(reader.GetOrdinal("title"));
            }
            X = reader.GetFloat(reader.GetOrdinal("x"));
            Y = reader.GetFloat(reader.GetOrdinal("y"));
            Z = reader.GetFloat(reader.GetOrdinal("z"));
            NpcFlags = (uint)reader.GetInt32(reader.GetOrdinal("flag"));
            Faction = (uint)reader.GetInt32(reader.GetOrdinal("faction"));
            MapId = reader.GetInt32(reader.GetOrdinal("map"));
            TrainerType = reader.GetInt32(reader.GetOrdinal("trainer_type"));
            TrainerClass = reader.GetInt32(reader.GetOrdinal("trainer_class"));
        }

        /// <summary>
        /// Gets or sets the NPC entry ID.
        /// </summary>
        public int Entry { get; set; }

        /// <summary>
        /// Gets or sets the NPC name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the NPC title.
        /// </summary>
        public string Title { get; set; }

        /// <summary>
        /// Gets or sets the X coordinate.
        /// </summary>
        public float X { get; set; }

        /// <summary>
        /// Gets or sets the Y coordinate.
        /// </summary>
        public float Y { get; set; }

        /// <summary>
        /// Gets or sets the Z coordinate.
        /// </summary>
        public float Z { get; set; }

        /// <summary>
        /// Gets or sets the NPC flags.
        /// </summary>
        public uint NpcFlags { get; set; }

        /// <summary>
        /// Gets or sets the faction ID.
        /// </summary>
        public uint Faction { get; set; }

        /// <summary>
        /// Gets or sets the map ID.
        /// </summary>
        public int MapId { get; set; }

        /// <summary>
        /// Gets or sets the trainer type.
        /// </summary>
        public int TrainerType { get; set; }

        /// <summary>
        /// Gets or sets the trainer class.
        /// </summary>
        public int TrainerClass { get; set; }

        /// <summary>
        /// Gets the location as a WoWPoint.
        /// </summary>
        public WoWPoint Location => new WoWPoint(X, Y, Z);
    }
}
