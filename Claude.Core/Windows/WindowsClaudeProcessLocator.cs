using Claude.Core.Models;

namespace Claude.Core.Windows;

public sealed class WindowsClaudeProcessLocator : IClaudeProcessLocator
{
    public IReadOnlyList<ClaudeWindow> EnumerateWindows()
    {
        var raw = ProcessResolver.EnumerateCmdExeWindows();
        var result = new List<ClaudeWindow>(raw.Count);
        foreach (var window in raw)
        {
            result.Add(new ClaudeWindow(
                ProcessId: window.ProcessId,
                Window: WindowToken.FromHandle(window.WindowHandle),
                Title: window.WindowTitle,
                WorkingDirectory: window.WorkingDirectory));
        }
        return result;
    }
}
