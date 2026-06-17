using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Styx.Helpers;

namespace Styx.WoWInternals
{
    /// <summary>
    /// Spell database loaded from Spells.bin file.
    /// This mirrors HB 3.3.5a's Class47 which reads spell names/ranks from a local file
    /// instead of making Lua calls for each spell (which can crash the game).
    /// </summary>
    public static class SpellDb
    {
        private static Dictionary<int, SpellData>? _spells;
        private static bool _initialized;
        private static readonly object _lock = new object();

        /// <summary>
        /// Data structure for spell info from Spells.bin
        /// </summary>
        public class SpellData
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string Rank { get; set; } = "";
        }

        /// <summary>
        /// Initialize the spell database from Spells.bin
        /// </summary>
        public static void Initialize()
        {
            lock (_lock)
            {
                if (_initialized)
                    return;

                _spells = new Dictionary<int, SpellData>();
                
                string spellsPath = Path.Combine(Application.StartupPath, "Spells.bin");
                
                if (!File.Exists(spellsPath))
                {
                    Logging.Write("[SpellDb] Warning: Spells.bin not found at {0}", spellsPath);
                    _initialized = true;
                    return;
                }

                try
                {
                    LoadSpellsFromFile(spellsPath);
                    Logging.Write("[SpellDb] Loaded {0} spells from Spells.bin", _spells.Count);
                }
                catch (Exception ex)
                {
                    Logging.Write("[SpellDb] Error loading Spells.bin: {0}", ex.Message);
                    Logging.WriteException(ex);
                }

                _initialized = true;
            }
        }

        private static void LoadSpellsFromFile(string filePath)
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(filePath), Encoding.ASCII))
            {
                // Read header "SPDB"
                string header = Encoding.ASCII.GetString(reader.ReadBytes(4));
                if (header != "SPDB")
                {
                    throw new Exception("Spells.bin is not in correct format (expected SPDB header)");
                }

                // Read version (should be 1.0f)
                float version = reader.ReadSingle();
                if (version != 1f)
                {
                    Logging.Write("[SpellDb] Warning: Unknown Spells.bin version: {0}", version);
                }

                // Read spell count
                int spellCount = reader.ReadInt32();
                
                // Skip reserved int
                reader.ReadInt32();

                // Read all spells
                for (int i = 0; i < spellCount; i++)
                {
                    var spell = new SpellData
                    {
                        Id = reader.ReadInt32(),
                        Name = reader.ReadString(),
                        Rank = reader.ReadString()
                    };
                    
                    if (_spells != null && !_spells.ContainsKey(spell.Id))
                    {
                        _spells.Add(spell.Id, spell);
                    }
                }
            }
        }

        /// <summary>
        /// Gets a spell by ID
        /// </summary>
        public static SpellData? GetSpell(int id)
        {
            if (!_initialized)
                Initialize();

            if (_spells != null && _spells.TryGetValue(id, out SpellData? spell))
            {
                return spell;
            }
            return null;
        }

        /// <summary>
        /// Gets spell name by ID. Returns empty string if not found.
        /// </summary>
        public static string GetSpellName(int id)
        {
            var spell = GetSpell(id);
            return spell?.Name ?? "";
        }

        /// <summary>
        /// Gets spell rank by ID. Returns empty string if not found.
        /// </summary>
        public static string GetSpellRank(int id)
        {
            var spell = GetSpell(id);
            return spell?.Rank ?? "";
        }

        /// <summary>
        /// Checks if a spell exists in the database
        /// </summary>
        public static bool HasSpell(int id)
        {
            if (!_initialized)
                Initialize();

            return _spells != null && _spells.ContainsKey(id);
        }

        /// <summary>
        /// Checks if a spell with the given name exists in the database. Used as the
        /// fallback for SpellManager.HasSpell(string) so passive talents (Vengeance,
        /// Meditation, Shamanism, Moonfury, etc.) that the player has not actively
        /// cast can still be detected for role identification. Ported from HB 4.3.4
        /// SpellManager.HasSpell (line 16) which fell back to RawSpells — CopilotBuddy's
        /// SpellManager.RawSpells was a broken clone of _knownSpells, so we look up the
        /// full Spells.bin database directly. O(N) but called only when the known-spell
        /// cache misses, and N=49816 in the worst case.
        /// </summary>
        public static bool HasSpellByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (!_initialized)
                Initialize();
            if (_spells == null)
                return false;
            foreach (var spell in _spells.Values)
            {
                if (string.Equals(spell.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the total number of spells in the database
        /// </summary>
        public static int Count
        {
            get
            {
                if (!_initialized)
                    Initialize();
                return _spells?.Count ?? 0;
            }
        }
    }
}
