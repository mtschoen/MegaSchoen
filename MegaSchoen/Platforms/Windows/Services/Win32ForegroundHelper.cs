using static MegaSchoen.Platforms.Windows.Services.Win32Interop;

namespace MegaSchoen.Platforms.Windows.Services;

static class Win32ForegroundHelper
{
    public static void BringToFront(IntPtr targetHwnd)
    {
        if (targetHwnd == IntPtr.Zero) return;

        ShowWindow(targetHwnd, SW_RESTORE);

        var currentThread = GetCurrentThreadId();
        var foregroundHwnd = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foregroundHwnd, out _);
        var targetThread = GetWindowThreadProcessId(targetHwnd, out var targetPid);

        if (currentThread != foregroundThread)
        {
            AttachThreadInput(currentThread, foregroundThread, true);
        }
        AllowSetForegroundWindow(targetPid);
        SetForegroundWindow(targetHwnd);
        if (currentThread != foregroundThread)
        {
            AttachThreadInput(currentThread, foregroundThread, false);
        }
    }
}
