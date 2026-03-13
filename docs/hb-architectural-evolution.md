# HonorBuddy Architectural Evolution: 3.3.5a → Legion

## Research-Only Chronological Analysis

> **Scope**: Reverse engineering analysis of decompiled HonorBuddy source code across five WoW expansions. All findings are evidence-based, citing specific file paths and code patterns found in the `.hb *` decompiled folders.

---

## Executive Summary

| Version | WoW Expansion | Memory Library | Hook Method | VEH | Polymorphism | DX Support |
|---------|---------------|----------------|-------------|-----|-------------|------------|
| 3.3.5a  | Wrath (WotLK) | **BlueMagic**  | 5-byte JMP  | No  | Basic (ExecutorRand) | DX9 only |
| 4.3.4   | Cataclysm     | **BlueMagic**  | 5-byte JMP  | No  | None in main Executor | DX9 only |
| 5.4.8   | Mists (MoP)   | **GreyMagic**  | 5-byte JMP + VEH gate | **Yes** | AddRandomLine | DX9 + DX11 |
| 6.2.3   | Warlords (WoD)| **GreyMagic**  | 5-byte JMP + VEH gate | **Yes** | **AsmRandomizer** | DX9 + DX11 |
| Legion  | Legion        | **GreyMagic**  | 5-byte JMP + VEH gate | **Yes** | AsmRandomizer (obfuscated) | DX9 + DX11 |

**Key finding**: HonorBuddy was ALWAYS an external process bot. It never ran as an injected DLL. All versions use `ReadProcessMemory` / `WriteProcessMemory` / `VirtualAllocEx` from a separate C# process, injecting x86 assembly shellcode into WoW's address space via FASM assembler. The "internal" appearance comes from the injected ASM trampoline that runs inside WoW's EndScene frame.

---

## 1. HB 3.3.5a — WotLK (Baseline Architecture)

### Source Files
- `.hb 3.3.5a/BlueMagic/Executor.cs` — Non-polymorphic hook executor
- `.hb 3.3.5a/BlueMagic/ExecutorRand.cs` — Polymorphic variant with `AddRandomLine()`
- `.hb 3.3.5a/BlueMagic/Memory.cs` — Process memory abstraction (OpenProcess, OpenThread)
- `.hb 3.3.5a/BlueMagic/Native/Imports.cs` — P/Invoke declarations

### Architecture: External Process with EIP Hijack

The bot operates as a separate C# process. `Memory.cs` constructor calls:
```csharp
OpenProcess(2035711U, ...)  // PROCESS_ALL_ACCESS equivalent
OpenThread(2032639U, ...)   // THREAD_ALL_ACCESS equivalent
```

It detects ASLR via PE header analysis:
```csharp
PeHeaderParser parser = new PeHeaderParser(...);
// Checks DllCharacteristics bit 64 (IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE)
```

### Hook Installation: SuspendThread + SetThreadContext EIP Redirect

`InitializeDetour()` (Executor.cs line 389) performs a **thread-hijack initialization**:

1. **Allocate 3 remote memory regions** via `VirtualAllocEx`:
   - `EndSceneDetour` — 32 bytes, `PAGE_EXECUTE_READ` (protection 32)
   - `InjectedCode` — 576 bytes, `PAGE_EXECUTE_READWRITE | PAGE_NOCACHE` (protection 576 = 0x240)
   - `DataPtr` — 4 bytes, `PAGE_READWRITE` (protection 4)

2. **Suspend WoW's main thread** and read EIP via `GetThreadContext`:
   ```csharp
   this.m_Memory.SuspendThread();
   Context ctx = this.m_Memory.GetThreadContext(65543U); // CONTEXT_FULL | CONTEXT_DEBUG_REGISTERS
   ```

3. **Safety check**: Verify EIP is NOT inside `d3d9.dll` (avoids hooking mid-DirectX call):
   ```csharp
   if (ctx.Eip >= m_d3dBase && ctx.Eip <= m_d3dBase + m_d3dSize)
       throw new Exception("Thread is in d3d9.dll");
   ```

4. **Redirect EIP** to injected setup code via `SetThreadContext`, then `ResumeThread`.

### Hook Payload: Simple 5-byte JMP

`InjectDetour()` (Executor.cs line 482) writes the EndScene trampoline:

```asm
; === Trampoline (injected at EndSceneDetour) ===
pushad
; Check if injection is waiting (WaitForSingleObject on named event, timeout=0)
mov eax, [WaitingHandle]
push 0
push eax
call WaitForSingleObject
test eax, eax
jnz @NoInjection
; Call the injected code
call InjectedCode
mov [ReturnedData], eax
; Signal finished
mov eax, [FinishedHandle]
push eax
call SetEvent
; Wait for continue signal with FrameDropWaitTime
mov eax, [FrameDropWaitTime]
push eax
mov eax, [ContinueHandle]
push eax
call WaitForSingleObject
test eax, eax
jz @CheckInjection
@NoInjection:
popad
; Copy original prologue bytes
mov edi, edi
push ebp
mov ebp, esp
; Jump back to original EndScene + 5
jmp OriginalEndScene+5
```

The hook is installed by writing `JMP EndSceneDetour` (E9 xx xx xx xx) at `m_OrigEndScene`.

### Polymorphism: ExecutorRand

`ExecutorRand.cs` extends `Executor` with `AddRandomLine()` that inserts random NOP-equivalent instructions between each meaningful instruction:
```csharp
protected void AddRandomLine(int amount, string line, params object[] args)
{
    // Inserts 'amount' random junk instructions before the real instruction
    this.m_Memory.Asm.AddLine(line, args);
}
```

This provides basic **pattern-matching resistance** — each injected payload looks different byte-for-byte.

### Anti-Detection Assessment (3.3.5a)

| Technique | Status | Evidence |
|-----------|--------|----------|
| VEH exception handler | **NOT PRESENT** | No TlsAlloc, no AddVectoredExceptionHandler anywhere |
| Polymorphic ASM | **BASIC** | ExecutorRand only — random padding between real instructions |
| Random event names | **YES** | Named events use random strings for WaitingHandle/ContinueHandle/FinishedHandle |
| Memory protection hardening | **NO** | Always `PAGE_EXECUTE_READWRITE \| PAGE_NOCACHE` (576/0x240) — never RWX→RX toggle |
| VMT swap | **NO** | Direct 5-byte JMP overwrite only |
| HWBP (debug registers) | **NO** | Debug registers read for context but never set for hooking |

---

## 2. HB 4.3.4 — Cataclysm (Minimal Evolution)

### Source Files
- `.hb 4.3.4/BlueMagic/Executor.cs` — Same architecture, slightly cleaned naming
- `.hb 4.3.4/BlueMagic/Memory.cs` — Identical to 3.3.5a

### Changes from 3.3.5a

**Nearly identical architecture.** The 4.3.4 Executor uses obfuscated method names (`method_3()` = InitializeDetour, `method_4()` = InjectDetour) but the logic is the same:

- Same `SuspendThread` → `GetThreadContext` → check EIP not in d3d9.dll → `SetThreadContext` initialization
- Same 3 memory allocations (32/576/4 bytes with same protection flags)
- Same 5-byte JMP at EndScene entry
- Same `pushad` → check event → call injected → set event → wait → `popad` → original prologue → JMP back
- **Still no VEH, no TLS, no DX11 support**

Resource strings were moved to `BlueMagicResources` class, suggesting a minor build-system cleanup but no architectural change.

### Why No Major Changes?

Warden in Cataclysm still used basic memory scanning. The simple JMP hook with random event names and optional polymorphic padding (ExecutorRand) was sufficient. The arms race hadn't escalated yet.

---

## 3. HB 5.4.8 — Mists of Pandaria (MAJOR Architectural Shift)

### Source Files
- `.hb 5.4.8/GreyMagic/Executor.cs` — Complete rewrite with VEH support
- `.hb 5.4.8/GreyMagic/DirectX.cs` — Native C++ DX9/DX11 discovery
- `.hb 5.4.8/GreyMagic/ExternalProcessMemory.cs` — New process memory abstraction
- `.hb 5.4.8/GreyMagic/FrameLock.cs` — Frame synchronization pattern

### Library Rename: BlueMagic → GreyMagic

The memory library was completely rewritten and renamed. Key differences:

| Aspect | BlueMagic (3.3.5a/4.3.4) | GreyMagic (5.4.8+) |
|--------|--------------------------|---------------------|
| Address type | `uint` (32-bit only) | `IntPtr` (64-bit ready) |
| DX support | DX9 only | DX9 + DX11 |
| VEH | None | Full TLS + VEH gate |
| Assembler | ManagedFasm (C# wrapper) | ManagedFasm + native C++/CLI |
| Polymorphism | Optional (ExecutorRand) | Always-on `AddRandomLine()` |

### DirectX Discovery: Native C++ Helper

`DirectX.cs` uses native C++/CLI code to find hook addresses:

```csharp
public static int GetD3D9EndScenePointer(IntPtr hwnd)
{
    return <Module>.GreyMagic.GetEndScene(hwnd.ToPointer());
    // Returns offset relative to d3d9.dll base
}

public static int GetD3D11SwapChainPresentPointer(IntPtr hwnd)
{
    return <Module>.GreyMagic.GetSwapChainPresent(hwnd.ToPointer());
    // Returns offset relative to dxgi.dll base
}
```

This creates a temporary Direct3D device from a hidden Form's HWND, reads the vtable to find `EndScene`/`Present`, and returns the offset. The C++ implementation is compiled into the GreyMagic mixed-mode assembly.

### VEH (Vectored Exception Handler) Introduction

This is the **most significant architectural change** across all versions. The constructor now takes a `veh` parameter:

```csharp
public Executor(ExternalProcessMemory memory, IntPtr hookLocation, int copyBytes, bool veh)
```

When VEH is enabled, `InitializeDetour()` resolves additional kernel32 functions:
- `TlsAlloc` / `TlsFree` / `TlsGetValue` / `TlsSetValue`
- `AddVectoredExceptionHandler` / `RemoveVectoredExceptionHandler`

The data layout grows to 40 bytes with new fields:
```
Offset 0-3:   WaitingHandle
Offset 4-7:   ContinueHandle
Offset 8-11:  FinishedHandle
Offset 12-15: ReturnedData
Offset 16-19: FrameCount
Offset 20-23: InHook
Offset 24-27: Status
Offset 28-31: TlsPtr
Offset 32-35: VehPtr
Offset 36-39: FrameDropWaitTime (XOR-encrypted)
```

### VEH Flow: Complete ASM Architecture

The VEH system works as follows:

#### Phase 1: SetUpGate (runs once at first hook activation)
```asm
@SetUpGate:
    call TlsAlloc              ; Allocate a TLS slot
    mov [TlsPtr], eax          ; Store TLS index
    cmp eax, 0xFFFFFFFF
    jnz @AddVEH
    mov eax, 1                 ; Error: TLS allocation failed
    jmp @SetUpGateReturn
@AddVEH:
    push @ExceptionHandler     ; Our VEH handler address
    push 1                     ; First handler (highest priority)
    call AddVectoredExceptionHandler
    mov [VehPtr], eax          ; Store VEH handle
    test eax, eax
    jnz @SetUpGateSucceeded
    mov eax, 2                 ; Error: VEH registration failed
    jmp @SetUpGateReturn
@SetUpGateSucceeded:
    xor eax, eax               ; Success (0)
@SetUpGateReturn:
    leave
    retn
```

#### Phase 2: CallGate (runs each frame when code injection is requested)
```asm
@CallGate:
    push esp                   ; Store ESP as TLS value (thread marker)
    push dword [TlsPtr]       ; TLS index
    call TlsSetValue           ; Mark this thread as "ours"
    test eax, eax
    jnz @CallGateCallInjectedCode
    mov eax, 3                 ; Error: TLS set failed
    jmp @CallGateReturn
@CallGateCallInjectedCode:
    call InjectedCode          ; Execute the bot's code
    mov [ReturnedData], eax    ; Store return value
    xor eax, eax               ; Success
@CallGateReturn:
    leave
    retn
```

#### Phase 3: ExceptionHandler (VEH handler — the stealth mechanism)
```asm
@ExceptionHandler:
    push ebp
    mov ebp, esp
    push dword [TlsPtr]
    call TlsGetValue           ; Check if exception is on OUR thread
    test eax, eax
    jz @EHContinueSearch       ; Not our thread → pass to next handler
    ; === Our thread: hijack the exception ===
    mov ecx, eax               ; ecx = TLS value (saved ESP)
    mov eax, [ebp+0x8]         ; eax = EXCEPTION_POINTERS
    mov eax, [eax]             ; eax = EXCEPTION_RECORD
    mov eax, [eax]             ; eax = ExceptionCode (unused, falls through)
    mov [ReturnedData], eax    ; Store
    mov eax, [ebp+0x8]
    mov eax, [eax+0x4]        ; CONTEXT structure
    mov [eax+0xC4], ecx       ; CONTEXT.Esp = saved ESP
    mov dword [eax+0xB8], @CallGateInterject  ; CONTEXT.Eip = redirect to CallGateInterject
    mov eax, 0xFFFFFFFF        ; EXCEPTION_CONTINUE_EXECUTION
    jmp @EHReturn
@EHContinueSearch:
    mov eax, 0                 ; EXCEPTION_CONTINUE_SEARCH
@EHReturn:
    leave
    retn
```

**The VEH trick explained**: When the 5-byte JMP hook at EndScene fires, the trampoline deliberately triggers an exception (e.g., access violation on a guard page). The VEH handler catches it, checks the TLS slot to confirm it's "our" thread, and redirects EIP to the bot's code via CONTEXT manipulation. This makes the code execution appear to be an exception recovery rather than a direct call from a hooked function — harder for Warden to trace.

#### Phase 4: TearDownGate (cleanup)
```asm
@TearDownGate:
    mov eax, [VehPtr]
    test eax, eax
    jz @RemoveTLS
    push eax
    call RemoveVectoredExceptionHandler
    mov dword [VehPtr], 0
@RemoveTLS:
    mov eax, [TlsPtr]
    cmp eax, 0xFFFFFFFF
    jz @TearDownSucceeded
    push eax
    call TlsFree
    mov dword [TlsPtr], 0xFFFFFFFF
@TearDownSucceeded:
    xor eax, eax
    leave
    retn
```

### XOR Obfuscation of Timing Values

New in 5.4.8: `FrameDropWaitTime` is XOR-encrypted in remote memory:
```csharp
private uint _frameDropWaitTimeXorKey;
// In InitializeDetour:
this._frameDropWaitTimeXorKey = (uint)this._random.Next();
```

The ASM decrypts it at runtime:
```asm
mov eax, [FrameDropWaitTime]
xor eax, XorKey                ; Decrypt before using as WaitForSingleObject timeout
```

### FrameLock Pattern

`FrameLock.cs` introduces a thread-safe frame synchronization:
```csharp
public FrameLock()
{
    Monitor.Enter(ExternalProcessMemory.AssemblyLock);
    if (!executingFrame)
        BeginExecute();
}
public void Dispose()
{
    if (executingFrame)
        EndExecute();
    Monitor.Exit(ExternalProcessMemory.AssemblyLock);
}
```

### Anti-Detection Assessment (5.4.8)

| Technique | Status | Evidence |
|-----------|--------|----------|
| VEH exception handler | **YES — NEW** | Full TLS + VEH gate system, CONTEXT.Eip redirection |
| TLS thread identification | **YES — NEW** | TlsAlloc/TlsSetValue to mark "our" thread |
| Polymorphic ASM | **IMPROVED** | `AddRandomLine()` always active (not optional) |
| XOR key obfuscation | **YES — NEW** | FrameDropWaitTime encrypted with random XOR key |
| DX11 support | **YES — NEW** | SwapChain::Present via native C++ helper |
| Native C++/CLI component | **YES — NEW** | Mixed-mode assembly for DX discovery and hook type helpers |
| Memory protection hardening | **NO** | Still PAGE_EXECUTE_READWRITE \| PAGE_NOCACHE (576) |
| VMT swap | **NO** | Still 5-byte JMP overwrite |
| HWBP | **NO** | No debug register manipulation |

---

## 4. HB 6.2.3 — Warlords of Draenor (Refinement & Multi-Hook)

### Source Files
- `.hb 6.2.3/GreyMagic/Executor.cs` — ExecutorInitParams system, AsmRandomizer
- `.hb 6.2.3/GreyMagic/AsmRandomizer.cs` — Advanced polymorphic engine
- `.hb 6.2.3/GreyMagic/ExecutorPatch.cs` — Multi-address patching
- `.hb 6.2.3/GreyMagic/DirectX.cs` — Same DX9/DX11 as 5.4.8

### ExecutorInitParams: Structured Hook Configuration

The constructor now takes a structured parameter object:
```csharp
public Executor(ExternalProcessMemory memory, ExecutorInitParams initParams)
```

Where `ExecutorInitParams` contains:
- `Type` — `ExecutorInitParamsHookType` enum: 1=DX9 EndScene, 2=DX11 Present
- `HookAddress` — `IntPtr` target address
- `Veh` — `bool` enable VEH
- `Patches` — `IReadOnlyCollection<ExecutorPatch>` additional memory patches

### ExecutorPatch: Multi-Address Hooking

New `ExecutorPatch.cs` enables patching multiple addresses per hook:
```csharp
public ExecutorPatch(IntPtr address, bool code, byte[] patchBytes)
```

Each patch stores:
- `address` — Remote address to patch
- `code` — Whether this is a code region (affects cache flushing)
- `patchBytes` — Bytes to write

In `InitializeDetour()`, the data block grows to accommodate patches:
```
Offset 40:    executedAnything flag
Offset 44+:   Per-patch data (origBytes pointer, patchBytes pointer, length, address, isCode flag)
```

When VEH is active, `InitializeDetour()` additionally resolves:
- `WriteProcessMemory` — For in-process patching from WoW's own context
- `FlushInstructionCache` — To ensure CPU sees the new code

This is significant: instead of just hooking EndScene, the bot can now patch arbitrary addresses inside WoW while running in the EndScene frame context.

### AsmRandomizer: Advanced Polymorphism

`AsmRandomizer.cs` is a major upgrade over the simple `AddRandomLine()`:

```csharp
// Constant splitting: turns "mov eax, 0x12345678" into multiple ADD instructions
public static uint[][] GenerateSplits(uint constant)
{
    // Splits into 1-3 random parts that sum to the original
    // Each generation produces different splits
}

// Register shuffling: uses different scratch registers each time
public class RegisterSet
{
    // Classifies registers as scratch vs. preserved
    // Randomly selects which register to use for each operation
}

// Random NOP insertion
public static void AddRandomNop(ManagedFasm asm)
{
    // Inserts semantically neutral instructions (xor reg,0; add reg,0; etc.)
}

// Random string generation for labels
public static string RandomString(Random random, int minLen, int maxLen)
```

This means every injected payload is polymorphic at three levels:
1. **Different register allocation** each time
2. **Constants split differently** each time (e.g., `push 0x1234` becomes `push X; add [esp], Y`)
3. **Random NOP padding** between instructions

### Anti-Detection Assessment (6.2.3)

| Technique | Status | Evidence |
|-----------|--------|----------|
| VEH exception handler | **YES** | Same TLS + VEH gate as 5.4.8 |
| AsmRandomizer | **YES — MAJOR UPGRADE** | Constant splitting, register shuffling, random NOPs |
| ExecutorPatch | **YES — NEW** | Multi-address code patching in WoW's context |
| In-process WriteProcessMemory | **YES — NEW** | Patches applied from WoW's own process context |
| FlushInstructionCache | **YES — NEW** | Proper code cache invalidation |
| Memory protection hardening | **NO** | Still PAGE_EXECUTE_READWRITE \| PAGE_NOCACHE |
| VMT swap | **NO** | Still 5-byte JMP overwrite |
| HWBP | **NO** | No debug register manipulation |

---

## 5. HB Legion — Legion (Obfuscation Peak, Same Architecture)

### Source Files
- `.hb legion/GreyMagic/Executor.cs` — Heavily obfuscated version of 6.2.3
- `.hb legion/GreyMagic/DirectX.cs` — Same DX9/DX11 pattern
- `.hb legion/GreyMagic/ExecutorPatch.cs` — Same structure, obfuscated names

### Obfuscation Changes

The Legion decompilation shows heavy obfuscation (likely Confuser/ConfuserEx):

| 6.2.3 Name | Legion Name |
|-------------|-------------|
| `ExecutorInitParamsHookType` | `Enum450` |
| `ExecutorPatch` | Same name but fields are `intptr_0`, `bool_0`, `byte_0` |
| Named enum values | Numeric constants |
| Method names | `method_XX()` pattern |
| Field names | `intptr_0` through `intptr_27` |

### New Addition: Udis86Net Disassembler

Legion adds `Udis86Net` for runtime disassembly, likely for:
- Analyzing hook target prologues to determine safe copy-byte counts
- Validating that patched code hasn't been modified by Warden

### Architectural Assessment

The underlying architecture is **identical to 6.2.3**:
- Same VEH + TLS gate system
- Same AsmRandomizer with constant splitting and register shuffling
- Same ExecutorPatch multi-address system
- Same DX9/DX11 support via native C++ helper
- Same memory protection values (576 for injected code)

The only meaningful change is the obfuscation layer on the C# side, making static analysis of the bot binary harder (but not the injected ASM, which is generated at runtime).

---

## Architectural Evolution Timeline

```
3.3.5a (2010)           4.3.4 (2012)           5.4.8 (2013)           6.2.3 (2015)           Legion (2016)
─────────────────────────────────────────────────────────────────────────────────────────────────────────────
BlueMagic               BlueMagic               GreyMagic              GreyMagic              GreyMagic
│                       │                       │                      │                      │
├─uint addresses        ├─same                  ├─IntPtr (64-bit)      ├─same                 ├─same
├─DX9 only              ├─same                  ├─DX9 + DX11           ├─same                 ├─same
├─C# FASM only          ├─same                  ├─C++/CLI native       ├─same                 ├─+ Udis86Net
│                       │                       │                      │                      │
├─SuspendThread init    ├─same                  ├─SuspendThread init   ├─same                 ├─same
├─5-byte JMP hook       ├─same                  ├─5-byte JMP hook      ├─same                 ├─same
├─NO VEH                ├─same                  ├─TLS + VEH gate       ├─same                 ├─same
│                       │                       │                      │                      │
├─Random event names    ├─same                  ├─same                 ├─same                 ├─same
├─ExecutorRand option   ├─same                  ├─AddRandomLine()      ├─AsmRandomizer        ├─same (obfusc)
├─PAGE_RWX_NC           ├─same                  ├─same + XOR timing    ├─same + Patches       ├─same
│                       │                       │                      │  + WPM/FIC            │
└─NO VMT, NO HWBP       └─same                  └─same                 └─same                 └─same
```

---

## Answering the Research Questions

### Q1: Why did HB move from "internal" to "external" patterns?

**It didn't.** This is a common misconception. HonorBuddy was ALWAYS an external-process bot across all five versions examined. Every version:

- Opens WoW's process via `OpenProcess(2035711U)` (PROCESS_ALL_ACCESS)
- Opens WoW's main thread via `OpenThread(2032639U)` (THREAD_ALL_ACCESS)
- Allocates memory in WoW via `VirtualAllocEx()`
- Reads/writes via `ReadProcessMemory()` / `WriteProcessMemory()`
- Assembles x86 shellcode via ManagedFasm

The confusion likely arises because HB's hook runs **inside** WoW's process context (on WoW's main thread during EndScene), making it appear "internal." But the orchestration is always external.

### Q2: What hooking/memory techniques did each version use?

**All versions used the same fundamental hook**: a 5-byte JMP (opcode E9) overwriting the prologue of `IDirect3DDevice9::EndScene` (and starting from 5.4.8, `IDXGISwapChain::Present`).

The hook payload evolved:

1. **3.3.5a/4.3.4**: Direct trampoline — `pushad` → check event → call code → signal → wait → `popad` → original prologue → JMP back
2. **5.4.8+**: VEH-gated trampoline — same flow but execution passes through a Vectored Exception Handler that validates the calling thread via TLS before allowing code execution. The VEH redirects CONTEXT.Eip rather than making a direct CALL.

Original prologue bytes (`mov edi, edi; push ebp; mov ebp, esp` — the "hotpatch" prologue) were saved in `Dispose()` for clean unhooking.

### Q3: How did Warden evasion evolve?

HonorBuddy's approach was **structural obfuscation** rather than **active Warden countermeasures**:

| Era | Evasion Strategy | Weakness |
|-----|-----------------|----------|
| 3.3.5a/4.3.4 | Random event names, optional polymorphic padding | Fixed 5-byte JMP at known address; static memory pages; no VEH |
| 5.4.8 | + VEH gate (execution appears as exception recovery), + TLS thread validation, + XOR-encrypted timing | Still modifies EndScene bytes; PAGE_RWX pages are suspiciously executable+writable |
| 6.2.3 | + AsmRandomizer (constant splitting, register shuffling), + ExecutorPatch (multi-site patching), + in-process WriteProcessMemory | Same JMP byte modification at EndScene; same RWX pages |
| Legion | + Binary obfuscation (Confuser), + Udis86Net disassembler | Same fundamental architecture |

**Notable absences across ALL versions:**
- **No RWX→RX toggle**: Memory pages were always `PAGE_EXECUTE_READWRITE | PAGE_NOCACHE`. Never toggled to read-execute after writing.
- **No VMT swap/redirect**: Always used inline 5-byte JMP. Never modified the DirectX vtable.
- **No HWBP (hardware breakpoints)**: Never set DR0-DR3 for hooking. Only read debug registers via `GetThreadContext`.
- **No Warden-specific code**: No scan-result spoofing, no Warden module interception, no memory-scan evasion. The bot relied purely on making its injected code "look different each time" and hiding the execution path behind VEH.

---

## Assessment: CopilotBuddy vs. HonorBuddy Techniques

The CopilotBuddy anti-detection plan proposes techniques that **go significantly beyond** what HonorBuddy ever implemented:

| Technique | HB Status | CopilotBuddy Plan |
|-----------|-----------|-------------------|
| RWX → RX memory toggle | Never implemented (always RWX) | Phase 1 priority |
| VMT swap hook | Never implemented (always JMP) | Phase 2 option |
| HWBP hook | Never implemented | Phase 3 option |
| GetThreadContext spoofing | Never implemented | Phase 3 consideration |
| Polymorphic ASM | Yes (from 3.3.5a) | Inherited and improved |
| VEH gate | Yes (from 5.4.8) | Inherited |
| Random event names | Yes (all versions) | Inherited |

HonorBuddy's eventual detection and Blizzard lawsuits suggest that its structural-obfuscation-only approach was ultimately insufficient. The techniques CopilotBuddy adds (especially RWX→RX and VMT swap) address the specific weaknesses that Warden likely exploited: scanning for modified EndScene bytes and detecting RWX memory pages in WoW's address space.
