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

        var stateFile = _store.Read();
        var stateBySessionId = stateFile.Sessions;

        var snapshots = new List<SessionSnapshot>(capacity: windows.Count);

        foreach (var window in windows)
        {
            if (string.IsNullOrEmpty(window.WorkingDirectory)) continue;

            var slug = SlugEncoder.Encode(window.WorkingDirectory);
            var slugDir = Path.Combine(_projectsRoot, slug);
            if (!Directory.Exists(slugDir)) continue;

            var transcripts = Directory.GetFiles(slugDir, "*.jsonl", SearchOption.TopDirectoryOnly);
            if (transcripts.Length == 0) continue;

            var freshest = transcripts
                .Select(p => new { Path = p, Mtime = File.GetLastWriteTimeUtc(p) })
                .OrderByDescending(p => p.Mtime)
                .First();

            if (!VerifyCwdMatch(freshest.Path, window.WorkingDirectory))
            {
                Logger.Log($"ActiveSessionEnumerator: cwd mismatch on {freshest.Path}; skipping (slug collision)");
                continue;
            }

            var sessionId = Path.GetFileNameWithoutExtension(freshest.Path);

            SessionEntry? stateEntry = stateBySessionId.TryGetValue(sessionId, out var entry) ? entry : null;
            var state = SessionStateClassifier.Classify(stateEntry, freshest.Path);
            var subagents = EnumerateSubagents(slugDir, sessionId);

            snapshots.Add(new SessionSnapshot(
                SessionId: sessionId,
                Cwd: window.WorkingDirectory,
                TranscriptPath: freshest.Path,
                LastActivityUtc: new DateTimeOffset(freshest.Mtime, TimeSpan.Zero),
                State: state,
                PendingMessage: stateEntry?.Message,
                Window: window.Window,
                WindowTitle: window.Title,
                Subagents: subagents));
        }

        snapshots.Sort(CompareForDisplay);
        return snapshots;
    }

    static int CompareForDisplay(SessionSnapshot a, SessionSnapshot b)
    {
        var byState = ((int)a.RollupState).CompareTo((int)b.RollupState);
        if (byState != 0) return byState;
        return b.LastActivityUtc.CompareTo(a.LastActivityUtc);
    }

    static bool VerifyCwdMatch(string transcriptPath, string expectedCwd)
    {
        try
        {
            using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var firstLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(firstLine)) return true;

            using var doc = System.Text.Json.JsonDocument.Parse(firstLine);
            if (!doc.RootElement.TryGetProperty("cwd", out var cwdElement)) return true;
            var actualCwd = cwdElement.GetString();
            return string.Equals(actualCwd, expectedCwd, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    static IReadOnlyList<SubagentSnapshot> EnumerateSubagents(string slugDir, string sessionId)
    {
        var subagentsDir = Path.Combine(slugDir, sessionId, "subagents");
        if (!Directory.Exists(subagentsDir)) return Array.Empty<SubagentSnapshot>();

        var files = Directory.GetFiles(subagentsDir, "agent-*.jsonl", SearchOption.TopDirectoryOnly);
        if (files.Length == 0) return Array.Empty<SubagentSnapshot>();

        var result = new List<SubagentSnapshot>(files.Length);
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var agentId = name.StartsWith("agent-", StringComparison.Ordinal) ? name["agent-".Length..] : name;
            var mtime = File.GetLastWriteTimeUtc(file);
            var state = SessionStateClassifier.Classify(stateEntry: null, file);
            result.Add(new SubagentSnapshot(agentId, file, new DateTimeOffset(mtime, TimeSpan.Zero), state));
        }
        return result;
    }
}
