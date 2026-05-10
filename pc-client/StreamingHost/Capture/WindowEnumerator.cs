using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace StreamingHost.Capture;

public record WindowInfo(IntPtr Hwnd, string Title, string ProcessName, int ProcessId);

/// <summary>
/// Enumerate top-level visible windows so the user can pick which one to stream.
/// </summary>
public static class WindowEnumerator
{
    public static List<WindowInfo> EnumerateVisibleWindows()
    {
        var list = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;

            // Filter: must have a title and reasonable size
            var titleLen = GetWindowTextLength(hwnd);
            if (titleLen == 0) return true;

            var title = new StringBuilder(titleLen + 1);
            GetWindowText(hwnd, title, title.Capacity);

            // Skip cloaked / off-screen windows (common for UWP/system shells)
            int cloaked = 0;
            DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, ref cloaked, sizeof(int));
            if (cloaked != 0) return true;

            GetWindowThreadProcessId(hwnd, out var pid);
            string procName;
            try { procName = Process.GetProcessById((int)pid).ProcessName; }
            catch { procName = "?"; }

            list.Add(new WindowInfo(hwnd, title.ToString(), procName, (int)pid));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    // P/Invoke
    private const int DWMWA_CLOAKED = 14;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, ref int attrValue, int attrSize);
}
