using Claude.Core.Models;

namespace Claude.Core;

public interface IClaudeWindowFocuser
{
    bool BringToFront(WindowToken window);
}
