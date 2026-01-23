# Memory Class

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - Memory operations work identically across all WoW versions.

The `Memory` class provides low-level memory access to the WoW process. It handles reading/writing memory, allocating memory, executing assembly code, and managing threads.

## Namespace

```csharp
using GreenMagic;
```

## Overview

The `Memory` class is the foundation for all memory manipulation in CopilotBuddy. It provides:

- **Process Attachment**: Opens and attaches to the WoW process
- **Memory Reading**: Read primitive types, structures, arrays, and strings from process memory
- **Memory Writing**: Write data back to process memory
- **Memory Allocation**: Allocate and free memory within the WoW process
- **Thread Control**: Suspend, resume, and create remote threads
- **Assembly Execution**: Execute x86 assembly code using the integrated `ManagedFasm` assembler

!!! danger "Advanced Usage Only"
    Direct memory manipulation can crash the game or corrupt data. Most users should use the higher-level APIs like `ObjectManager`, `LocalPlayer`, and `WoWUnit` instead.

---

## Constructor

### `Memory(int processId)`

Creates a new Memory instance attached to the specified process.

```csharp
// Get WoW process ID
int pid = Process.GetProcessesByName("Wow")[0].Id;

// Attach to the process
Memory memory = new Memory(pid);
```

**Parameters:**
- `processId`: The process ID to attach to (use 0 to skip attachment)

**Initialization:**
- Opens process handle with full access rights
- Opens main thread handle
- Initializes inline assembler (`ManagedFasm`)
- Parses PE header for ASLR detection
- Calculates image base address

---

## Properties

### Process Information

| Property | Type | Description |
|----------|------|-------------|
| `ProcessId` | `int` | The attached process ID |
| `ProcessHandle` | `IntPtr` | Native handle to the process |
| `ThreadHandle` | `IntPtr` | Native handle to the main thread |
| `WindowHandle` | `IntPtr` | Window handle (HWND) |
| `Process` | `Process` | Managed `Process` object |
| `IsProcessOpen` | `bool` | True if process handle is valid |
| `IsThreadOpen` | `bool` | True if thread handle is valid |

### Utilities

| Property | Type | Description |
|----------|------|-------------|
| `Asm` | `ManagedFasm` | Inline x86 assembler for code injection |
| `PatchManager` | `PatchManager` | Manages memory patches |
| `PeHeaderParser` | `PeHeaderParser` | PE header information |

---

## Reading Memory

### Basic Read Methods

#### `T Read<T>(params uint[] addresses)`

Reads a value of type `T` from memory. Supports pointer chains.

```csharp
// Read a single value
float health = memory.Read<float>(0x12345678);

// Read through pointer chain
uint value = memory.Read<uint>(0x400000, 0x10, 0x20);
// Equivalent to: *(uint*)((*(uint*)(0x400000 + 0x10)) + 0x20)
```

**Supported Types:**
- Primitives: `byte`, `int`, `uint`, `float`, `double`, `bool`, etc.
- `string` (null-terminated UTF-8)
- Any `struct` with `StructLayout` attribute

#### `T ReadRelative<T>(params uint[] addresses)`

Reads using relative addresses (adds image base automatically).

```csharp
// WoW 3.3.5a base address is 0x400000
// This reads from 0x400000 + 0xD3F92C = 0xD7F92C
uint value = memory.ReadRelative<uint>(0xD3F92C);
```

!!! tip "ASLR Handling"
    `ReadRelative` automatically handles ASLR (Address Space Layout Randomization). If ASLR is enabled, it adjusts addresses based on the actual module base.

#### `T ReadStruct<T>(uint address) where T : struct`

Reads a structure from memory.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct Vector3
{
    public float X, Y, Z;
}

Vector3 position = memory.ReadStruct<Vector3>(0x12345678);
```

### Array Reading

#### `T[] ReadStructArray<T>(uint address, int elements) where T : struct`

Reads an array of structures.

```csharp
// Read 10 floats
float[] values = memory.ReadStructArray<float>(address, 10);
```

### Byte Reading

#### `byte[] ReadBytes(uint address, int count)`

Reads raw bytes from memory.

```csharp
byte[] data = memory.ReadBytes(0x12345678, 256);
```

#### `void ReadBytes(uint address, byte[] buffer)`

Reads bytes directly into an existing buffer (unsafe).

```csharp
byte[] buffer = new byte[512];
memory.ReadBytes(0x12345678, buffer);
```

### String Reading

#### `string ReadString(Encoding encoding, uint address, int maxLength = 256)`

Reads a null-terminated string.

```csharp
// UTF-8 string
string name = memory.ReadString(Encoding.UTF8, 0x12345678);

// ASCII string with max length
string text = memory.ReadString(Encoding.ASCII, address, 128);
```

#### `string ReadString(uint address)`

Reads a UTF-8 string with default max length (256 bytes).

```csharp
string name = memory.ReadString(0x12345678);
```

---

## Writing Memory

### Basic Write Methods

#### `bool Write<T>(uint address, T value)`

Writes a value to memory. Returns `true` on success.

```csharp
// Write an integer
memory.Write<int>(0x12345678, 42);

// Write a float
memory.Write<float>(address, 3.14f);

// Write a boolean
memory.Write<bool>(address, true);
```

**Supported Types:**
- All primitive types
- `string` (null-terminated)
- `byte[]` arrays
- Custom structures

#### `bool WriteStruct<T>(uint address, T value) where T : struct`

Writes a structure to memory.

```csharp
Vector3 pos = new Vector3 { X = 100, Y = 200, Z = 300 };
memory.WriteStruct(address, pos);
```

### Byte Writing

#### `int WriteBytes(uint address, byte[] data)`

Writes raw bytes to memory. Returns number of bytes written.

```csharp
byte[] patch = new byte[] { 0x90, 0x90, 0x90 }; // NOP NOP NOP
int written = memory.WriteBytes(0x12345678, patch);
```

---

## Memory Allocation

### `uint AllocateMemory(int size)`

Allocates executable memory within the WoW process.

```csharp
// Allocate 4096 bytes (default)
uint buffer = memory.AllocateMemory(4096);

// Use the buffer
memory.Write(buffer, "Hello World");

// Free when done
memory.FreeMemory(buffer);
```

**Default Flags:**
- `MEM_COMMIT` (0x1000): Commits the memory
- `PAGE_EXECUTE_READWRITE` (0x40): Allows read, write, and execute

### `uint AllocateMemory(int size, uint allocationType, uint protect)`

Allocates memory with custom flags.

```csharp
// Allocate read-only memory
uint buffer = memory.AllocateMemory(1024, 0x1000, 0x02); // PAGE_READONLY
```

### `bool FreeMemory(uint address)`

Frees previously allocated memory.

```csharp
memory.FreeMemory(buffer);
```

---

## Thread Control

### Suspend/Resume

#### `uint SuspendThread()`

Suspends the WoW main thread. Returns previous suspend count.

```csharp
memory.SuspendThread();
// Do thread-sensitive operations
memory.ResumeThread();
```

!!! warning "Thread Safety"
    Always resume threads after suspending. Forgetting to resume will freeze the game.

#### `uint ResumeThread()`

Resumes a suspended thread.

### Remote Thread Creation

#### `IntPtr CreateRemoteThread(uint startAddress, uint parameter)`

Creates and starts a new thread in the WoW process.

```csharp
uint threadId;
IntPtr hThread = memory.CreateRemoteThread(functionAddress, 0, out threadId);

// Wait for thread completion
memory.WaitForSingleObject(hThread, 5000); // 5 second timeout

// Get exit code
uint exitCode = memory.GetExitCodeThread(hThread);
```

**Common Uses:**
- Calling WoW functions from a new thread
- Injecting DLLs (`LoadLibrary` address as start address)
- Background processing

---

## Assembly Execution

### Using ManagedFasm

The `Memory` class includes an integrated x86 assembler for executing assembly code.

```csharp
// Get the assembler
var asm = memory.Asm;

lock (asm.AssemblyLock)
{
    // Clear previous code
    asm.Clear();
    
    // Add assembly instructions
    asm.AddLine("mov eax, {0}", 42);
    asm.AddLine("mov ecx, {0}", someAddress);
    asm.AddLine("call {0}", functionAddress);
    asm.AddLine("retn");
    
    // Execute the code
    asm.Execute();
    
    // Read return value (EAX)
    int result = memory.Read<int>(asm.ReturnPointer);
}
```

!!! example "Calling WoW Functions"
    ```csharp
    // Call CGPlayer_C::ClickToMove
    lock (executor.AssemblyLock)
    {
        executor.Clear();
        executor.AddLine("mov ecx, {0}", LocalPlayer.BaseAddress);
        executor.AddLine("push {0}", (uint)ClickToMoveType.Move);
        executor.AddLine("push 0"); // GUID low
        executor.AddLine("push 0"); // GUID high
        executor.AddLine("push {0}", BitConverter.ToUInt32(BitConverter.GetBytes(z), 0));
        executor.AddLine("push {0}", BitConverter.ToUInt32(BitConverter.GetBytes(y), 0));
        executor.AddLine("push {0}", BitConverter.ToUInt32(BitConverter.GetBytes(x), 0));
        executor.AddLine("call {0}", (uint)GlobalOffsets.CGPlayer_C__ClickToMove);
        executor.AddLine("retn");
        executor.Execute();
    }
    ```

---

## Utility Methods

### Address Calculation

#### `uint GetAbsolute(uint relative)`

Converts a relative address to an absolute address.

```csharp
// WoW 3.3.5a default image base is 0x400000
uint absolute = memory.GetAbsolute(0xD3F92C);
// Result: 0xD7F92C (if no ASLR)
```

### Module Information

#### `ProcessModule GetModule(string moduleName)`

Gets a loaded module by name.

```csharp
ProcessModule wowModule = memory.GetModule("Wow.exe");
Console.WriteLine($"Base: {wowModule.BaseAddress:X8}");
Console.WriteLine($"Size: {wowModule.ModuleMemorySize} bytes");
```

---

## Memory Safety

### Best Practices

1. **Always validate addresses before reading/writing**
   ```csharp
   if (address != 0 && address > 0x10000)
   {
       value = memory.Read<int>(address);
   }
   ```

2. **Use try-catch for memory operations**
   ```csharp
   try
   {
       int value = memory.Read<int>(address);
   }
   catch (Exception ex)
   {
       Logging.WriteException(ex);
   }
   ```

3. **Free allocated memory**
   ```csharp
   uint buffer = memory.AllocateMemory(1024);
   try
   {
       // Use buffer
   }
   finally
   {
       memory.FreeMemory(buffer);
   }
   ```

4. **Lock during assembly execution**
   ```csharp
   lock (memory.Asm.AssemblyLock)
   {
       // Assembly code here
   }
   ```

---

## Complete Example

```csharp
using System;
using System.Diagnostics;
using GreenMagic;

// Attach to WoW
Process[] procs = Process.GetProcessesByName("Wow");
if (procs.Length == 0)
{
    Console.WriteLine("WoW not running!");
    return;
}

Memory memory = new Memory(procs[0].Id);

// Read player health
uint healthOffset = 0xBD8;
float health = memory.Read<float>(LocalPlayer.BaseAddress + healthOffset);
Console.WriteLine($"Health: {health}");

// Allocate and use memory
uint buffer = memory.AllocateMemory(256);
try
{
    // Write a string
    memory.Write(buffer, "CopilotBuddy");
    
    // Read it back
    string text = memory.ReadString(buffer);
    Console.WriteLine($"Buffer contains: {text}");
}
finally
{
    memory.FreeMemory(buffer);
}

// Clean up
memory.Dispose();
```

---

## See Also

- [ObjectManager](objectmanager.md) - High-level object access (uses Memory internally)
- [Lua](lua.md) - Safe alternative for many operations
- [WoWObject](../wowobjects/wowobject.md) - Base object class using descriptors
