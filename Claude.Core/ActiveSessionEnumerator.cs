using Claude.Core.Models;

namespace Claude.Core;

public sealed class ActiveSessionEnumerator
{
    readonly IReadOnlyList<ISessionSource> _sources;

    public ActiveSessionEnumerator(IEnumerable<ISessionSource> sources)
    {
        _sources = sources.ToList();
    }

    public ActiveSessionEnumerator(
        IClaudeProcessLocator locator,
        StateStore store,
        string projectsRoot,
        Func<string, DateTime>? creationTimeUtcSource = null)
        : this([new ClaudeSessionSource(locator, store, projectsRoot, creationTimeUtcSource)]) { }

    public ActiveSessionEnumerator(IClaudeProcessLocator locator, StateStore store)
        : this([new ClaudeSessionSource(locator, store)]) { }

    public IReadOnlyList<SessionSnapshot> Enumerate() =>
        _sources
            .SelectMany(source => source.Enumerate())
            .OrderBy(snapshot => (int)snapshot.RollupState)
            .ThenByDescending(snapshot => snapshot.LastActivityUtc)
            .ToList();
}
