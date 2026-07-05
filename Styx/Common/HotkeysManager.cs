using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Interop;
using Styx.Helpers;

namespace Styx.Common
{
    public static class HotkeysManager
    {
        private static readonly object _lock = new object();
        private static readonly List<Hotkey> _hotkeys = new List<Hotkey>();
        private static readonly Queue<Hotkey> _registerQueue = new Queue<Hotkey>();
        private static readonly Queue<Hotkey> _unregisterQueue = new Queue<Hotkey>();
        private static bool _initialized;
        private static IntPtr _windowHandle;
        private static int _idCounter;
        private static Thread _processingThread;

        private const int WM_HOTKEY = 786;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint key);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PeekMessage(out MSG msg, IntPtr windowHandle, uint filterMin, uint filterMax, uint remove);

        public static IEnumerable<Hotkey> Hotkeys
        {
            get
            {
                lock (_lock)
                {
                    return _hotkeys.ToList();
                }
            }
        }

        public static void Initialize(IntPtr windowHandle)
        {
            if (_initialized)
                return;
            _windowHandle = windowHandle;
            _initialized = true;
        }

        public static Hotkey Register(string name, Keys key, ModifierKeys modifierKeys, Action<Hotkey> callback)
        {
            Hotkey hotkey = new Hotkey(name, key, modifierKeys, Interlocked.Increment(ref _idCounter), callback);
            lock (_lock)
            {
                _registerQueue.Enqueue(hotkey);
                _hotkeys.Add(hotkey);
            }
            EnsureProcessingThread();
            return hotkey;
        }

        public static void Unregister(string name)
        {
            lock (_lock)
            {
                Unregister(_hotkeys.FirstOrDefault(h => h.Name == name));
            }
        }

        public static void Unregister(Hotkey hotkey)
        {
            if (hotkey == null)
                return;
            lock (_lock)
            {
                _unregisterQueue.Enqueue(hotkey);
                _hotkeys.Remove(hotkey);
            }
        }

        private static void RegisterHotkeyInternal(Hotkey hotkey)
        {
            if (hotkey.IsRegistered)
                return;
            if (RegisterHotKey(IntPtr.Zero, hotkey.Id, (uint)hotkey.ModifierKeys, (uint)hotkey.Key))
                hotkey.IsRegistered = true;
        }

        private static void UnregisterHotkeyInternal(Hotkey hotkey)
        {
            if (!hotkey.IsRegistered)
                return;
            if (UnregisterHotKey(IntPtr.Zero, hotkey.Id))
                hotkey.IsRegistered = false;
        }

        private static void EnsureProcessingThread()
        {
            if (_processingThread != null)
                return;
            _processingThread = new Thread(ProcessingLoop)
            {
                Name = "Hotkey Processing Loop",
                IsBackground = true
            };
            _processingThread.Start();
        }

        private static void ProcessingLoop()
        {
            var localList = new List<Hotkey>();
            for (;;)
            {
                // Raw background thread: on .NET Core an unhandled exception here (e.g. a hotkey
                // CALLBACK throwing) is process-fatal — catch, log, keep the loop alive.
                try
                {
                    lock (_lock)
                    {
                        while (_registerQueue.Count > 0)
                            RegisterHotkeyInternal(_registerQueue.Dequeue());
                        while (_unregisterQueue.Count > 0)
                            UnregisterHotkeyInternal(_unregisterQueue.Dequeue());
                        localList.Clear();
                        localList.AddRange(_hotkeys);
                    }

                    if (GetForegroundWindow() != _windowHandle)
                    {
                        foreach (Hotkey hotkey in localList)
                            UnregisterHotkeyInternal(hotkey);
                    }
                    else
                    {
                        foreach (Hotkey hotkey in localList)
                            RegisterHotkeyInternal(hotkey);
                        ProcessMessages(localList);
                    }
                }
                catch (Exception ex)
                {
                    Logging.WriteException(ex);
                }

                Thread.Sleep(100);
            }
        }

        private static void ProcessMessages(List<Hotkey> hotkeys)
        {
            while (PeekMessage(out MSG msg, IntPtr.Zero, WM_HOTKEY, WM_HOTKEY, 1))
            {
                Hotkey hotkey = hotkeys.FirstOrDefault(h => h.Id == msg.wParam.ToInt32());
                if (hotkey != null)
                {
                    Logging.WriteDiagnostic(hotkey.Name + " pressed.");
                    hotkey.Callback(hotkey);
                }
            }
        }
    }
}
