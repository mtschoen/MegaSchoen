using Claude.Core.Models;

namespace Claude.Core;

public sealed class ActiveSessionEnumerator
{
    readonly IClaudeProcessLocator _locator;
    readonly StateStore _store;
    readonly string _projectsRoot;

    public ActiveSessionEnumerator(IClaudeProcessLocator locator, StateStore store, string projectsRoot)
    {
        _locator = locator;
        _store = store;
        _projectsRoot = projectsRoot;
    }

    public ActiveSessionEnumerator(IClaudeProcessLocator locator, StateStore store)
        : this(locator, store, DefaultProjectsRoot()) { }

    static string DefaultProjectsRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    public IReadOnlyList<SessionSnapshot> Enumerate()
    {
        var windows = _locator.EnumerateWindows();
        if (windows.Count == 0) return Array.Empty<SessionSnapshot>();

        // Implementation grows over the next tasks.
        return Array.Empty<SessionSnapshot>();
    }
}
