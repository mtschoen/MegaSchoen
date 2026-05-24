using Claude.Core.Models;

namespace Claude.Core;

public sealed class ActiveSessionEnumerator
{
    // Each Claude session writes a fresh JSONL when the process starts; the
    // file's creation time should land within a second or two of the
    // claude.exe process StartTime. 30s tolerance is generous enough to absorb
    // clock skew and slow startup, tight enough to reject the wrong file when
    // multiple sessions share a cwd.
    static readonly TimeSpan StartTimeMatchTolerance = TimeSpan.FromSeconds(30);

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

        var stateBySessionId = _store.Read();

        var snapshots = new List<SessionSnapshot>(capacity: windows.Count);

        // Group windows by slug so claude.exes that share a cwd compete for the
        // same JSONL pool; assignment is greedy by oldest StartTime first.
        var groupedBySlug = windows
            .Where(w => !string.IsNullOrEmpty(w.WorkingDirectory))
            .GroupBy(w => SlugEncoder.Encode(w.WorkingDirectory!));

        foreach (var group in groupedBySlug)
        {
            var slugDir = Path.Combine(_projectsRoot, group.Key);
            if (!Directory.Exists(slugDir)) continue;
            var transcripts = Directory.GetFiles(slugDir, "*.jsonl", SearchOption.TopDirectoryOnly);
            if (transcripts.Length == 0) continue;

            var candidates = transcripts
                .Select(p => new TranscriptCandidate(p, File.GetCreationTimeUtc(p), File.GetLastWriteTimeUtc(p)))
                .OrderByDescending(c => c.LastWriteUtc)
                .ToList();

            var assigned = new HashSet<string>();
            var orderedWindows = group.OrderBy(w => w.StartTimeUtc).ToList();

            foreach (var window in orderedWindows)
            {
                var match = PickTranscript(candidates, assigned, window.StartTimeUtc);
                if (match is null) continue;
                assigned.Add(match.Path);

                if (!VerifyCwdMatch(match.Path, window.WorkingDirectory!))
                {
                    Logger.Log($"ActiveSessionEnumerator: cwd mismatch on {match.Path}; skipping (slug collision)");
                    continue;
                }

                var sessionId = Path.GetFileNameWithoutExtension(match.Path);
                SessionEntry? stateEntry = stateBySessionId.TryGetValue(sessionId, out var entry) ? entry : null;
                var state = SessionStateClassifier.Classify(stateEntry, match.Path);
                var subagents = EnumerateSubagents(slugDir, sessionId);

                snapshots.Add(new SessionSnapshot(
                    SessionId: sessionId,
                    Cwd: window.WorkingDirectory!,
                    TranscriptPath: match.Path,
                    LastActivityUtc: new DateTimeOffset(match.LastWriteUtc, TimeSpan.Zero),
                    State: state,
                    PendingMessage: stateEntry?.Message,
                    Window: window.Window,
                    WindowTitle: window.Title,
                    Subagents: subagents));
            }
        }

        snapshots.Sort(CompareForDisplay);
        return snapshots;
    }

    sealed record TranscriptCandidate(string Path, DateTime CreationUtc, DateTime LastWriteUtc);

    static TranscriptCandidate? PickTranscript(
        List<TranscriptCandidate> candidates,
        HashSet<string> assigned,
        DateTimeOffset processStartUtc)
    {
        TranscriptCandidate? bestByCreation = null;
        var bestDelta = TimeSpan.MaxValue;
        foreach (var candidate in candidates)
        {
            if (assigned.Contains(candidate.Path)) continue;
            var delta = (candidate.CreationUtc - processStartUtc.UtcDateTime).Duration();
            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestByCreation = candidate;
            }
        }
        if (bestByCreation is not null && bestDelta <= StartTimeMatchTolerance) return bestByCreation;

        // No JSONL was created near this process's StartTime — likely
        // `claude --resume` reusing an older transcript. Fall back to the
        // freshest unassigned candidate (candidates are sorted by LastWriteUtc desc).
        foreach (var candidate in candidates)
        {
            if (!assigned.Contains(candidate.Path)) return candidate;
        }
        return null;
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
