// RestGovernor — dynamic class/level/death-aware rest thresholds, folded in from the QuestGovernor
// drop-in plugin so the Vibe bots own this instead of depending on an external plugin.
//
// Singular eats/drinks off SingularSettings.MinHealth/MinMana (Helpers/Rest.cs). This governor computes
// those per class+level+death-caution and writes them via reflection (Singular lives in a separate
// runtime-compiled assembly we can't reference). It also exposes the *live* values so SafeRest backs off
// on exactly the band Singular sits at — eliminating the rest/roam thrash that came from SafeRest using
// its own fixed thresholds that disagreed with the dynamic ones.
//
// Coexistence: if the standalone QuestGovernor plugin is still enabled it stays the writer (no double-write,
// and we don't silently disable a user-installed plugin); we then just read the live values back. Remove the
// plugin and this governor seamlessly becomes the writer.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Styx;                          // WoWPowerType
using Styx.Combat.CombatRoutine;     // WoWClass
using Styx.Helpers;                  // Logging, CharacterSettings
using Styx.Logic.Combat;             // RoutineManager
using Styx.Plugins;                  // PluginManager
using Styx.WoWInternals;             // ObjectManager
using Styx.WoWInternals.WoWObjects;  // LocalPlayer

namespace Bots.Vibes.Shared
{
    public sealed class RestGovernor
    {
        private static readonly HashSet<WoWClass> SelfHeal = new()
        { WoWClass.Priest, WoWClass.Paladin, WoWClass.Shaman, WoWClass.Druid, WoWClass.Warlock };
        private static readonly HashSet<WoWClass> SquishyNoHeal = new()
        { WoWClass.Mage, WoWClass.Rogue, WoWClass.Warrior };
        private static readonly HashSet<WoWClass> PetClass = new()
        { WoWClass.Hunter, WoWClass.Warlock };

        // caution: 0..6, each step = +3% to rest thresholds. Up on death, down on clean streaks.
        // Kept deliberately gentle: a steep slope (+12%/death at the old +2 step × 6%) spiralled a
        // low-level caster into 80% mana / 90% HP rest gates after a couple deaths — it then re-rested
        // after a single Lightning Bolt, barely killed, got caught at low mana, died, and ratcheted the
        // gates higher still. +3%/death + lower caps below keep the band sane through a rough patch.
        private const int CautionStepPct = 3;
        private const int CautionMax = 6;
        private const int CleanFightsToRelax = 8;

        private int _caution;
        private int _cleanFights;
        private bool _wasDead;
        private bool _wasCombat;
        private int _deathsAtFightStart;
        private int _deaths;
        private int _lastH = int.MinValue, _lastM = int.MinValue;

        private bool _reflectionResolved;
        private bool _reflectionFailed;
        private object _routineSettings;
        private PropertyInfo _minHealthProp, _minManaProp;

        // Restock seeding (FoodAmount/DrinkAmount) — opt-in. VibeGrinder seeds via GrindAreaSynthesizer, so
        // only VibeQuester (which has no synthesizer) asks the governor to do it.
        private readonly bool _seedRestock;
        private bool _seeded;

        public RestGovernor(bool seedRestock = false)
        {
            _seedRestock = seedRestock;
        }

        // Live thresholds consumers (SafeRest) read. Seeded with the old fixed SafeRest values so a tick
        // before the first Pulse still behaves; overwritten with live/computed values from Pulse on.
        private int _minHealth = 55, _minMana = 45;
        public int MinHealth => _minHealth;
        public int MinMana => _minMana;

        public void Pulse(LocalPlayer me)
        {
            try
            {
                if (me == null) return;
                if (_seedRestock && !_seeded) SeedRestockAmounts(me);
                if (!_reflectionResolved && !_reflectionFailed) ResolveRoutineSettings();

                TrackDeathsAndStreaks(me);                 // keep caution current so we're ready if we own writing
                (int h, int m) = ComputeThresholds(me);

                bool weWrite = _reflectionResolved && !PluginOwnsWriting();
                if (weWrite) WriteThresholds(h, m);

                // Expose the live truth: what we (or the plugin) actually wrote into Singular. Non-Singular
                // routines have no readable thresholds, so fall back to our own formula.
                if (_reflectionResolved)
                {
                    _minHealth = ReadInt(_minHealthProp, h);
                    _minMana = ReadInt(_minManaProp, m);
                }
                else
                {
                    _minHealth = h;
                    _minMana = m;
                }
            }
            catch (Exception ex)
            {
                Log("Pulse error: " + ex.Message);   // must never break the bot
            }
        }

        // ---- caution feedback loop ----

        private void TrackDeathsAndStreaks(LocalPlayer me)
        {
            bool dead = me.IsDead || me.IsGhost;
            if (dead && !_wasDead)
            {
                _deaths++;
                _caution = Math.Min(_caution + 1, CautionMax);   // +1, not +2 — one death shouldn't swing the gates 12%
                _cleanFights = 0;
                Log($"death #{_deaths} — caution up to {_caution}");
            }
            _wasDead = dead;

            bool inCombat = me.Combat;
            if (inCombat && !_wasCombat) _deathsAtFightStart = _deaths;
            else if (!inCombat && _wasCombat)
            {
                if (_deaths == _deathsAtFightStart && ++_cleanFights >= CleanFightsToRelax)
                {
                    _cleanFights = 0;
                    if (_caution > 0) { _caution--; Log($"clean streak — caution down to {_caution}"); }
                }
            }
            _wasCombat = inCombat;
        }

        // ---- food/drink restock seeding (so the engine's buy logic engages once affordable) ----

        private void SeedRestockAmounts(LocalPlayer me)
        {
            _seeded = true;
            try
            {
                var cs = CharacterSettings.Instance;
                if (cs == null) { _seeded = false; return; }   // not ready yet; retry next pulse
                bool usesMana = me.PowerType == WoWPowerType.Mana || me.Class == WoWClass.Druid;
                bool changed = false;
                if (cs.FoodAmount == 0) { cs.FoodAmount = 20; changed = true; }
                if (usesMana && cs.DrinkAmount == 0) { cs.DrinkAmount = 20; changed = true; }
                if (changed)
                    Log($"seeded restock amounts (Food={cs.FoodAmount}, Drink={cs.DrinkAmount}) — " +
                        "buy engages once you can afford it at a Food vendor");
            }
            catch (Exception ex) { Log("seed failed: " + ex.Message); }
        }

        // ---- per-class / per-level threshold formula ----

        private (int h, int m) ComputeThresholds(LocalPlayer me)
        {
            WoWClass cls = me.Class;
            int lvl = me.Level;
            double lf = Math.Clamp((lvl - 1) / 69.0, 0.0, 1.0);   // 0 at lvl1 -> 1 at lvl70+
            bool usesMana = me.PowerType == WoWPowerType.Mana || cls == WoWClass.Druid;
            double caution = _caution * CautionStepPct;

            // Health: leveling is safety-first — enter every fight near full. HIGH at low level (small HP
            // pool + no defensives → a mob can spike you from comfortable to dead), tapering DOWN toward cap
            // (gear/cooldowns let you fight lower). Self-healers top off mid-fight so carry a little less;
            // squishy no-heal classes carry more; pet classes less (the pet soaks). NOT the old inverted
            // "rest minimally at low level" curve — that put a L15 at 35% HP, which is giga-lethal solo.
            double health = 72 - 10 * lf;
            if (SquishyNoHeal.Contains(cls)) health += 8;
            if (SelfHeal.Contains(cls)) health -= 5;
            if (PetClass.Contains(cls)) health -= 10;
            health = Math.Clamp(health + caution, 45, 82);   // cap < old 90: never gate on near-full HP

            // Mana: zero for rage/energy/rune classes (never rest for mana). Casters drink up to enter fights
            // with a full bar (high at low level, easing toward cap); self-healers a touch higher.
            double mana = 0;
            if (usesMana)
            {
                mana = 52 - 12 * lf;
                if (SelfHeal.Contains(cls)) mana += 5;
                mana = Math.Clamp(mana + caution, 38, 68);   // cap < old 80: a caster that gates at 80% mana re-rests after one cast
            }

            return ((int)Math.Round(health), (int)Math.Round(mana));
        }

        private void WriteThresholds(int h, int m)
        {
            if (h == _lastH && m == _lastM) return;   // only write on change (avoids settings-save spam)
            if (SetInt(_minHealthProp, h) && SetInt(_minManaProp, m))
            {
                _lastH = h; _lastM = m;
                Log($"thresholds → MinHealth={h}, MinMana={m}  (caution={_caution})");
            }
        }

        // ---- routine rest-threshold reflection bridge ----
        // Singular and GoodVibes each expose a settings singleton with public int MinHealth/MinMana that
        // the routine rests on; they live in separate runtime-compiled assemblies we can't reference. Bind
        // to whichever is active so SafeRest backs off on exactly the band the routine actually rests at.

        private void ResolveRoutineSettings()
        {
            try
            {
                var routine = RoutineManager.Current;
                if (routine == null) return;   // routine not loaded yet; try again next pulse
                Assembly asm = routine.GetType().Assembly;
                if (TryBindSettings(asm, "Singular.Settings.SingularSettings", "Singular")) return;
                if (TryBindSettings(asm, "GoodVibes.GVSettings", "GoodVibes")) return;
                Fail("active routine exposes no MinHealth/MinMana settings");
            }
            catch (Exception ex) { Fail("reflection error: " + ex.Message); }
        }

        // Bind to <typeName>.Instance.MinHealth/MinMana (public instance int props) if present.
        private bool TryBindSettings(Assembly asm, string typeName, string label)
        {
            Type t = asm.GetType(typeName);
            if (t == null) return false;
            object inst = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                       ?? t.GetField("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            PropertyInfo hp = t.GetProperty("MinHealth", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo mp = t.GetProperty("MinMana", BindingFlags.Public | BindingFlags.Instance);
            if (inst == null || hp == null || mp == null) return false;
            _routineSettings = inst;
            _minHealthProp = hp;
            _minManaProp = mp;
            _reflectionResolved = true;
            Log("bound to " + label + " settings — governing rest thresholds");
            return true;
        }

        // True if the standalone QuestGovernor plugin is loaded and enabled — it stays the writer then.
        private static bool PluginOwnsWriting()
        {
            var plugins = PluginManager.Plugins;
            if (plugins == null) return false;
            return plugins.Any(p => p.Enabled &&
                                    string.Equals(p.Name, "QuestGovernor", StringComparison.OrdinalIgnoreCase));
        }

        private bool SetInt(PropertyInfo prop, int value)
        {
            try { prop.SetValue(_routineSettings, value); return true; }
            catch (Exception ex) { Log("set failed: " + ex.Message); return false; }
        }

        private int ReadInt(PropertyInfo prop, int fallback)
        {
            try { return Convert.ToInt32(prop.GetValue(_routineSettings)); }
            catch { return fallback; }
        }

        private void Fail(string why)
        {
            _reflectionFailed = true;
            Log("not governing Singular — " + why + " (SafeRest uses computed thresholds)");
        }

        private void Log(string msg) =>
            Logging.Write(System.Drawing.Color.Gold, "[RestGovernor] " + msg);
    }
}
