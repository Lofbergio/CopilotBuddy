using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GreenMagic.Native;

namespace GreenMagic
{
    /// <summary>
    /// Windows enumeration and manipulation utilities - EXACT port of BlueMagic.Windows
    /// All methods from HB 3.3.5 BlueMagic are present here.
    /// </summary>
    public static class Windows
    {
        private static readonly object WindowsLock = new object();
        private static List<IntPtr> _lWindows = new List<IntPtr>();

        private static bool EnumWindowsCallback(IntPtr hWnd, IntPtr lParam)
        {
            _lWindows.Add(hWnd);
            return true;
        }

        private static bool _EnumWindows()
        {
            _lWindows = new List<IntPtr>();
            Imports.EnumWindowsProc callback = new Imports.EnumWindowsProc(EnumWindowsCallback);
            return Imports.EnumWindows(callback, IntPtr.Zero);
        }

        public static IntPtr[] EnumWindows()
        {
            lock (WindowsLock)
            {
                return _EnumWindows() ? _lWindows.ToArray() : null;
            }
        }

        public static IntPtr[] EnumMainWindows()
        {
            Process[] processes = Process.GetProcesses();
            return processes.Select(proc => proc.MainWindowHandle).ToArray();
        }

        public static IntPtr[] FindWindows(string classname, string windowTitle)
        {
            if (classname == null) classname = string.Empty;
            if (windowTitle == null) windowTitle = string.Empty;

            List<IntPtr> list = new List<IntPtr>();
            lock (WindowsLock)
            {
                if (!_EnumWindows())
                    return null;

                list.AddRange(_lWindows.Where(hWnd =>
                {
                    if (windowTitle.Length > 0 && Imports.GetWindowTitle(hWnd) == windowTitle)
                        return true;
                    if (classname.Length > 0 && Imports.GetClassName(hWnd) == classname)
                        return true;
                    return false;
                }));
            }

            return list.ToArray();
        }

        public static IntPtr FindWindow(string classname, string windowTitle)
        {
            if (classname == null) classname = string.Empty;
            if (windowTitle == null) windowTitle = string.Empty;

            lock (WindowsLock)
            {
                if (!_EnumWindows())
                    return IntPtr.Zero;

                var found = _lWindows.Where(hWnd =>
                {
                    if (windowTitle.Length > 0 && Imports.GetWindowTitle(hWnd) == windowTitle)
                        return true;
                    if (classname.Length > 0 && Imports.GetClassName(hWnd) == classname)
                        return true;
                    return false;
                }).FirstOrDefault();

                return found;
            }
        }

        public static IntPtr[] FindMainWindows(string classname, string windowTitle)
        {
            if (classname == null) classname = string.Empty;
            if (windowTitle == null) windowTitle = string.Empty;

            Process[] processes = Process.GetProcesses();
            return processes
                .Where(proc => proc.MainWindowHandle != IntPtr.Zero)
                .Where(proc =>
                {
                    if (windowTitle.Length > 0 && proc.MainWindowTitle == windowTitle)
                        return true;
                    if (classname.Length > 0 && Imports.GetClassName(proc.MainWindowHandle) == classname)
                        return true;
                    return false;
                })
                .Select(proc => proc.MainWindowHandle)
                .ToArray();
        }

        public static IntPtr FindMainWindow(string classname, string windowTitle)
        {
            if (classname == null) classname = string.Empty;
            if (windowTitle == null) windowTitle = string.Empty;

            Process[] processes = Process.GetProcesses();
            var proc = processes
                .Where(p => p.MainWindowHandle != IntPtr.Zero)
                .Where(p =>
                {
                    if (windowTitle.Length > 0 && p.MainWindowTitle == windowTitle)
                        return true;
                    if (classname.Length > 0 && Imports.GetClassName(p.MainWindowHandle) == classname)
                        return true;
                    return false;
                })
                .FirstOrDefault();

            return proc != null ? proc.MainWindowHandle : IntPtr.Zero;
        }

        public static IntPtr[] FindWindowsContains(string classname, string windowTitle)
        {
            if (classname == null) classname = string.Empty;
            if (windowTitle == null) windowTitle = string.Empty;

            List<IntPtr> list = new List<IntPtr>();
            lock (WindowsLock)
            {
                if (!_EnumWindows())
                    return null;

                list.AddRange(_lWindows.Where(hWnd =>
                {
                    if (windowTitle.Length > 0 && Imports.GetWindowTitle(hWnd).Contains(windowTitle))
                        return true;
                    if (classname.Length > 0 && Imports.GetClassName(hWnd).Contains(classname))
                        return true;
                    return false;
                }));
            }

            return list.ToArray();
        }

        public static IntPtr FindWindowContains(string classname, string windowTitle)
        {
            if (classname == null) classname = string.Empty;
            if (windowTitle == null) windowTitle = string.Empty;

            lock (WindowsLock)
            {
                if (!_EnumWindows())
                    return IntPtr.Zero;

                var found = _lWindows.Where(hWnd =>
                {
                    if (windowTitle.Length > 0 && Imports.GetWindowTitle(hWnd).Contains(windowTitle))
                        return true;
                    if (classname.Length > 0 && Imports.GetClassName(hWnd).Contains(classname))
                        return true;
                    return false;
                }).FirstOrDefault();

                return found;
            }
        }

        public static IntPtr[] FindMainWindowsContains(string classname, string windowTitle)
        {
            if (classname == null) classname = string.Empty;
            if (windowTitle == null) windowTitle = string.Empty;

            Process[] processes = Process.GetProcesses();
            return processes
                .Where(proc => proc.MainWindowHandle != IntPtr.Zero)
                .Where(proc =>
                {
                    if (windowTitle.Length > 0 && proc.MainWindowTitle.Contains(windowTitle))
                        return true;
                    if (classname.Length > 0 && Imports.GetClassName(proc.MainWindowHandle).Contains(classname))
                        return true;
                    return false;
                })
                .Select(proc => proc.MainWindowHandle)
                .ToArray();
        }

        public static IntPtr FindMainWindowContains(string classname, string windowTitle)
        {
            if (classname == null) classname = string.Empty;
            if (windowTitle == null) windowTitle = string.Empty;

            Process[] processes = Process.GetProcesses();
            var proc = processes
                .Where(p => p.MainWindowHandle != IntPtr.Zero)
                .Where(p =>
                {
                    if (windowTitle.Length > 0 && p.MainWindowTitle.Contains(windowTitle))
                        return true;
                    if (classname.Length > 0 && Imports.GetClassName(p.MainWindowHandle).Contains(classname))
                        return true;
                    return false;
                })
                .FirstOrDefault();

            return proc != null ? proc.MainWindowHandle : IntPtr.Zero;
        }

        public static IntPtr FindWindowByProcessName(string processName)
        {
            if (processName.EndsWith(".exe"))
                processName = processName.Remove(processName.Length - 4, 4);

            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length != 0)
                return processes[0].MainWindowHandle;
            else
                return IntPtr.Zero;
        }

        public static IntPtr[] FindWindowsByProcessName(string processName)
        {
            if (processName.EndsWith(".exe"))
                processName = processName.Remove(processName.Length - 4, 4);

            Process[] processes = Process.GetProcessesByName(processName);
            if (processes.Length != 0)
                return processes.Select(proc => proc.MainWindowHandle).ToArray();
            else
                return null;
        }

        public static IntPtr FindWindowByProcessId(int dwProcessId)
        {
            Process proc = Process.GetProcessById(dwProcessId);
            return proc.MainWindowHandle;
        }

        public static bool ShowWindow(IntPtr hWnd, WindowShowStyle style)
        {
            return Imports.ShowWindow(hWnd, style);
        }
    }
}
