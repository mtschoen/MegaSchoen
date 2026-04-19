using static MegaSchoen.Platforms.Windows.Services.Win32Interop;

namespace MegaSchoen.Platforms.Windows.Services;

static class Win32ForegroundHelper
{
    public static bool BringToFront(IntPtr targetHwnd)
    {
        if (targetHwnd == IntPtr.Zero) return false;

        if (IsIconic(targetHwnd))
        {
            ShowWindow(targetHwnd, SW_RESTORE);
        }

        // Three-way AttachThreadInput: let our thread, the target's thread, and
        // the current foreground thread share an input queue, so SetForegroundWindow
        // is allowed to hand focus to the target. Standard workaround for the
        // foreground-lock restriction on modern Windows.
        var currentThread = GetCurrentThreadId();
        var foregroundHwnd = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foregroundHwnd, out _);
        var targetThread = GetWindowThreadProcessId(targetHwnd, out _);

        var attachedCurrent = false;
        var attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedCurrent = AttachThreadInput(currentThread, foregroundThread, true);
            }
            if (foregroundThread != 0 && foregroundThread != targetThread)
            {
                attachedTarget = AttachThreadInput(targetThread, foregroundThread, true);
            }

            BringWindowToTop(targetHwnd);
            var result = SetForegroundWindow(targetHwnd);
            SetFocus(targetHwnd);
            return result;
        }
        finally
        {
            if (attachedTarget) AttachThreadInput(targetThread, foregroundThread, false);
            if (attachedCurrent) AttachThreadInput(currentThread, foregroundThread, false);
        }
    }
}
