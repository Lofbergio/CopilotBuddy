using System;
using System.Runtime.InteropServices;

namespace Styx.Helpers;

/// <summary>
/// Helper methods for sending keyboard input to windows.
/// </summary>
public static class KeyHelpers
{
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_CHAR = 0x0102;
    private const uint WM_KEYUP = 0x0101;

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, uint wParam, uint lParam);

    /// <summary>
    /// Sends text to a window using key messages.
    /// </summary>
    /// <param name="text">The text to send.</param>
    /// <param name="hWnd">The window handle.</param>
    public static void ControlSend(string text, IntPtr hWnd)
    {
        foreach (char c in text)
        {
            SendMessage(hWnd, WM_KEYDOWN, c, 0);
            SendMessage(hWnd, WM_CHAR, c, 0);
            SendMessage(hWnd, WM_KEYUP, c, 0);
        }
    }
}
