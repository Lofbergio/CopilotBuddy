// Live verification bench for hardcoded 3.3.5a statics.
//
// The static sweep (Tools/audit_pointers.py) can prove a CODE address wrong (no prologue + no call
// site) but it CANNOT settle a DATA address: statics reached via base+register never show a direct
// xref, so "unreferenced" is only a suspicion. This plugin settles them the only way that works —
// read the address in-game and compare against an independent oracle (Lua, or the object manager).
//
// Every check reports PASS / FAIL / SKIP with the oracle value beside the memory value, so the log
// alone is enough to adjudicate. A FAIL triggers a window scan that reports the addresses which DO
// hold the expected value — that is how the old FocusGuid bug was pinned to 0xBD07D0.
//
// Run: click "Run Offset Doctor" in the Plugins UI, or from in-game  /run ODOC = 1
namespace PluginOffsetDoctor
{
    using GreenMagic;
    using Styx;
    using Styx.Helpers;
    using Styx.Plugins.PluginClass;
    using Styx.WoWInternals;
    using Styx.WoWInternals.WoWObjects;

    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;

    public class OffsetDoctor : HBPlugin
    {
        public override string Name { get { return "Offset Doctor"; } }
        public override string Author { get { return "vibes"; } }
        public override Version Version { get { return new Version(2, 0, 0, 0); } }

        public override bool WantButton { get { return true; } }
        public override string ButtonText { get { return "Run Offset Doctor"; } }
        public override void OnButtonPress() { RunSafely(); }

        private static DateTime _nextPoll = DateTime.MinValue;
        private int _pass, _fail, _skip;

        public override void Pulse()
        {
            if (DateTime.UtcNow < _nextPoll) return;
            _nextPoll = DateTime.UtcNow.AddMilliseconds(500);
            if (LuaStr("return tostring(ODOC or 0)") != "1") return;
            Lua.DoString("ODOC = 0");
            RunSafely();
        }

        // ---------------------------------------------------------------- logging

        private static void L(string fmt, params object[] a)
        {
            Logging.Write(Color.FromName("Gold"), "[OffsetDoctor] " + fmt, a);
        }

        private static void LBad(string fmt, params object[] a)
        {
            Logging.Write(Color.FromName("Red"), "[OffsetDoctor] " + fmt, a);
        }

        private void Pass(string name, uint addr, string detail)
        {
            _pass++;
            L("PASS  {0,-22} 0x{1:X6}  {2}", name, addr, detail);
        }

        // A wrong offset must be LOUD -- a silently-latched bad read is one nobody ever fixes.
        private void Fail(string name, uint addr, string detail)
        {
            _fail++;
            LBad("FAIL  {0,-22} 0x{1:X6}  {2}", name, addr, detail);
        }

        // SKIP is NOT a pass. It means the world did not present the state needed to judge, and the
        // run must be repeated under the stated condition.
        private void Skip(string name, uint addr, string why)
        {
            _skip++;
            L("SKIP  {0,-22} 0x{1:X6}  needs: {2}", name, addr, why);
        }

        // ---------------------------------------------------------------- helpers

        private static Memory Wow { get { return ObjectManager.Wow; } }

        private static string LuaStr(string lua)
        {
            try { return Lua.GetReturnVal<string>(lua, 0); }
            catch { return null; }
        }

        private static ulong LuaGuid(string unit)
        {
            // UnitGUID returns "0x0000000000000000"; nil for a missing unit.
            string s = LuaStr(string.Format("return tostring(UnitGUID('{0}') or 0)", unit));
            if (string.IsNullOrEmpty(s) || s == "0" || s == "nil") return 0UL;
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            ulong v;
            return ulong.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) ? v : 0UL;
        }

        private static long LuaNum(string lua)
        {
            string s = LuaStr(lua);
            if (string.IsNullOrEmpty(s)) return long.MinValue;
            double d;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return long.MinValue;
            return (long)d;
        }

        private static uint RdU32(uint a) { try { return Wow.Read<uint>(a); } catch { return 0U; } }
        private static ulong RdU64(uint a) { try { return Wow.Read<ulong>(a); } catch { return 0UL; } }

        // When a read disagrees with the oracle, report every address in the window that DOES hold
        // the expected value. Turns "wrong" into "here is the right one".
        private string ScanFor(ulong expected, uint centre, int radius, bool eightByte)
        {
            var hits = new List<string>();
            uint lo = centre > (uint)radius ? centre - (uint)radius : 0U;
            uint hi = centre + (uint)radius;
            for (uint a = lo; a <= hi && hits.Count < 8; a += 4)
            {
                ulong v = eightByte ? RdU64(a) : RdU32(a);
                if (v == expected)
                    hits.Add(string.Format("0x{0:X6}({1:+#;-#;+0})", a, (long)a - centre));
            }
            return hits.Count == 0
                ? "no address in +/-0x" + radius.ToString("X") + " holds the expected value"
                : "expected value found at " + string.Join(", ", hits.ToArray());
        }

        private void RunSafely()
        {
            try { Run(); }
            catch (Exception ex) { LBad("exception: {0}", ex); }
        }

        // ---------------------------------------------------------------- the run

        private void Run()
        {
            LocalPlayer me = StyxWoW.Me;
            if (Wow == null || me == null || !me.IsValid) { LBad("not in game"); return; }

            _pass = _fail = _skip = 0;
            L("================= run start =================");
            L("char {0}, level {1}, map {2}. SKIP lines list the state you must arrange, then re-run.",
                me.Name, me.Level, me.MapId);

            CheckNetStats();
            CheckPartyGuids();
            CheckRaidPtrs();
            CheckQuestLogCount();
            CheckLandmarks();
            CheckMirrorTimers();
            CheckPendingCursorSpell();
            CheckFactionStanding();
            CheckForcedReactions();
            CheckMerchant();
            CheckShownQuestId();

            L("================= run end: {0} pass, {1} FAIL, {2} skip =================", _pass, _fail, _skip);
            if (_skip > 0)
                L("Re-run after arranging the SKIP conditions -- a skip is not a pass.");
        }

        // ---- NetStats: chain 0xC7B1F4 -> +11860. Oracle: GetNetStats() latency. --------------

        private void CheckNetStats()
        {
            const uint Base = 0x00C79CF4;   // client connection; sub_6B0970 reads exactly this
            const uint Sub = 11860;
            uint baseVal = RdU32(Base);
            if (baseVal == 0U) { Fail("NetStats.base", Base, "base pointer reads 0 (dead or wrong)"); return; }

            long luaLatency = LuaNum("return select(3, GetNetStats())");
            if (luaLatency == long.MinValue) { Skip("NetStats", Base, "GetNetStats() unavailable"); return; }

            // The struct starts with uint[16] latencies + index + count, then byte counters.
            uint latencyCount = RdU32(baseVal + Sub + 68);
            uint sent = RdU32(baseVal + Sub + 72);
            uint recv = RdU32(baseVal + Sub + 76);
            bool sane = latencyCount <= 16U && (sent > 0U || recv > 0U);
            string d = string.Format("chain->0x{0:X6}+{1}: latencyCount={2} sent={3} recv={4}; Lua latency={5}ms",
                baseVal, Sub, latencyCount, sent, recv, luaLatency);
            if (sane) Pass("NetStats", Base, d);
            else Fail("NetStats", Base, d + "  (counters implausible -- struct offset or base wrong)");
        }

        // ---- Party GUIDs. Two rival addresses; the log decides. ------------------------------

        private void CheckPartyGuids()
        {
            // 0xBD1948 is what LocalPlayer actually reads (75 xrefs in the client);
            // 0xBD1DD8 was an unused constant in the same file (0 xrefs). Both tested.
            CheckGuidTable("PartyGuids.live", 0x00BD1948, false);
            CheckGuidTable("PartyGuids.rival", 0x00BD1DD8, false);
        }

        private void CheckGuidTable(string name, uint baseAddr, bool indirect)
        {
            ulong p1 = LuaGuid("party1");
            if (p1 == 0UL) { Skip(name, baseAddr, "be in a PARTY (not raid) with >=1 member"); return; }

            var oracle = new List<ulong>();
            for (int i = 1; i <= 4; i++)
            {
                ulong g = LuaGuid("party" + i);
                if (g != 0UL) oracle.Add(g);
            }

            int matched = 0;
            var seen = new List<string>();
            for (int slot = 0; slot < 5; slot++)
            {
                ulong v = RdU64((uint)(baseAddr + slot * 8));
                seen.Add("0x" + v.ToString("X16"));
                if (v != 0UL && oracle.Contains(v)) matched++;
            }

            string d = string.Format("{0}/{1} party GUIDs matched; slots [{2}]",
                matched, oracle.Count, string.Join(" ", seen.ToArray()));
            if (matched == oracle.Count && matched > 0) Pass(name, baseAddr, d);
            else Fail(name, baseAddr, d + "  |  " + ScanFor(p1, baseAddr, 0x800, true));
        }

        // ---- Raid: table of POINTERS; each points at a GUID. ---------------------------------

        private void CheckRaidPtrs()
        {
            long n = LuaNum("return GetNumRaidMembers()");
            if (n <= 0) { Skip("RaidPtrs", 0x00BEB568, "be in a RAID (GetNumRaidMembers() > 0)"); return; }

            CheckRaidTable("RaidPtrs.live", 0x00BEB568, (int)n);
            CheckRaidTable("RaidPtrs.rival", 0x00BECFC8, (int)n);
        }

        private void CheckRaidTable(string name, uint baseAddr, int count)
        {
            var oracle = new List<ulong>();
            for (int i = 1; i <= count && i <= 40; i++)
            {
                ulong g = LuaGuid("raid" + i);
                if (g != 0UL) oracle.Add(g);
            }
            if (oracle.Count == 0) { Skip(name, baseAddr, "raid members with resolvable GUIDs"); return; }

            int matched = 0;
            for (int slot = 0; slot < 40; slot++)
            {
                uint p = RdU32((uint)(baseAddr + slot * 4));
                if (p == 0U) continue;
                ulong g = RdU64(p);
                if (g != 0UL && oracle.Contains(g)) matched++;
            }

            string d = string.Format("{0}/{1} raid GUIDs resolved through the pointer table", matched, oracle.Count);
            if (matched >= oracle.Count) Pass(name, baseAddr, d);
            else Fail(name, baseAddr, d + "  (pointer table does not resolve to the raid roster)");
        }

        // Name cache: SETTLED, no check needed. 0x00C5D940 has zero references anywhere in the
        // client (Ghidra, indirect included) and the NameCache* trio had no readers -- a
        // Honorbuddy-4.x construct that does not exist in 3.3.5a. Names resolve through WoWCache.

        // ---- Quest log count. Oracle: GetNumQuestLogEntries(). -------------------------------

        private void CheckQuestLogCount()
        {
            const uint Addr = 12729040U; // 0xC23AD0
            long lua = LuaNum("return (GetNumQuestLogEntries())");
            if (lua == long.MinValue) { Skip("QuestLog.count", Addr, "GetNumQuestLogEntries() unavailable"); return; }
            // An oracle of 0 proves nothing -- a dead address reads 0 too. Demand a discriminating value.
            if (lua == 0) { Skip("QuestLog.count", Addr, "PICK UP A QUEST first (oracle 0 matches any dead address)"); return; }

            uint mem = RdU32(Addr);
            string d = string.Format("memory={0}, Lua={1}", mem, lua);
            if ((long)mem == lua) Pass("QuestLog.count", Addr, d);
            else Fail("QuestLog.count", Addr, d + "  |  " + ScanFor((ulong)lua, Addr, 0x400, false));
        }

        // ---- Landmarks. Oracle: GetNumMapLandmarks(). ----------------------------------------

        private void CheckLandmarks()
        {
            const uint Addr = 12488416U; // 0xBE8EE0
            long lua = LuaNum("return (GetNumMapLandmarks())");
            if (lua == long.MinValue) { Skip("Landmarks.count", Addr, "GetNumMapLandmarks() unavailable"); return; }
            if (lua == 0) { Skip("Landmarks.count", Addr, "a zone WITH landmarks (open the world map there)"); return; }

            uint mem = RdU32(Addr);
            string d = string.Format("memory={0}, Lua={1}", mem, lua);
            if ((long)mem == lua) Pass("Landmarks.count", Addr, d);
            else Fail("Landmarks.count", Addr, d + "  |  " + ScanFor((ulong)lua, Addr, 0x400, false));
        }

        // ---- Mirror timers (breath/fatigue). Oracle: the timer being ACTIVE. -----------------

        private void CheckMirrorTimers()
        {
            const uint Addr = 12389248U; // 0xBD0B80
            string type = LuaStr("return tostring(select(1, GetMirrorTimerInfo(1)) or 'UNKNOWN')");
            if (string.IsNullOrEmpty(type) || type == "UNKNOWN" || type == "nil")
            { Skip("MirrorTimers", Addr, "an ACTIVE mirror timer (swim underwater until breath shows)"); return; }

            long luaMax = LuaNum("return (select(2, GetMirrorTimerInfo(1)) or -1)");
            uint mem = RdU32(Addr);
            string d = string.Format("memory[0]={0}, Lua type='{1}' max={2}", mem, type, luaMax);
            if (mem != 0U) Pass("MirrorTimers", Addr, d);
            else Fail("MirrorTimers", Addr, d + "  (timer active but memory reads 0)");
        }

        // ---- Pending cursor spell. Oracle: SpellIsTargeting(). -------------------------------

        private void CheckPendingCursorSpell()
        {
            const uint ModeAddr = 0x00D3F4E4;
            const uint IdOffset = 0x20;

            bool arming = LuaStr("return tostring(SpellIsTargeting())") == "1";
            uint mode = RdU32(ModeAddr);
            uint id = mode != 0U ? RdU32(mode + IdOffset) : 0U;

            if (!arming)
            {
                // Reticle down: both should be quiet. Non-zero here means the address is not this.
                string d = string.Format("reticle DOWN; mode={0} id={1}", mode, id);
                if (mode == 0U && id == 0U)
                    Skip("PendingCursorSpell", ModeAddr, "arm a GROUND spell (Blizzard/Volley/etc) then re-run -- "
                        + "reads 0/0 with the reticle down, which is consistent but not proof");
                else
                    Fail("PendingCursorSpell", ModeAddr, d + "  (should be 0/0 with no reticle)");
                return;
            }

            string dd = string.Format("reticle UP; mode={0} spellId={1}", mode, id);
            if (mode != 0U && id != 0U) Pass("PendingCursorSpell", ModeAddr, dd);
            else Fail("PendingCursorSpell", ModeAddr, dd + "  (reticle armed but memory reads 0)");
        }

        // ---- Faction standing base. Oracle: GetFactionInfo standings. ------------------------

        private void CheckFactionStanding()
        {
            const uint Addr = 0x00C22B70;
            long n = LuaNum("return (GetNumFactions())");
            if (n <= 0) { Skip("FactionStandingBase", Addr, "a character with reputations (GetNumFactions() > 0)"); return; }

            // Find the first real faction row (not a header) and use its bar value as the needle.
            long needle = long.MinValue;
            for (int i = 1; i <= n && i <= 40; i++)
            {
                long isHeader = LuaNum(string.Format("return select(9, GetFactionInfo({0})) and 1 or 0", i));
                if (isHeader == 1) continue;
                long v = LuaNum(string.Format("return (select(6, GetFactionInfo({0})) or -1)", i));
                if (v > 0) { needle = v; break; }
            }
            if (needle == long.MinValue) { Skip("FactionStandingBase", Addr, "at least one non-header faction with standing > 0"); return; }

            string d = string.Format("needle standing={0}; {1}", needle, ScanFor((ulong)needle, Addr, 0x600, false));
            // No documented layout to assert -- report where the value lives so the layout can be derived.
            if (d.Contains("expected value found")) Pass("FactionStandingBase", Addr, d);
            else Fail("FactionStandingBase", Addr, d);
        }

        // ---- Forced reactions: count + array. Structural plausibility only. ------------------

        private void CheckForcedReactions()
        {
            const uint CountAddr = 0x00C23490;
            const uint ArrayAddr = 0x00C23494;
            uint count = RdU32(CountAddr);
            uint arr = RdU32(ArrayAddr);

            string d = string.Format("count={0}, arrayPtr=0x{1:X8}", count, arr);
            // The pointer must be judged on its OWN merits -- an earlier version let count==0
            // short-circuit the whole test, which passed an arrayPtr of 0x00000004 as sane.
            bool ptrOk = arr == 0U || (arr > 0x400000U && arr < 0xF00000U);
            if (!ptrOk) { Fail("ForcedReactions", CountAddr, d + "  (arrayPtr is neither null nor a client address)"); return; }
            if (count > 64U) { Fail("ForcedReactions", CountAddr, d + "  (count implausible)"); return; }
            if (count == 0U && arr == 0U)
                Skip("ForcedReactions", CountAddr, "a zone/quest that sets a FORCED reaction (rare) -- reads 0/0 now, "
                    + "the expected idle state, which proves nothing either way");
            else Pass("ForcedReactions", CountAddr, d);
        }

        // ---- Merchant frame statics. Oracle: GetMerchantNumItems() + UnitGUID('npc'). --------

        private void CheckMerchant()
        {
            const uint MerchantGuid = 0x00BFA3E8;
            const uint NumItems = 0x00BFA3F0;
            const uint NumBuyback = 0x00BFA3F4;

            if (LuaStr("return tostring(MerchantFrame and MerchantFrame:IsVisible() or false)") != "true")
            { Skip("Merchant.*", MerchantGuid, "OPEN A VENDOR window, then re-run"); return; }

            long luaItems = LuaNum("return (GetMerchantNumItems())");
            ulong npc = LuaGuid("npc");

            ulong memGuid = RdU64(MerchantGuid);
            uint memItems = RdU32(NumItems);
            uint memBuyback = RdU32(NumBuyback);

            string dg = string.Format("memory=0x{0:X16}, UnitGUID('npc')=0x{1:X16}", memGuid, npc);
            if (npc != 0UL && memGuid == npc) Pass("Merchant.guid", MerchantGuid, dg);
            else Fail("Merchant.guid", MerchantGuid, dg + "  |  " + ScanFor(npc, MerchantGuid, 0x800, true));

            string di = string.Format("memory={0}, Lua={1}", memItems, luaItems);
            if (luaItems >= 0 && (long)memItems == luaItems) Pass("Merchant.numItems", NumItems, di);
            else Fail("Merchant.numItems", NumItems, di + "  |  " + ScanFor((ulong)luaItems, NumItems, 0x800, false));

            L("INFO  Merchant.numBuyback   0x{0:X6}  memory={1} (no Lua oracle; sane range 0-12)", NumBuyback, memBuyback);
        }

        // ---- Quest frame's shown quest id. Oracle: the frame title. --------------------------

        private void CheckShownQuestId()
        {
            const uint Addr = 0x00C0D65C;
            string title = LuaStr("return tostring((QuestFrame and QuestFrame:IsVisible() and GetTitleText()) or '')");
            if (string.IsNullOrEmpty(title) || title == "nil")
            { Skip("ShownQuestId", Addr, "OPEN A QUEST dialog at an NPC, then re-run"); return; }

            uint id = RdU32(Addr);
            // 3.3.5a has no GetQuestID(), so the id cannot be asserted -- but it must be a plausible
            // quest id while a quest panel is up, and it lags by one frame (see the VibeParty notes).
            string d = string.Format("shown quest title '{0}', memory id={1}", title, id);
            if (id > 0U && id < 100000U) Pass("ShownQuestId", Addr, d + " (plausible; note it holds the PREVIOUS frame's id)");
            else Fail("ShownQuestId", Addr, d + "  (implausible quest id while a quest panel is open)");
        }
    }
}
