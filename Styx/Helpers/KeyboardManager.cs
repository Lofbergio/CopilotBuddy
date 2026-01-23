#nullable disable
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Styx.WoWInternals;

namespace Styx.Helpers
{
    /// <summary>
    /// Provides keyboard input functionality for the WoW client.
    /// </summary>
    public static class KeyboardManager
    {
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;

        [DllImport("user32.dll", CharSet = CharSet.Auto, EntryPoint = "SendMessage")]
        private static extern IntPtr SendMessageInternal(IntPtr hWnd, uint msg, uint wParam, uint lParam);

        [DllImport("user32.dll", EntryPoint = "PostMessage", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessageInternal(HandleRef hWnd, uint msg, uint wParam, uint lParam);

        /// <summary>
        /// Sends a message to the WoW window.
        /// </summary>
        public static void SendMessage(uint msg, uint wParam, uint lParam)
        {
            SendMessageInternal(ObjectManager.Wow.WindowHandle, msg, wParam, lParam);
        }

        /// <summary>
        /// Posts a message to the WoW window.
        /// </summary>
        public static void PostMessage(uint msg, uint wParam, uint lParam)
        {
            var handleRef = new HandleRef(null, ObjectManager.Wow.WindowHandle);
            PostMessageInternal(handleRef, msg, wParam, lParam);
        }

        /// <summary>
        /// Simulates pressing the down arrow key.
        /// </summary>
        public static void DownArrow()
        {
            SendMessage(WM_KEYDOWN, 40U, 22020097U);
            SendMessage(WM_KEYUP, 40U, 22020097U);
        }

        /// <summary>
        /// Simulates pressing a key down.
        /// </summary>
        public static void PressKey(char key)
        {
            PostMessage(WM_KEYDOWN, (uint)key, 0U);
        }

        /// <summary>
        /// Simulates releasing a key.
        /// </summary>
        public static void ReleaseKey(char key)
        {
            PostMessage(WM_KEYUP, (uint)key, 0U);
        }

        /// <summary>
        /// Simulates a key press and release.
        /// </summary>
        public static void KeyUpDown(char key)
        {
            PressKey(key);
            Thread.Sleep(10);
            ReleaseKey(key);
        }

        /// <summary>
        /// Performs a random key press to prevent AFK detection.
        /// </summary>
        public static void AntiAfk()
        {
            var random = new Random();
            int action = random.Next(2, 6);

            switch (action)
            {
                case 2:
                    KeyUpDown('W');
                    break;
                case 3:
                    KeyUpDown('Q');
                    break;
                case 4:
                    KeyUpDown('S');
                    break;
                case 5:
                    KeyUpDown('D');
                    break;
                default:
                    KeyUpDown('A');
                    break;
            }
        }

        /// <summary>
        /// Action bar enumeration.
        /// </summary>
        public enum ActionBar
        {
            Bar1 = 1,
            Bar2,
            Bar3,
            Bar4,
            Bar5,
            Bar6
        }

        /// <summary>
        /// Action button enumeration.
        /// </summary>
        public enum ActionButton
        {
            Button1 = 1,
            Button2,
            Button3,
            Button4,
            Button5,
            Button6,
            Button7,
            Button8,
            Button9,
            Button10,
            Button11,
            Button12
        }
    }
}
