using Claude.Core.Interop;
using Claude.Core.Models;

namespace Claude.Core.Windows;

public sealed class WindowsClaudeWindowFocuser : IClaudeWindowFocuser
{
    public bool BringToFront(WindowToken window) => Win32ForegroundHelper.BringToFront(window);
}
