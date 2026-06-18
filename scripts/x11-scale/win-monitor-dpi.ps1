# Prints the effective DPI (e.g. 192 for 200%) of the monitor under the cursor.
# Per-monitor-aware so GetDpiForMonitor returns the real monitor DPI, not 96.
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class MonDpi {
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("Shcore.dll")] static extern int GetDpiForMonitor(IntPtr hmon, int dpiType, out uint x, out uint y);
    public static uint Effective() {
        // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4
        SetProcessDpiAwarenessContext((IntPtr)(-4));
        POINT p; GetCursorPos(out p);
        IntPtr h = MonitorFromPoint(p, 2); // MONITOR_DEFAULTTONEAREST
        uint x, y; GetDpiForMonitor(h, 0, out x, out y); // MDT_EFFECTIVE_DPI
        return x;
    }
}
'@
[MonDpi]::Effective()
