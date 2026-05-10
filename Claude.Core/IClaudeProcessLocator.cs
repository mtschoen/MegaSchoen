using Claude.Core.Models;

namespace Claude.Core;

public interface IClaudeProcessLocator
{
    IReadOnlyList<ClaudeWindow> EnumerateWindows();
}
