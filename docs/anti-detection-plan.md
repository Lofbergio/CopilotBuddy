# Agent N — Critique & Phased Anti-Detection Plan

> **Document type:** Research / Planning (no code changes)  
> **Date:** 2026-03-11  
> **Target:** CopilotBuddy (GreenMagic/ExecutorRand.cs, .NET 10, x86, WoW 3.3.5a build 12340)  
> **Threat model:** TrinityCore Warden — 9 MEM_CHECK, 1 Lua check, 1 module check, 30s cycle, logging-only

---

## 1. CHALLENGE OF ASSUMPTIONS

### 1.1 "Is the 5-byte JMP really the biggest risk? Or is the RWX page?"

**The RWX page is the bigger risk. Here's why:**

- The 5-byte JMP at EndScene is at a **fixed, known address**. Warden's MEM_CHECK can check it, but the server must **know** to check that specific address. TrinityCore's default Warden config checks ~200 known offsets. EndScene's address varies by d3d9.dll version, D3D wrapper, and GPU driver — the server would need the exact vtable offset for each client build. This is a **targeted** check.

- The RWX page (`m_InjectedCode` at `0x240 = PAGE_EXECUTE_READWRITE | PAGE_NOCACHE`) is a **statistical anomaly**. Normal WoW has **zero** committed RWX pages. Your WardenScanner already proves this — it flags that single 4KB page. Warden's `VirtualQueryEx`-based scan (the PAGE_CHECK variant) sweeps the entire address space looking for RWX pages. This is a **sweep** check — no prior knowledge needed. Any Warden implementation that scans page protections will find it.

- `PAGE_NOCACHE (0x200)` makes it **even more suspicious**. Normal application code never combines RWX with NOCACHE. This is a self-incriminating combination that screams "injected code buffer."

**Verdict:** Fix the RWX page **first** (Phase 1). The JMP is risky but requires targeted knowledge; the RWX page is a neon sign.

### 1.2 "Is VMT swap worth it given the complexity?"

**No, not as a first move. Here's the analysis:**

The current 5-byte JMP writes `E9 xx xx xx xx` at EndScene's entry point. A VMT swap would instead overwrite the **function pointer** in the D3D9 device vtable so it points to `m_EndSceneDetour` instead of the real EndScene. Zero code bytes modified.

**Complexity assessment:**
- Need to locate the D3D9 device vtable in WoW's process (already partially done — EndScene address is resolved via vtable walk)
- Need to write a single 4-byte pointer instead of 5 bytes of code
- Need to handle vtable restoration on cleanup
- **Race condition risk:** If WoW re-creates the D3D9 device (Alt+Tab, resolution change, device lost), the vtable pointer resets and the hook is lost. CopilotBuddy currently doesn't handle device-lost events.
- **Detection:** Warden can check vtable pointers just as easily as code bytes. TrinityCore's MEM_CHECK can read 4 bytes from the vtable offset.

**Verdict:** VMT swap provides marginal benefit over the JMP for the complexity cost. The JMP is detectable but so is the vtable pointer. Where VMT swap **wins** is that it modifies a **data** pointer (not executable code), so code-integrity checks (hash-based) won't catch it. Worth implementing in Phase 2, but not critical.

### 1.3 "Can HWBP and VMT swap coexist?"

**Yes, but they solve different problems:**

- HWBP (DR0-DR3 hardware breakpoints) replaces the hooking mechanism entirely. Set DR0 = EndScene address, DR7 = break on execute. The VEH handler catches the `EXCEPTION_SINGLE_STEP` and redirects to the detour.
- VMT swap replaces the hooking mechanism too (different approach).
- You'd use **one or the other**, not both simultaneously. They are **alternative** hooking strategies.
- HWBP is strictly superior in stealth (no code modification, no pointer modification) but has its own detection vector: `NtGetContextThread` / `GetThreadContext` reveals DR0-DR3 values.

**Verdict:** Choose HWBP **or** VMT swap, not both. HWBP is stealthier but requires a `GetThreadContext` hook to hide DR registers. Phase 3 material.

### 1.4 "Can we predict Warden scan windows?"

**Partially, but it's unreliable for evasion:**

- TrinityCore Warden sends scan requests every ~30 seconds (configurable per server)
- The scan request arrives as a packet, gets processed, Warden module runs its checks synchronously on the main thread, and sends results back
- CopilotBuddy's WardenScanner can monitor Warden state (loaded/active/idle) via `WardenStructurePTR` at `0x00D31A4C`
- **However:** Timing-based evasion (unhook before scan, re-hook after) is fundamentally flawed because:
  1. The 30s interval is approximate — server can vary it
  2. Network latency makes timing unpredictable
  3. If you're unhooked when the bot needs to execute, you lose a frame
  4. A race condition where Warden scans during the re-hook window is a **detection** event

**Verdict:** Don't rely on timing-based evasion. Make the hook **always** clean rather than **sometimes** clean. Timing is supplementary intel, not a strategy.

### 1.5 "Is polymorphic ASM actually useful against Warden?"

**No, not against TrinityCore Warden. Here's the critical distinction:**

Warden's MEM_CHECK reads bytes at **specific fixed offsets** and compares against known-good values. It does **not** do signature scanning, pattern matching, or heuristic analysis. It's a CRC/hash check on known addresses.

The polymorphic padding (`AddRandomLine()`) varies the bytes in `m_EndSceneDetour` and `m_InjectedCode`, but:
- Warden never scans those addresses (they're dynamically allocated, different each run)
- The only fixed-address check is at EndScene itself, where the JMP opcode `E9` is always the first byte regardless of padding
- The polymorphism protects against **static signature** scanners (like antivirus), not against **memory integrity** checkers (like Warden)

**Where it IS useful:** If Warmane or the server runs a more sophisticated scanner that does pattern matching on allocated code (some private servers do), polymorphism helps. Keep it, but don't count on it for Warden evasion.

**Verdict:** Polymorphic ASM is defense-in-depth. It costs nothing to keep but provides zero protection against TrinityCore's Warden specifically.

---

## 2. TECHNIQUE CRITIQUE

### Tier 1 — Critical

| # | Technique | Feasibility | Risk | Complexity | Dependencies | Crash Risk |
|---|-----------|-------------|------|-----------|--------------|------------|
| 1 | **RWX→RX toggle** | ✅ HIGH | LOW | ~30 LOC | `VirtualProtectEx` already imported in Imports.cs | NONE if done correctly |
| 2 | **VMT swap hook** | ⚠️ MEDIUM | MEDIUM | ~80 LOC | D3D9 device vtable address (already resolved), device-lost handling | LOW — pointer write is atomic |
| 3 | **Code restoration** | ⚠️ MEDIUM | HIGH | ~50 LOC | Timing with execution cycle, thread synchronization | **HIGH** — restoring while EndScene is executing = crash |

**Detailed critique:**

**1. RWX→RX toggle (⭐⭐⭐⭐⭐ — DO THIS FIRST)**

Current code in `InitializeDetour()` line ~477:
```csharp
m_InjectedCode = Memory.AllocateMemory(size, 0x1000, 0x240); // PAGE_EXECUTE_READWRITE | PAGE_NOCACHE
```

Fix: Allocate as `0x04` (RW), write code, then `VirtualProtectEx` to `0x20` (RX). Before each `Inject()` call, toggle to RW, write, toggle back to RX.

**Risks:**
- If `VirtualProtectEx` fails (unlikely — we have PROCESS_VM_OPERATION), the code page is unexecutable → crash on next EndScene frame. **Mitigation:** Check return value, fall back to RWX if VirtualProtectEx fails.
- **Race condition:** WoW's main thread could execute `m_InjectedCode` between the RW→write and write→RX transitions. During the RW phase, the page is not executable. **Mitigation:** This is actually safe because `m_InjectedCode` is only called when the injection event is signaled, and we only write to it before signaling. The execution sequence is: write code → set RX → signal event → WoW executes. No race.

**Implementation location:** `Memory.cs` (add `ProtectMemory` wrapper), `ExecutorRand.cs` (`Execute()`, `GrabFrame()`, `SharedExecuteLogicEnd()`).

---

**2. VMT swap hook**

Replace the 5-byte JMP at EndScene with a vtable pointer modification:
```
Before: D3D9DeviceVtable[42] → real_EndScene (JMP → m_EndSceneDetour)
After:  D3D9DeviceVtable[42] → m_EndSceneDetour (no JMP needed)
```

**Risks:**
- D3D9 device can be recreated (resolution change, Alt+Tab on some systems, device lost). The vtable pointer resets. Need a watchdog to re-hook.
- Other hooks (OBS, Fraps, RTSS) also hook EndScene via vtable swap. If they save the pointer after we install ours, unhooking creates a chain break. The current JMP-based hook handles chaining (see prologue detection in `InjectDetour()`). VMT swap removes that capability.
- Warden can still read the vtable pointer value and compare against the expected EndScene address.

**Race conditions:**
- Writing a 4-byte pointer is atomic on x86 (aligned DWORD write). No race.
- Device recreation is not atomic — need to handle it.

**Dependencies:**
- The vtable base address is already known (that's how `m_OrigEndScene` is resolved)
- Need to store the vtable offset (index 42 for EndScene in D3D9)
- Need a device-lost detection mechanism

---

**3. Code restoration after execution**

Restore original 5 (or 6/7) bytes at EndScene when not actively executing, re-patch the JMP only when the bot needs to run code.

**Risks:**
- **CRITICAL RACE CONDITION:** WoW calls EndScene ~60 times/second (once per frame). If we restore bytes and WoW calls EndScene in the ~μs between our restore and re-patch, EndScene runs unhooked. We miss a frame — acceptable. But if we're **mid-write** (e.g., 3 of 5 bytes written) when WoW calls EndScene, corrupt instruction = **CRASH**.
- `WriteProcessMemory` on x86 is not atomic for 5 bytes. It's a memcpy internally. On single-core systems this is especially dangerous.
- This technique only helps if Warden scans during the "unhooked" window. Given bot execution happens dozens of times per second and Warden scans once per 30 seconds, the probability of "clean during scan" is high. But it's probabilistic, not deterministic.

**Mitigation:** Use `SuspendThread` / `ResumeThread` around the write:
1. Suspend WoW's main thread
2. Restore original bytes
3. Resume thread
4. Wait for Warden scan to pass
5. Suspend again
6. Re-patch JMP
7. Resume

**Problem:** `SuspendThread` can deadlock if WoW is inside a kernel call. HB 3.3.5a used this approach for EIP hijacking but had careful timing.

**Verdict:** High risk, medium reward. Phase 2 at earliest, and only if combined with Warden scan window detection.

---

### Tier 2 — Important

| # | Technique | Feasibility | Risk | Complexity | Dependencies | Crash Risk |
|---|-----------|-------------|------|-----------|--------------|------------|
| 4 | **HWBP hook** | ⚠️ MEDIUM | MEDIUM | ~120 LOC | SetThreadContext access, VEH rework | LOW if VEH is correct |
| 5 | **GetThreadContext hook** | ❌ LOW | HIGH | ~200 LOC | Inline hook or IAT hook of ntdll | **HIGH** — hooking ntdll is risky |
| 6 | **Two-phase allocation** | ✅ HIGH | LOW | ~15 LOC | None — trivial change to AllocateMemory | NONE |

**Detailed critique:**

**4. HWBP hook (⭐⭐⭐⭐ stealth, ⭐⭐ complexity)**

Set DR0 = EndScene address with "break on execute" in DR7. When WoW calls EndScene, a `EXCEPTION_SINGLE_STEP` fires. VEH handler catches it, modifies CONTEXT.Eip to point to `m_EndSceneDetour`.

**What changes:**
- Remove the 5-byte JMP entirely
- Modify VEH handler to detect single-step exceptions (not just our TLS-marked threads — this fires on WoW's main thread)
- Use `SetThreadContext` on WoW's main thread to set DR0/DR7
- The VEH handler currently only identifies "our" thread via TLS. For HWBP, it needs to redirect **any** thread that hits EndScene.

**Why it's complex:**
- The current VEH block in `EmitVehBlock()` is ~150 lines of injected ASM that filters by TLS. HWBP needs a different filter: check if the exception is at EndScene address.
- DR0-DR3 are per-thread. Need to set them on WoW's **main thread** (the one calling EndScene). Currently, CopilotBuddy opens thread 0 (`_process.Threads[0]`), which may not be the rendering thread.
- If WoW uses debug registers for its own purposes, we clobber them.

**Detection vector:** `GetThreadContext` reveals DR0-DR3 values. Technique #5 is required to fully hide HWBP.

---

**5. GetThreadContext hook**

Intercept `NtGetContextThread` in ntdll.dll to return zeroed DR0-DR3 when Warden calls it.

**Why this is the hardest technique:**
- Requires hooking inside ntdll.dll — the lowest user-mode layer
- CopilotBuddy operates **externally** (separate process, using `WriteProcessMemory`). Hooking ntdll inside WoW requires either:
  - Injecting a DLL into WoW (which creates **new** detection surface — Warden checks loaded modules)
  - Patching ntdll's code bytes directly via `WriteProcessMemory` (inline hook — same detection risk as the EndScene JMP)
  - IAT hooking WoW's import table (Warden checks IAT integrity)
- Any hook of ntdll is **more** detectable than the problem it solves
- Requires kernel-mode knowledge of the CONTEXT structure layout across Windows versions

**Verdict:** ❌ Not feasible without DLL injection. The cure is worse than the disease for an external bot. Skip entirely unless the architecture changes to use a loaded DLL.

---

**6. Two-phase allocation (⭐⭐⭐⭐⭐ easy win)**

Change:
```csharp
m_InjectedCode = Memory.AllocateMemory(size, 0x1000, 0x240);
```
To:
```csharp
m_InjectedCode = Memory.AllocateMemory(size, 0x1000, 0x04);  // RW
// ... write code ...
VirtualProtectEx(hProcess, m_InjectedCode, size, 0x20, out _); // → RX
```

This is essentially the same as Technique #1 but as an allocation pattern rather than a toggle. **This is the trivial version of #1.** Do both together.

---

### Tier 3 — Nice-to-have

| # | Technique | Feasibility | Risk | Complexity | Crash Risk | Worth It? |
|---|-----------|-------------|------|-----------|------------|-----------|
| 7 | **Dynamic JMP removal** | ⚠️ | HIGH | ~80 LOC | **HIGH** (race) | No — subsumed by VMT swap |
| 8 | **Memory cloaking** | ⚠️ | MEDIUM | ~40 LOC | MEDIUM | Marginal — pages are already RX |
| 9 | **Timing obfuscation** | ✅ | LOW | ~10 LOC | NONE | Yes — easy, adds entropy |

**7. Dynamic JMP removal:** Same race condition problems as Technique #3. If we implement VMT swap (#2), this becomes irrelevant because there's no JMP to remove. Skip.

**8. Memory cloaking (PAGE_NOACCESS when idle):** After the RWX→RX fix, `m_InjectedCode` is already RX. Setting it to PAGE_NOACCESS when idle means any stray execution hits an access violation. Useful if Warden tries to **read** the code bytes of allocated pages. However, `ReadProcessMemory` (which Warden uses for MEM_CHECK) bypasses page protection — it reads physical memory via the kernel. **PAGE_NOACCESS won't stop Warden's scan.** Skip.

**9. Timing obfuscation:** Randomize `FrameDropWaitTime` within a range instead of fixed 1000ms. Already partially implemented via XOR obfuscation. Adding jitter (e.g., 800-1200ms random per frame) makes behavioral fingerprinting harder. Trivial to add.

---

## 3. DEAL-BREAKERS

### Techniques that could CRASH the game:
| Technique | Crash vector | Severity |
|-----------|-------------|----------|
| Code restoration (#3) | Mid-write race: partial instruction at EndScene entry | **FATAL** — corrupts x86 instruction stream |
| Dynamic JMP removal (#7) | Same as #3 | **FATAL** |
| HWBP (#4) | Wrong DR7 configuration → spurious breakpoints in WoW | RECOVERABLE (VEH catches) if coded correctly |
| GetThreadContext hook (#5) | Corrupt ntdll hook → any syscall crashes | **FATAL** — system-wide impact |
| RWX→RX toggle (#1) | VirtualProtectEx failure leaves page RW (not executable) → crash on next call | RECOVERABLE — check return value |

### Techniques that could desync the bot:
| Technique | Desync vector | Severity |
|-----------|------------|----------|
| Code restoration (#3) | Restored during execution → misses continuous execution frames | HIGH — bot stops responding |
| VMT swap (#2) | Device lost → vtable reset → hook lost → no more EndScene interception | HIGH — bot becomes deaf |
| RWX→RX toggle (#1) | If code is written but protection not yet set to RX when WoW calls it | LOW — signal ordering prevents this |

### Techniques requiring kernel-mode access:
| Technique | Why | Alternative |
|-----------|-----|-------------|
| GetThreadContext hook (#5) | ntdll is the user/kernel boundary | None feasible for external bot |
| HWBP with full concealment | Hiding DR registers from kernel-mode queries | Accept that DR registers are visible to ring-0 scans |

---

## 4. PHASED IMPLEMENTATION PLAN

### Phase 1: Quick Wins (1-2 days, lowest risk, highest impact)

**Goal:** Eliminate the RWX neon sign. Zero crash risk.

| Step | What | Where | Lines | Test |
|------|------|-------|-------|------|
| 1a | Add `ProtectMemory(uint addr, uint size, uint newProtect)` wrapper | `Memory.cs` — new method wrapping `Imports.VirtualProtectEx` | ~15 | Unit test: allocate RW, protect RX, verify with VirtualQueryEx |
| 1b | Two-phase allocation for `m_InjectedCode` | `ExecutorRand.cs` `InitializeDetour()` line ~477 | ~5 | Change `0x240` → `0x04`, add ProtectMemory call after first write |
| 1c | RW/RX toggle around `Asm.Inject(m_InjectedCode)` | `ExecutorRand.cs` `Execute()`, `SharedExecuteLogicEnd()` | ~12 | Before Inject: protect RW. After Inject: protect RX. Verify with WardenScanner |
| 1d | Remove `PAGE_NOCACHE` flag entirely | `ExecutorRand.cs` `InitializeDetour()` | 1 | Already handled by 1b |
| 1e | Add jitter to FrameDropWaitTime | `ExecutorRand.cs` wherever timeout is set | ~5 | Randomize between 800-1200ms instead of fixed 1000 |

**Validation:** Run WardenScanner → should report **zero** RWX pages from CopilotBuddy.

**Risk:** Essentially zero. VirtualProtectEx is a documented, reliable API. The signal ordering (write → protect → signal) prevents races.

**Dependency graph:**
```
1a (ProtectMemory wrapper) ──→ 1b (two-phase alloc)
                           ──→ 1c (RW/RX toggle)
                                    ↓
                           1d (remove NOCACHE) [trivial, part of 1b]
1e (timing jitter) [independent]
```

---

### Phase 2: Medium Complexity (3-5 days, moderate risk)

**Goal:** Eliminate the 5-byte JMP detection vector. Replace with VMT swap.

| Step | What | Where | Lines | Test |
|------|------|-------|-------|------|
| 2a | Store D3D9 vtable address + EndScene index | `ExecutorRand.cs` — new fields | ~10 | Already partially implemented (EndScene discovery) |
| 2b | Replace `Asm.Inject(m_OrigEndScene)` JMP with vtable pointer write | `ExecutorRand.cs` `InjectDetour()` last section | ~20 | Write `m_EndSceneDetour` to vtable[42]. Verify EndScene redirects. |
| 2c | Update `Dispose()` to restore vtable pointer | `ExecutorRand.cs` `Dispose()` | ~10 | Existing byte-check logic replaced with pointer-check logic |
| 2d | Remove prologue restoration from detour | `ExecutorRand.cs` `InjectDetour()` prologue section | DELETE ~60 | No prologue needed — vtable points directly to our code, which calls real EndScene via saved pointer |
| 2e | Add device-lost watchdog | New method or existing frame loop | ~30 | Monitor vtable[42] periodically. If it changes (device recreated), re-hook. |

**Architecture change:** With VMT swap, the detour no longer needs to "restore original bytes and jump back." Instead:
```
Before (JMP): WoW calls EndScene → JMP to detour → detour restores prologue → JMP back to EndScene+5
After (VMT):  WoW calls vtable[42] → goes directly to detour → detour calls real_EndScene via saved pointer
```

This simplifies the detour and removes the entire prologue detection block (lines 750-830 in ExecutorRand.cs).

**Validation:**
- WardenScanner: EndScene entry bytes should be **original, unmodified**
- Bot should function identically
- Run for 30+ minutes, including Alt+Tab cycles

**Risk matrix:**
| Risk | Probability | Impact | Mitigation |
|------|------------|--------|------------|
| Device lost breaks hook | LOW (rare on 3.3.5a) | HIGH — bot goes deaf | Watchdog in 2e |
| Other hooks break chain | MEDIUM (OBS, RTSS) | MEDIUM — other overlays break | Detect and handle in 2b |
| Warden checks vtable pointer | LOW (TC default doesn't) | HIGH — detected | Accept; still better than code modification |

**Dependency graph:**
```
Phase 1 complete ──→ 2a (store vtable addr)
                      ↓
                     2b (vtable write) ──→ 2d (remove prologue)
                      ↓
                     2c (dispose update)
                     2e (watchdog) [can be parallel with 2d]
```

---

### Phase 3: Advanced Techniques (1-2 weeks, high complexity)

**Goal:** Zero-modification hooking via hardware breakpoints. Maximum stealth.

| Step | What | Where | Lines | Test |
|------|------|-------|-------|------|
| 3a | Identify WoW's rendering thread | `Memory.cs` or new `ThreadResolver.cs` | ~40 | Enumerate threads, find the one calling D3D9 |
| 3b | Implement HWBP-based EndScene hook | `ExecutorRand.cs` — replace JMP/VMT with DR0 setup | ~120 | SetThreadContext to set DR0=EndScene, DR7=execute break |
| 3c | Modify VEH handler for SINGLE_STEP | `ExecutorRand.cs` `EmitVehBlock()` — major rework | ~80 | VEH checks exception address == EndScene, redirects to detour |
| 3d | DR register concealment (best-effort) | `ExecutorRand.cs` — clear DR0 in VEH after redirect | ~10 | Self-clears DR0 after each redirect, re-sets on next frame |

**Why 3d is "best-effort":** Without hooking `GetThreadContext` (infeasible for external bot, see critique #5), we can't prevent Warden from reading DR registers. But we can minimize the window: set DR0 only when waiting for EndScene, clear it immediately after the hook fires. Warden would need to call `GetThreadContext` in the exact microsecond window between set and clear.

**Decision point:** Phase 3 should only be pursued if:
1. Phase 1+2 are deployed and stable
2. WardenScanner confirms the server is actively checking EndScene vtable pointers
3. The threat model escalates beyond logging-only

**Dependency graph:**
```
Phase 2 complete ──→ 3a (thread ID) ──→ 3b (HWBP setup)
                                         ↓
                                        3c (VEH rework) ──→ 3d (DR concealment)
```

---

## 5. RISK MATRIX (ALL TECHNIQUES)

| Technique | Detection Reduction | Implementation Risk | Crash Risk | Complexity | Priority |
|-----------|-------------------|-------------------|-----------|-----------|----------|
| RWX→RX toggle | ⭐⭐⭐⭐⭐ | ⭐ (minimal) | ⭐ (near zero) | ~30 LOC | **P0 — DO FIRST** |
| Two-phase alloc | ⭐⭐⭐⭐⭐ | ⭐ (minimal) | ⭐ (near zero) | ~5 LOC | **P0 — DO FIRST** |
| Timing jitter | ⭐⭐ | ⭐ (minimal) | ⭐ (zero) | ~5 LOC | **P0 — FREE** |
| VMT swap | ⭐⭐⭐⭐ | ⭐⭐⭐ (moderate) | ⭐⭐ (low) | ~80 LOC | **P1** |
| HWBP hook | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ (high) | ⭐⭐⭐ (medium) | ~250 LOC | **P2** |
| Code restoration | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ (very high) | ⭐⭐⭐⭐⭐ (crash) | ~50 LOC | **SKIP** |
| Dynamic JMP removal | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ (very high) | ⭐⭐⭐⭐⭐ (crash) | ~80 LOC | **SKIP** |
| GetThreadContext hook | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ (very high) | ⭐⭐⭐⭐⭐ (crash) | ~200 LOC | **SKIP** |
| Memory cloaking | ⭐ (zero vs Warden) | ⭐⭐ (low) | ⭐⭐ (low) | ~40 LOC | **SKIP** |

---

## 6. DEPENDENCY GRAPH (FULL)

```
                    ┌──────────────────────────────────────────────┐
                    │  PHASE 1: Quick Wins (P0)                    │
                    │                                              │
                    │  ProtectMemory() ──→ Two-Phase Alloc         │
                    │        │              (eliminate 0x240)       │
                    │        ↓                                     │
                    │  RW/RX Toggle ──→ WardenScanner validates    │
                    │  (around Inject)    zero RWX pages           │
                    │                                              │
                    │  Timing Jitter (independent)                 │
                    └────────────────────┬─────────────────────────┘
                                         │
                                         ↓
                    ┌──────────────────────────────────────────────┐
                    │  PHASE 2: VMT Swap (P1)                      │
                    │                                              │
                    │  Store vtable addr ──→ Vtable write          │
                    │                         ├──→ Remove prologue │
                    │                         └──→ Update Dispose  │
                    │  Device-lost watchdog (parallel)             │
                    └────────────────────┬─────────────────────────┘
                                         │
                                         ↓  (only if threat escalates)
                    ┌──────────────────────────────────────────────┐
                    │  PHASE 3: HWBP (P2)                          │
                    │                                              │
                    │  Thread resolver ──→ DR0/DR7 setup           │
                    │                       ↓                      │
                    │                 VEH rework (SINGLE_STEP)     │
                    │                       ↓                      │
                    │              DR concealment (best-effort)    │
                    └──────────────────────────────────────────────┘
```

---

## 7. WHAT NOT TO DO (ANTI-PATTERNS)

1. **Don't hook ntdll.** External bots cannot safely hook ntdll without DLL injection, which creates a bigger detection surface than the problem it solves.

2. **Don't use SuspendThread for write windows.** Suspending WoW's main thread for byte restoration is a deadlock risk and Warden checks if its thread gets suspended.

3. **Don't over-invest in polymorphic ASM.** It doesn't help against Warden's integrity checks. Keep what exists (it's free entropy) but don't expand it.

4. **Don't time scan evasion.** "Unhook before Warden checks" is probabilistic and fragile. Make the persistent state clean instead.

5. **Don't combine techniques 2 (VMT swap) and 3 (code restoration).** They solve the same problem differently. Pick one per phase.

---

## 8. RECOMMENDED IMMEDIATE ACTIONS

1. **Implement Phase 1** — the RWX→RX fix. This is the single highest-impact change with near-zero risk. It eliminates the detection that WardenScanner already confirms.

2. **Run WardenScanner after Phase 1** to validate zero RWX pages from CopilotBuddy.

3. **Monitor Warden scan logs** (WardenScanner database) for 48 hours to establish baseline before Phase 2.

4. **Phase 2 decision gate:** Only proceed to VMT swap if scan logs show EndScene byte checks being performed by the server.

5. **Phase 3 is a research project**, not a near-term deliverable. Only pursue if Phase 1+2 prove insufficient against the server's actual Warden configuration.
