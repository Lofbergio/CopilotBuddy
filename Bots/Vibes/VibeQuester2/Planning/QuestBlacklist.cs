using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Styx.Helpers;

namespace Bots.Vibes.VibeQuester2.Planning
{
    /// <summary>
    /// Persistent, human-readable record of quests VibeQuester v2 has determined it cannot complete —
    /// so future runs never re-accept them, and a future build with more capability can re-enable them.
    /// This is runtime-LEARNED knowledge (plus optional hand-curated seeds), deliberately kept in its
    /// own JSON file rather than QuestData.db: that DB is a generated artifact an extractor regen would
    /// clobber, and this data must be diffable + hand-editable (curate seeds, add triage notes, remove
    /// an entry to force a retry).
    ///
    /// Re-enablement engine: an entry blocks a quest ONLY while its <see cref="QuestBlacklistEntry.RequiresCapability"/>
    /// is not in <see cref="SupportedCapabilities"/>. Ship the behaviour, add its key below, and every
    /// quest gated on it is silently re-attempted on the next load — no hand-editing, no roach-motel.
    /// </summary>
    public class QuestBlacklist
    {
        // Capability keys THIS build can satisfy. Empty today: v2 has no scripted / use-item-at-location
        // support, so those categories stay blocking. Add a key here the day its behaviour ships and the
        // matching entries auto-retry on next load.
        private static readonly HashSet<string> SupportedCapabilities =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { };

        // The explicit permanent/manual-only class: an entry that no shipped capability will ever clear
        // (needs a human to review + delete). Distinct from a null capability, which used to slip past the
        // re-enable engine and block forever silently — the roach motel this const closes.
        public const string ManualReviewCapability = "manual-review";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        private readonly string _path;
        private readonly Dictionary<int, QuestBlacklistEntry> _entries = new Dictionary<int, QuestBlacklistEntry>();

        public QuestBlacklist()
        {
            _path = Path.Combine(Logging.ApplicationPath, "VibeQuester2-QuestBlacklist.json");
            Load();
        }

        /// <summary>Blocks selection only while nothing in this build satisfies the entry's capability.</summary>
        public bool IsBlocked(int questId)
            => _entries.TryGetValue(questId, out QuestBlacklistEntry e)
               && !SupportedCapabilities.Contains(e.RequiresCapability ?? "");

        public bool TryGet(int questId, out QuestBlacklistEntry entry) => _entries.TryGetValue(questId, out entry);

        /// <summary>Record a learned block (or reinforce an existing one) and persist immediately.</summary>
        public void Record(QuestBlacklistEntry entry)
        {
            if (entry == null || entry.QuestId <= 0) return;
            // No roach motel: an entry with no capability would fail SupportedCapabilities forever AND
            // never be a candidate for re-enable. Default it to the explicit manual-only class instead.
            if (string.IsNullOrEmpty(entry.RequiresCapability))
                entry.RequiresCapability = ManualReviewCapability;
            string now = DateTime.UtcNow.ToString("o");
            if (_entries.TryGetValue(entry.QuestId, out QuestBlacklistEntry existing))
            {
                existing.Occurrences++;
                existing.LastSeen = now;
                // A fresh detection may sharpen the classification; keep the original firstSeen.
                existing.Category = entry.Category ?? existing.Category;
                existing.RequiresCapability = entry.RequiresCapability ?? existing.RequiresCapability;
                existing.Reason = entry.Reason ?? existing.Reason;
                existing.Evidence = entry.Evidence ?? existing.Evidence;
            }
            else
            {
                entry.FirstSeen = now;
                entry.LastSeen = now;
                entry.Occurrences = Math.Max(1, entry.Occurrences);
                _entries[entry.QuestId] = entry;
            }
            Save();
            Logging.Write("[VQ2-Blacklist] q{0} '{1}' recorded: {2} (needs capability '{3}').",
                entry.QuestId, entry.QuestName, entry.Category, entry.RequiresCapability);
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                List<QuestBlacklistEntry> list =
                    JsonSerializer.Deserialize<List<QuestBlacklistEntry>>(File.ReadAllText(_path), JsonOptions);
                if (list == null) return;
                int reenabled = 0;
                foreach (QuestBlacklistEntry e in list)
                {
                    if (e == null || e.QuestId <= 0) continue;
                    if (SupportedCapabilities.Contains(e.RequiresCapability ?? "")) { reenabled++; continue; }
                    _entries[e.QuestId] = e;
                }
                Logging.Write("[VQ2-Blacklist] loaded {0} blocked quest(s){1}.",
                    _entries.Count,
                    reenabled > 0 ? string.Format(", {0} re-enabled by new capability", reenabled) : "");
                if (reenabled > 0) Save();   // self-clean so re-enabled quests don't linger in the file
            }
            catch (Exception ex)
            {
                Logging.Write("[VQ2-Blacklist] load failed ({0}) — starting empty.", ex.Message);
                _entries.Clear();
            }
        }

        private void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(
                    _entries.Values.OrderBy(e => e.QuestId).ToList(), JsonOptions);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(_path)) File.Delete(_path);
                File.Move(tmp, _path);
            }
            catch (Exception ex)
            {
                Logging.Write("[VQ2-Blacklist] save failed: {0}", ex.Message);
            }
        }
    }

    /// <summary>One quest v2 can't complete. Fields grouped by purpose; see QuestBlacklist for the
    /// re-enablement contract that RequiresCapability + DetectedByVersion drive.</summary>
    public class QuestBlacklistEntry
    {
        // --- Identity ---
        public int QuestId { get; set; }
        public string QuestName { get; set; }

        // --- Classification (the re-enablement engine) ---
        public string Category { get; set; }            // use-item | use-item-location | scripted-event | escort | vehicle | gossip-multistep | no-questgiver-flag | unresolved-entity | unsupported-type
        public string RequiresCapability { get; set; }  // named feature that would unblock it — THE re-enable key (never null; "manual-review" = explicit manual-only class)
        public string BlockClass { get; set; }          // "structural" (never works until built) | "conditional" (state-dependent, may re-enable)

        // NOTE: the only enforced disposition is exclusion-from-selection (IsBlocked). There is no
        // "abandon-if-held"/"skip-turnin-defer" behaviour — an Action field describing those was removed
        // rather than left as schema that lies about what the code does. Add it back only WITH the behaviour.

        // --- Evidence (audit — was the classification even right?) ---
        public string Reason { get; set; }
        public string Evidence { get; set; }
        public string Notes { get; set; }               // free-text human triage

        // --- Provenance (staleness + trust) ---
        public string DetectedByVersion { get; set; }
        public string Source { get; set; }              // "learned" | "curated"
        public string FirstSeen { get; set; }
        public string LastSeen { get; set; }
        public int Occurrences { get; set; }

        // --- Structural snapshot (offline analysis without re-deriving from the DB) ---
        public int StartItem { get; set; }
        public int GiverId { get; set; }
        public int EnderId { get; set; }
        public string ObjectiveSummary { get; set; }
        public List<string> ObjectiveDetail { get; set; }
        public int Map { get; set; }
    }
}
