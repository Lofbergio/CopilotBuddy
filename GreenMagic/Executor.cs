using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using GreenMagic.Native;

namespace GreenMagic
{
    /// <summary>
    /// Executor - EXACT copy of BlueMagic.Executor (.hb 3.3.5a).
    /// Uses thread suspension and context manipulation for initial injection.
    /// Different from ExecutorRand which uses continuous polling.
    /// </summary>
    public sealed class Executor : IDisposable
    {
        private Memory m_Memory;
        private bool m_ContinuousExecution;
        private bool m_FirstExecution;
        private byte[] m_ClearBytes = new byte[4096];

        private uint m_OrigEndScene;
        private uint m_EndSceneDetour;
        private uint m_InjectedCode;
        private uint m_DataPtr;
        private uint m_ReturnedDataPtr;

        private uint m_InjectionWaitingHandlePtr;
        private string m_InjectionWaitingEventName;
        private EventWaitHandle m_InjectionWaitingEvent;

        private uint m_InjectionContinueHandlePtr;
        private string m_InjectionContinueEventName;
        private EventWaitHandle m_InjectionContinueEvent;

        private uint m_InjectionFinishedHandlePtr;
        private string m_InjectionFinishedEventName;
        private EventWaitHandle m_InjectionFinishedEvent;

        private Random m_Random;

        private UIntPtr WaitForSingleObject;
        private UIntPtr CreateEventA;
        private UIntPtr ResetEvent;
        private UIntPtr SetEvent;

        public bool IsOpen => m_Memory != null && m_Memory.IsProcessOpen && m_Memory.IsThreadOpen;
        public bool IsInitialized { get; private set; }
        public uint DataPointer => m_DataPtr;
        public uint ReturnPointer => m_ReturnedDataPtr;
        public uint InjectCodePointer => m_InjectedCode;

        public Executor(Memory memory, uint endSceneProc)
        {
            if (memory == null || !memory.IsProcessOpen)
                throw new ArgumentNullException("Memory object passed to Executor constructor was invalid.");

            if (endSceneProc == 0)
                throw new ArgumentNullException("EndSceneProc passed to Executor constructor was invalid.");

            if (memory.Read<uint>(new uint[] { endSceneProc }) == 0)
                throw new ArgumentNullException("EndSceneProc passed to Executor constructor was invalid.");

            m_Memory = memory;
            m_OrigEndScene = endSceneProc;

            byte[] seed = new byte[4];
            using (var rng = new RNGCryptoServiceProvider())
            {
                rng.GetNonZeroBytes(seed);
            }
            m_Random = new Random(BitConverter.ToInt32(seed, 0));

            m_InjectionWaitingEventName = "Global\\" + GetRandomString(16);
            m_InjectionWaitingEvent = new EventWaitHandle(false, EventResetMode.ManualReset, m_InjectionWaitingEventName);

            m_InjectionContinueEventName = "Global\\" + GetRandomString(16);
            m_InjectionContinueEvent = new EventWaitHandle(false, EventResetMode.AutoReset, m_InjectionContinueEventName);

            m_InjectionFinishedEventName = "Global\\" + GetRandomString(16);
            m_InjectionFinishedEvent = new EventWaitHandle(false, EventResetMode.AutoReset, m_InjectionFinishedEventName);

            InitializeDetour();
            IsInitialized = true;
        }

        public Executor(int processId, uint endSceneProc) : this(new Memory(processId), endSceneProc)
        {
        }

        public void BeginExecute()
        {
            if (m_ContinuousExecution)
                EndExecute();

            m_ContinuousExecution = true;
            m_FirstExecution = true;
        }

        public void EndExecute()
        {
            m_ContinuousExecution = false;
            m_InjectionWaitingEvent.Reset();
            m_InjectionContinueEvent.Set();
            m_Memory.WriteBytes(m_InjectedCode, m_ClearBytes);
        }

        public void Execute()
        {
            if (!IsOpen || !IsInitialized)
                throw new Exception("Cannot execute code while process is not opened and/or Executor is not initialized.");

            m_Memory.Asm.Inject(m_InjectedCode);
            m_InjectionWaitingEvent.Set();

            if (m_ContinuousExecution)
            {
                if (!m_FirstExecution)
                {
                    m_InjectionContinueEvent.Set();
                }
                else
                {
                    m_FirstExecution = false;
                }
            }

            if (!m_InjectionFinishedEvent.WaitOne(10000, false))
                throw new Exception("Process must have frozen or gotten out of sync; InjectionFinishedEvent was never fired.");

            if (!m_ContinuousExecution)
            {
                m_InjectionWaitingEvent.Reset();
                m_InjectionContinueEvent.Set();
                m_Memory.WriteBytes(m_InjectedCode, m_ClearBytes);
            }
        }

        public void Clear() => m_Memory.Asm.Clear();
        public void AddLine(string line) => m_Memory.Asm.AddLine(line);
        public void AddLine(string format, params object[] args) => m_Memory.Asm.AddLine(format, args);

        private string GetRandomString(int minLength, int maxLength)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            StringBuilder sb;
            if (minLength < maxLength)
                sb = new StringBuilder(m_Random.Next(minLength, maxLength));
            else
                sb = new StringBuilder(minLength);

            for (int i = 0; i < sb.Capacity; i++)
                sb.Append(chars[(int)(m_Random.NextDouble() * (chars.Length - 1))]);

            return sb.ToString();
        }

        private string GetRandomString(int length) => GetRandomString(length, length);
        private string GetRandomString() => GetRandomString(64, 64);

        private void InitializeDetour()
        {
            if (!IsOpen)
                throw new Exception("Process is not open for memory manipulation.");

            int num = m_ClearBytes.Length;
            ProcessModule module = m_Memory.GetModule("d3d9.dll");
            if (module == null)
                throw new Exception("Executor can only be used on processes that use DirectX9.");

            IntPtr k32 = Imports.GetModuleHandleW("kernel32.dll");
            if (k32 == IntPtr.Zero)
                throw new Exception("Could not get handle to kernel32.dll");

            WaitForSingleObject = Imports.GetProcAddress(k32, "WaitForSingleObject");
            if (WaitForSingleObject == UIntPtr.Zero)
                throw new Exception("Could not get proc address of WaitForSingleObject.");

            CreateEventA = Imports.GetProcAddress(k32, "CreateEventA");
            if (CreateEventA == UIntPtr.Zero)
                throw new Exception("Could not get proc address of CreateEventA.");

            ResetEvent = Imports.GetProcAddress(k32, "ResetEvent");
            if (ResetEvent == UIntPtr.Zero)
                throw new Exception("Could not get proc address of ResetEvent.");

            SetEvent = Imports.GetProcAddress(k32, "SetEvent");
            if (SetEvent == UIntPtr.Zero)
                throw new Exception("Could not get proc address of SetEvent.");

            uint eventStub = m_Memory.AllocateMemory(num, 4096U, 32U);
            uint namesMem = m_Memory.AllocateMemory(num, 4096U, 4U);

            uint name2 = namesMem + (uint)m_InjectionWaitingEventName.Length + 4;
            uint name3 = name2 + (uint)m_InjectionContinueEventName.Length + 4;

            m_Memory.Write(namesMem, m_InjectionWaitingEventName);
            m_Memory.Write(name2, m_InjectionContinueEventName);
            m_Memory.Write(name3, m_InjectionFinishedEventName);

            m_EndSceneDetour = m_Memory.AllocateMemory(num, 4096U, 32U);
            m_InjectedCode = m_Memory.AllocateMemory(num, 4096U, 576U);
            m_DataPtr = m_Memory.AllocateMemory(num, 4096U, 4U);

            m_InjectionWaitingHandlePtr = m_DataPtr;
            m_InjectionContinueHandlePtr = m_DataPtr + 4;
            m_InjectionFinishedHandlePtr = m_DataPtr + 8;
            m_ReturnedDataPtr = m_DataPtr + 12;

            // Suspend thread and get context
            m_Memory.SuspendThread();
            Context ctx = m_Memory.GetThreadContext(65537U);

            // Wait until EIP is outside d3d9.dll range
            while (ctx.Eip >= (uint)module.BaseAddress && ctx.Eip <= (uint)module.BaseAddress + (uint)module.ModuleMemorySize)
            {
                m_Memory.ResumeThread();
                Thread.Sleep(1);
                m_Memory.SuspendThread();
                ctx = m_Memory.GetThreadContext(65537U);
            }

            InjectDetour();
            InjectEventStub(eventStub, ctx.Eip, namesMem, name2, name3);

            ctx.Eip = eventStub;
            m_Memory.SetThreadContext(m_Memory.ThreadHandle, ctx);
            m_InjectionWaitingEvent.Set();
            m_Memory.ResumeThread();

            // Wait for stub to finish
            while (m_InjectionWaitingEvent.WaitOne(0))
                Thread.Sleep(1);

            m_InjectionFinishedEvent.Set();
            Thread.Sleep(10);

            m_Memory.FreeMemory(eventStub);
            m_Memory.FreeMemory(namesMem);
        }

        private void InjectDetour()
        {
            m_Memory.Asm.Clear();
            m_Memory.Asm.AddLine("pushad");
            m_Memory.Asm.AddLine("@CheckInjection:");
            m_Memory.Asm.AddLine("mov eax, [{0}]", m_InjectionWaitingHandlePtr);
            m_Memory.Asm.AddLine("push 0");
            m_Memory.Asm.AddLine("push eax");
            m_Memory.Asm.AddLine("call {0}", WaitForSingleObject);
            m_Memory.Asm.AddLine("test eax, eax");
            m_Memory.Asm.AddLine("jnz @NoInjection");
            m_Memory.Asm.AddLine("@Injection:");
            m_Memory.Asm.AddLine("call {0}", m_InjectedCode);
            m_Memory.Asm.AddLine("mov [{0}], eax", m_ReturnedDataPtr);
            m_Memory.Asm.AddLine("mov eax, [{0}]", m_InjectionFinishedHandlePtr);
            m_Memory.Asm.AddLine("push eax");
            m_Memory.Asm.AddLine("call {0}", SetEvent);
            m_Memory.Asm.AddLine("mov eax, [{0}]", m_InjectionContinueHandlePtr);
            m_Memory.Asm.AddLine("push 1000");
            m_Memory.Asm.AddLine("push eax");
            m_Memory.Asm.AddLine("call {0}", WaitForSingleObject);
            m_Memory.Asm.AddLine("test eax, eax");
            m_Memory.Asm.AddLine("jz @CheckInjection");
            m_Memory.Asm.AddLine("mov eax, [{0}]", m_InjectionWaitingHandlePtr);
            m_Memory.Asm.AddLine("push eax");
            m_Memory.Asm.AddLine("call {0}", ResetEvent);
            m_Memory.Asm.AddLine("@NoInjection:");
            m_Memory.Asm.AddLine("popad");
            m_Memory.Asm.AddLine("mov edi, edi");
            m_Memory.Asm.AddLine("push ebp");
            m_Memory.Asm.AddLine("mov ebp, esp");
            m_Memory.Asm.AddLine("jmp {0}", m_OrigEndScene + 5);
            m_Memory.Asm.Inject(m_EndSceneDetour);

            m_Memory.Asm.Clear();
            m_Memory.Asm.AddLine("jmp {0}", m_EndSceneDetour);
            m_Memory.Asm.Inject(m_OrigEndScene);
        }

        private void InjectEventStub(uint eventStub, uint returnInstruction, uint waitNamePtr, uint contNamePtr, uint finNamePtr)
        {
            m_Memory.Asm.Clear();
            m_Memory.Asm.AddLine("push {0}", returnInstruction);
            m_Memory.Asm.AddLine("pushad");
            m_Memory.Asm.AddLine("pushfd");

            m_Memory.Asm.AddLine("push {0}", waitNamePtr);
            m_Memory.Asm.AddLine("push 0");
            m_Memory.Asm.AddLine("push 0");
            m_Memory.Asm.AddLine("push 0");
            m_Memory.Asm.AddLine("call {0}", CreateEventA);
            m_Memory.Asm.AddLine("test eax, eax");
            m_Memory.Asm.AddLine("jz @Return");
            m_Memory.Asm.AddLine("mov [{0}], eax", m_InjectionWaitingHandlePtr);

            m_Memory.Asm.AddLine("push {0}", contNamePtr);
            m_Memory.Asm.AddLine("push 0");
            m_Memory.Asm.AddLine("push 0");
            m_Memory.Asm.AddLine("push 0");
            m_Memory.Asm.AddLine("call {0}", CreateEventA);
            m_Memory.Asm.AddLine("test eax, eax");
            m_Memory.Asm.AddLine("jz @Return");
            m_Memory.Asm.AddLine("mov [{0}], eax", m_InjectionContinueHandlePtr);

            m_Memory.Asm.AddLine("push {0}", finNamePtr);
            m_Memory.Asm.AddLine("push 0");
            m_Memory.Asm.AddLine("push 0");
            m_Memory.Asm.AddLine("push 0");
            m_Memory.Asm.AddLine("call {0}", CreateEventA);
            m_Memory.Asm.AddLine("test eax, eax");
            m_Memory.Asm.AddLine("jz @Return");
            m_Memory.Asm.AddLine("mov [{0}], eax", m_InjectionFinishedHandlePtr);

            m_Memory.Asm.AddLine("mov eax, [{0}]", m_InjectionWaitingHandlePtr);
            m_Memory.Asm.AddLine("push eax");
            m_Memory.Asm.AddLine("call {0}", ResetEvent);

            m_Memory.Asm.AddLine("mov eax, [{0}]", m_InjectionFinishedHandlePtr);
            m_Memory.Asm.AddLine("push -1");
            m_Memory.Asm.AddLine("push eax");
            m_Memory.Asm.AddLine("call {0}", WaitForSingleObject);

            m_Memory.Asm.AddLine("@Return:");
            m_Memory.Asm.AddLine("popfd");
            m_Memory.Asm.AddLine("popad");
            m_Memory.Asm.AddLine("retn");

            m_Memory.Asm.Inject(eventStub);
        }

        public void Dispose()
        {
            IsInitialized = false;

            if (m_Memory != null)
            {
                m_Memory.Asm.Clear();
                m_Memory.Asm.AddLine("mov edi, edi");
                m_Memory.Asm.AddLine("push ebp");
                m_Memory.Asm.AddLine("mov ebp, esp");
                m_Memory.Asm.Inject(m_OrigEndScene);

                m_Memory.FreeMemory(m_EndSceneDetour);
                m_Memory.FreeMemory(m_InjectedCode);
                m_Memory.FreeMemory(m_DataPtr);
                m_Memory.CloseProcessHandle();
            }

            m_InjectionWaitingEvent?.Close();
            m_InjectionContinueEvent?.Close();
            m_InjectionFinishedEvent?.Close();
        }
    }
}
