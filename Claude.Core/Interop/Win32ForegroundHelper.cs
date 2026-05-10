using Claude.Core.Models;
using static Claude.Core.Interop.User32;

namespace Claude.Core.Interop;

static class Win32ForegroundHelper
{
    public static bool BringToFront(WindowToken token)
    {
        var targetHwnd = token.Handle;
        if (targetHwnd == IntPtr.Zero) return false;

        if (IsIconic(targetHwnd))
        {
            ShowWindow(targetHwnd, SW_RESTORE);
        }

        // Three-way AttachThreadInput so SetForegroundWindow is allowed to
        // hand focus to the target. Standard workaround for the
        // foreground-lock restriction on modern Windows.
        var currentThread = Kernel32.GetCurrentThreadId();
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
