using System;
using System.Diagnostics;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using GreenMagic.Native;

namespace GreenMagic
{
    /// <summary>
    /// ExecutorRand - Executor with polymorphic ASM code generation.
    /// Adds random NOP/PUSH/POP/XOR instructions between real code to vary signatures.
    /// Same functionality as Executor but with anti-detection randomization.
    /// </summary>
    public sealed class ExecutorRand : IDisposable
    {
        public Memory Memory { get; private set; }
        private readonly Random m_Random;
        private readonly object thisLock = new object();
        public object AssemblyLock = new object();

        // EventWaitHandles for synchronization
        private EventWaitHandle m_InjectionWaitingEvent;
        private EventWaitHandle m_InjectionContinueEvent;
        public EventWaitHandle m_InjectionFinishedEvent;
        private string m_InjectionWaitingEventName;
        private string m_InjectionContinueEventName;
        private string m_InjectionFinishedEventName;

        // Memory addresses in WoW process
        private uint m_OrigEndScene;
        private uint m_EndSceneDetour;
        private uint m_InjectedCode;
        private uint m_DataPtr;
        private uint m_ReturnedDataPtr;
        private uint m_InjectionWaitingHandlePtr;
        private uint m_InjectionContinueHandlePtr;
        private uint m_InjectionFinishedHandlePtr;

        // Kernel32 function addresses
        private UIntPtr WaitForSingleObject;
        private UIntPtr CreateEventA;
        private UIntPtr ResetEvent;
        private UIntPtr SetEvent;

        // State
        private bool m_ContinuousExecution;
        private bool m_FirstExecution;
        private byte[] m_ClearBytes = new byte[4096];
        private byte[]? m_OriginalEndSceneBytes;
        private bool m_Disposed;

        public bool IsOpen => Memory != null && Memory.IsProcessOpen && Memory.IsThreadOpen;
        public bool IsInitialized { get; private set; }
        public uint DataPointer => m_DataPtr;
        public uint ReturnPointer => m_ReturnedDataPtr;
        public uint InjectCodePointer => m_InjectedCode;

        public ExecutorRand(Memory memory, uint endSceneAddress)
        {
            if (memory == null || !memory.IsProcessOpen)
                throw new ArgumentNullException(nameof(memory), "Memory is null or process not open");

            if (endSceneAddress == 0)
                throw new ArgumentException("EndScene address is null");

            // Verify EndScene is valid
            uint[] testRead = new uint[] { endSceneAddress };
            if (memory.Read<uint>(testRead) == 0)
                throw new ArgumentException("EndScene address is invalid");

            Memory = memory;
            Memory.Asm.SetMemorySize(65536);
            m_OrigEndScene = endSceneAddress;

            // Initialize random with crypto seed
            byte[] seed = new byte[4];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetNonZeroBytes(seed);
            }
            m_Random = new Random(BitConverter.ToInt32(seed, 0));

            bool flag = false;
            string text = Environment.UserDomainName + "\\" + Environment.UserName;
            EventWaitHandleSecurity eventWaitHandleSecurity = new EventWaitHandleSecurity();
            EventWaitHandleAccessRule eventWaitHandleAccessRule = new EventWaitHandleAccessRule(text, EventWaitHandleRights.FullControl, AccessControlType.Allow);
            // Also add Everyone and SYSTEM for cross-process IPC
            eventWaitHandleSecurity.AddAccessRule(eventWaitHandleAccessRule);
            eventWaitHandleSecurity.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null), EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify, AccessControlType.Allow));
            eventWaitHandleSecurity.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), EventWaitHandleRights.FullControl, AccessControlType.Allow));
            
            m_InjectionWaitingEventName = "Global\\" + GetRandomString(16);
            System.Diagnostics.Debug.WriteLine($"[ExecutorRand] Creating WaitingEvent: {m_InjectionWaitingEventName}");
            m_InjectionWaitingEvent = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, m_InjectionWaitingEventName, out flag, eventWaitHandleSecurity);
            if (!flag)
            {
                throw new Exception("You should never see this message, but the event was opened instead of created!  That's bad!");
            }
            m_InjectionContinueEventName = "Global\\" + GetRandomString(16);
            System.Diagnostics.Debug.WriteLine($"[ExecutorRand] Creating ContinueEvent: {m_InjectionContinueEventName}");
            m_InjectionContinueEvent = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, m_InjectionContinueEventName, out flag, eventWaitHandleSecurity);
            if (!flag)
            {
                throw new Exception("You should never see this message, but the event was opened instead of created!  That's bad!");
            }
            m_InjectionFinishedEventName = "Global\\" + GetRandomString(16);
            System.Diagnostics.Debug.WriteLine($"[ExecutorRand] Creating FinishedEvent: {m_InjectionFinishedEventName}");
            m_InjectionFinishedEvent = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, m_InjectionFinishedEventName, out flag, eventWaitHandleSecurity);
            if (!flag)
            {
                throw new Exception("You should never see this message, but the event was opened instead of created!  That's bad!");
            }

            InitializeDetour();
            Clear();
            IsInitialized = true;
        }

        public ExecutorRand(int processId, uint endSceneAddress)
            : this(new Memory(processId), endSceneAddress)
        {
        }

        #region Execution Control

        public void BeginExecute()
        {
            lock (thisLock)
            {
                if (m_ContinuousExecution)
                    EndExecute();

                m_ContinuousExecution = true;
                m_FirstExecution = true;
            }
        }

        public void EndExecute()
        {
            lock (thisLock)
            {
                m_ContinuousExecution = false;
                m_FirstExecution = false;
                m_InjectionWaitingEvent.Reset();
                m_InjectionContinueEvent.Set();
            }
        }

        public void Execute()
        {
            if (!IsOpen || !IsInitialized)
                throw new Exception("Cannot execute code while process is not opened and/or Executor is not initialized.");
            lock (thisLock)
            {
                Memory.Asm!.Inject(m_InjectedCode);
                if (m_ContinuousExecution)
                {
                    if (m_FirstExecution)
                    {
                        m_InjectionContinueEvent.Reset();
                        m_InjectionWaitingEvent.Set();
                        if (!m_InjectionFinishedEvent.WaitOne(10000, false))
                            throw new Exception("Process must have frozen or gotten out of sync; InjectionFinishedEvent was never fired.");
                        m_FirstExecution = false;
                    }
                    else
                    {
                        m_InjectionWaitingEvent.Set();
                        m_InjectionContinueEvent.Set();
                        if (!m_InjectionFinishedEvent.WaitOne(10000, false))
                            throw new Exception("Process must have frozen or gotten out of sync; InjectionFinishedEvent was never fired.");
                    }
                }
                else
                {
                    m_InjectionWaitingEvent.Set();
                    if (!m_InjectionFinishedEvent.WaitOne(10000, false))
                        throw new Exception("Process must have frozen or gotten out of sync; InjectionFinishedEvent was never fired.");
                    m_InjectionWaitingEvent.Reset();
                    m_InjectionContinueEvent.Set();
                }
            }
        }

        public void GrabFrame()
        {
            Memory.Write<byte>(m_InjectedCode, 195);
            m_InjectionWaitingEvent.Set();
            if (!m_InjectionFinishedEvent.WaitOne(10000, false))
                throw new Exception("Process must have frozen or gotten out of sync; InjectionFinishedEvent was never fired.");
            m_InjectionWaitingEvent.Reset();
            m_InjectionContinueEvent.Set();
        }

        #endregion

        #region ASM Building

        public void Clear() => Memory.Asm.Clear();
        public void AddLine(string line) => Memory.Asm.AddLine(line);
        public void AddLine(string format, params object[] args) => Memory.Asm.AddLine(format, args);

        /// <summary>
        /// Add line with random padding instructions before and after.
        /// </summary>
        public void AddRandomLine(string line)
        {
            AddRandomLine(line, Array.Empty<object>());
        }

        public void AddRandomLine(int numRandomLines, string line)
        {
            AddRandomLine(numRandomLines, line, Array.Empty<object>());
        }

        public void AddRandomLine(string format, params object[] args)
        {
            AddRandomLine(m_Random.Next(2, 5), format, args);
        }

        public void AddRandomLine(int numRandomLines, string format, params object[] args)
        {
            if (numRandomLines <= 1)
                numRandomLines = 2;

            int before = numRandomLines / 2;
            int after = before + numRandomLines % 2;

            RandomLine(before);
            AddLine(format, args);
            RandomLine(after);
        }

        /// <summary>
        /// Insert random NOP/PUSH/POP/XOR instructions.
        /// </summary>
        private void RandomLine(int numLines)
        {
            while (numLines-- > 0)
            {
                string r32 = GetRandomR32();
                string r16 = GetRandomR16();
                int type = m_Random.Next(1, 6);

                switch (type)
                {
                    case 1:
                        for (int i = m_Random.Next(1, 4); i > 0; i--) AddLine("nop");
                        break;
                    case 2:
                        AddLine("mov {0}, {0}", r32);
                        break;
                    case 3:
                        AddLine("mov {0}, {0}", r16);
                        break;
                    case 4:
                        AddLine("push {0}", r16);
                        AddLine("pop {0}", r16);
                        break;
                    case 5:
                        AddLine("push {0}", r32);
                        AddLine("pop {0}", r32);
                        break;
                }
            }
        }

        private string GetRandomR32()
        {
            return m_Random.Next(1, 7) switch
            {
                1 => "eax",
                2 => "ebx",
                3 => "ecx",
                4 => "edx",
                5 => "edi",
                6 => "esi",
                _ => "eax"
            };
        }

        private string GetRandomR16()
        {
            return m_Random.Next(1, 7) switch
            {
                1 => "ax",
                2 => "bx",
                3 => "cx",
                4 => "dx",
                5 => "di",
                6 => "si",
                _ => "ax"
            };
        }

        #endregion

        #region Detour Setup

        private void InitializeDetour()
        {
            if (!IsOpen) throw new Exception("Process is not open for memory manipulation.");
            int size = m_ClearBytes.Length;
            if (Memory.GetModule("d3d9.dll") == null)
                throw new Exception("Executor can only be used on processes that use DirectX9.");

            IntPtr k32 = Imports.GetModuleHandleW("kernel32.dll");
            if (k32 == IntPtr.Zero) throw new Exception("GetModuleHandleW(kernel32) failed");

            WaitForSingleObject = Imports.GetProcAddress(k32, "WaitForSingleObject");
            CreateEventA        = Imports.GetProcAddress(k32, "CreateEventA");
            ResetEvent          = Imports.GetProcAddress(k32, "ResetEvent");
            SetEvent            = Imports.GetProcAddress(k32, "SetEvent");

            if (WaitForSingleObject == UIntPtr.Zero ||
                CreateEventA == UIntPtr.Zero ||
                ResetEvent == UIntPtr.Zero ||
                SetEvent == UIntPtr.Zero)
                throw new Exception("Failed to resolve required kernel32 exports");

            uint eventStub = Memory.AllocateMemory(size, 0x1000, 0x20); // PAGE_EXECUTE_READ
            if (eventStub == 0) throw new Exception("Allocate event stub failed");
            uint namesMem = Memory.AllocateMemory(size, 0x1000, 0x04); // PAGE_READWRITE
            if (namesMem == 0) throw new Exception("Allocate event names failed");

            uint name2 = namesMem + (uint)m_InjectionWaitingEventName.Length + 4;
            uint name3 = name2 + (uint)m_InjectionContinueEventName.Length + 4;
            if (!Memory.Write(namesMem, m_InjectionWaitingEventName) ||
                !Memory.Write(name2, m_InjectionContinueEventName) ||
                !Memory.Write(name3, m_InjectionFinishedEventName))
            {
                throw new Exception("Could not write event names to memory.");
            }

            m_EndSceneDetour = Memory.AllocateMemory(size, 0x1000, 0x20);
            if (m_EndSceneDetour == 0) throw new Exception("Allocate detour failed");
            m_InjectedCode = Memory.AllocateMemory(size, 0x1000, 0x240); // PAGE_EXECUTE_READWRITE | PAGE_NOCACHE
            if (m_InjectedCode == 0) throw new Exception("Allocate injected code failed");
            m_DataPtr = Memory.AllocateMemory(size, 0x1000, 0x04);
            if (m_DataPtr == 0) throw new Exception("Allocate data failed");

            // Sauvegarder les bytes originaux de EndScene pour le prologue
            m_OriginalEndSceneBytes = Memory.ReadBytes(m_OrigEndScene, 6);
            System.Diagnostics.Debug.WriteLine($"[InitializeDetour] Original EndScene bytes: {(m_OriginalEndSceneBytes != null ? BitConverter.ToString(m_OriginalEndSceneBytes) : "null")}");
            if (m_OriginalEndSceneBytes == null)
                throw new Exception("Failed to read original EndScene bytes.");

            m_InjectionWaitingHandlePtr  = m_DataPtr;
            m_InjectionContinueHandlePtr = m_DataPtr + 4;
            m_InjectionFinishedHandlePtr = m_DataPtr + 8;
            m_ReturnedDataPtr            = m_DataPtr + 12;

            if (!InjectEventStub(eventStub, namesMem, name2, name3))
                throw new Exception("InjectEventStub failed");

            InjectDetour();
            Memory.FreeMemory(eventStub);
            Memory.FreeMemory(namesMem);
        }

        private void InjectDetour()
        {
            Clear();
            AddRandomLine("pushad");
            AddRandomLine("@CheckInjection:");
            AddRandomLine("mov eax, [{0}]", m_InjectionWaitingHandlePtr);
            AddRandomLine("push 0");
            AddRandomLine("push eax");
            AddRandomLine("call {0}", WaitForSingleObject);
            AddRandomLine("test eax, eax");
            AddRandomLine("jnz @NoInjection");
            AddRandomLine("@Injection:");
            AddRandomLine("call {0}", m_InjectedCode);
            AddRandomLine("mov [{0}], eax", m_ReturnedDataPtr);
            AddRandomLine("mov eax, [{0}]", m_InjectionFinishedHandlePtr);
            AddRandomLine("push eax");
            AddRandomLine("call {0}", SetEvent);
            AddRandomLine("mov eax, [{0}]", m_InjectionContinueHandlePtr);
            AddRandomLine("push 1000");
            AddRandomLine("push eax");
            AddRandomLine("call {0}", WaitForSingleObject);
            AddRandomLine("test eax, eax");
            AddRandomLine("jz @CheckInjection");
            AddRandomLine("mov eax, [{0}]", m_InjectionWaitingHandlePtr);
            AddRandomLine("push eax");
            AddRandomLine("call {0}", ResetEvent);
            AddLine("@NoInjection:");
            AddRandomLine("popad");
            
            // Restore original EndScene prologue based on captured bytes
            // WoW 3.3.5a EndScene has two common prologues:
            // 1) 55 8B EC 8B 45 08 = push ebp; mov ebp,esp; mov eax,[ebp+8] (6 bytes before our JMP)
            // 2) 8B FF 55 8B EC = mov edi,edi; push ebp; mov ebp,esp (5 bytes before our JMP)
            var prolog = m_OriginalEndSceneBytes;
            if (prolog != null && prolog.Length >= 6)
            {
                Debug.WriteLine($"[InjectDetour] Prologue bytes: {BitConverter.ToString(prolog)}");
                
                if (prolog[0] == 0x55 && prolog[1] == 0x8B && prolog[2] == 0xEC)
                {
                    // Prologue: push ebp; mov ebp,esp; mov eax,[ebp+8]
                    Debug.WriteLine("[InjectDetour] Using 6-byte prologue (55 8B EC 8B 45 08)");
                    AddLine("push ebp");
                    AddLine("mov ebp, esp");
                    AddLine("mov eax, [ebp+8]");
                    AddLine("jmp {0}", m_OrigEndScene + 6);
                }
                else
                {
                    // Prologue: mov edi,edi; push ebp; mov ebp,esp
                    Debug.WriteLine("[InjectDetour] Using 5-byte prologue (8B FF 55 8B EC)");
                    AddLine("mov edi, edi");
                    AddLine("push ebp");
                    AddLine("mov ebp, esp");
                    AddLine("jmp {0}", m_OrigEndScene + 5);
                }
            }
            else
            {
                // Fallback to standard 5-byte prologue
                Debug.WriteLine("[InjectDetour] FALLBACK: Using standard 5-byte prologue");
                AddLine("mov edi, edi");
                AddLine("push ebp");
                AddLine("mov ebp, esp");
                AddLine("jmp {0}", m_OrigEndScene + 5);
            }
            
            if (!Memory.Asm!.Inject(m_EndSceneDetour))
                throw new Exception("Failed to inject detour trampoline");
                
            // Install 5-byte JMP at original EndScene to redirect to our detour
            Clear();
            AddLine("jmp {0}", m_EndSceneDetour);
            Debug.WriteLine($"[InjectDetour] Installing JMP at EndScene 0x{m_OrigEndScene:X8} -> 0x{m_EndSceneDetour:X8}");
            
            if (!Memory.Asm!.Inject(m_OrigEndScene))
                throw new Exception("Failed to inject JMP at EndScene");
                
            Debug.WriteLine("[InjectDetour] Detour installed successfully");
        }

        private bool InjectEventStub(uint eventStub, uint waitNamePtr, uint contNamePtr, uint finNamePtr)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[InjectEventStub] eventStub=0x{eventStub:X8}, CreateEventA=0x{CreateEventA:X}");
                System.Diagnostics.Debug.WriteLine($"[InjectEventStub] m_InjectionWaitingHandlePtr=0x{m_InjectionWaitingHandlePtr:X8}");
                
                Clear();
                AddRandomLine("push {0}", waitNamePtr);
                AddRandomLine("push 0");
                AddRandomLine("push 0");
                AddRandomLine("push 0");
                AddRandomLine("call {0}", CreateEventA);
                AddRandomLine("test eax, eax");
                AddRandomLine("jz @ReturnFalse");
                AddRandomLine("mov [{0}], eax", m_InjectionWaitingHandlePtr);
                AddRandomLine("push {0}", contNamePtr);
                AddRandomLine("push 0");
                AddRandomLine("push 0");
                AddRandomLine("push 0");
                AddRandomLine("call {0}", CreateEventA);
                AddRandomLine("test eax, eax");
                AddRandomLine("jz @ReturnFalse");
                AddRandomLine("mov [{0}], eax", m_InjectionContinueHandlePtr);
                AddRandomLine("push {0}", finNamePtr);
                AddRandomLine("push 0");
                AddRandomLine("push 0");
                AddRandomLine("push 0");
                AddRandomLine("call {0}", CreateEventA);
                AddRandomLine("test eax, eax");
                AddRandomLine("jz @ReturnFalse");
                AddRandomLine("mov [{0}], eax", m_InjectionFinishedHandlePtr);
                AddLine("mov eax, 1");
                AddLine("retn");
                AddLine("@ReturnFalse:");
                AddLine("xor eax, eax");
                AddLine("retn");
                
                uint result = Memory.Asm!.InjectAndExecute(eventStub);
                System.Diagnostics.Debug.WriteLine($"[InjectEventStub] InjectAndExecute returned: {result}");
                return result == 1;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InjectEventStub] Exception: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Utilities

        private string GetRandomString(int minLength, int maxLength)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            int length = minLength < maxLength ? m_Random.Next(minLength, maxLength) : minLength;

            var result = new StringBuilder(length);
            for (int i = 0; i < length; i++)
                result.Append(chars[(int)(m_Random.NextDouble() * (chars.Length - 1))]);

            return result.ToString();
        }

        private string GetRandomString(int length) => GetRandomString(length, length);
        private string GetRandomString() => GetRandomString(64, 64);

        #endregion

        #region Cleanup

        public void Dispose()
        {
            if (m_Disposed)
                return;

            m_Disposed = true;
            IsInitialized = false;

            try
            {
                if (Memory != null && m_OriginalEndSceneBytes != null)
                {
                    // Restore original EndScene bytes
                    Memory.WriteBytes(m_OrigEndScene, m_OriginalEndSceneBytes);
                    Debug.WriteLine($"[ExecutorRand] Restored original EndScene bytes at 0x{m_OrigEndScene:X8}");

                    // Clear injected code
                    Memory.WriteBytes(m_InjectedCode, m_ClearBytes);

                    // Free allocated memory
                    Memory.FreeMemory(m_EndSceneDetour);
                    Memory.FreeMemory(m_InjectedCode);
                    Memory.FreeMemory(m_DataPtr);
                }
            }
            catch
            {
            }

            try { m_InjectionWaitingEvent?.Close(); } catch { }
            try { m_InjectionContinueEvent?.Close(); } catch { }
            try { m_InjectionFinishedEvent?.Close(); } catch { }
        }

        #endregion
    }
}
