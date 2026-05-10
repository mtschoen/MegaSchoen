using Claude.Core;
using Claude.Core.Models;

namespace Claude.Core.Tests.Fakes;

internal sealed class FakeProcessLocator : IClaudeProcessLocator
{
    public List<ClaudeWindow> Windows { get; } = new();
    public IReadOnlyList<ClaudeWindow> EnumerateWindows() => Windows;
}
