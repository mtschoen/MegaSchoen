namespace Claude.Core.Tests.Fakes;

internal sealed class ClaudeProjectsFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"claude-projects-{Guid.NewGuid():N}");

    // Creation times cannot be faked on the real filesystem everywhere
    // (File.SetCreationTimeUtc cannot set the Linux birth time), so the
    // fixture records each session's intended creation time and hands it back
    // through GetCreationTimeUtc, which tests inject into
    // ActiveSessionEnumerator as its creationTimeUtcSource.
    readonly Dictionary<string, DateTime> _creationTimesUtc = new(StringComparer.OrdinalIgnoreCase);

    public ClaudeProjectsFixture()
    {
        Directory.CreateDirectory(Root);
    }

    public string AddSession(string slug, string sessionId, string lastLineJson, DateTime mtimeUtc, DateTime? creationTimeUtc = null) =>
        AddSession(slug, sessionId, new[] { lastLineJson }, mtimeUtc, creationTimeUtc);

    public string AddSession(string slug, string sessionId, IReadOnlyList<string> lines, DateTime mtimeUtc, DateTime? creationTimeUtc = null)
    {
        var dir = Path.Combine(Root, slug);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{sessionId}.jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        // Default matches Windows semantics: a freshly written file's creation
        // time is "now" even when the test backdates its mtime.
        _creationTimesUtc[path] = creationTimeUtc ?? DateTime.UtcNow;
        File.SetLastWriteTimeUtc(path, mtimeUtc);
        return path;
    }

    public DateTime GetCreationTimeUtc(string path) =>
        _creationTimesUtc.TryGetValue(path, out var recorded)
            ? recorded
            : TranscriptCreationTime.GetUtc(path);

    public string AddSubagent(string slug, string sessionId, string agentId, string lastLineJson, DateTime mtimeUtc)
    {
        var dir = Path.Combine(Root, slug, sessionId, "subagents");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"agent-{agentId}.jsonl");
        File.WriteAllText(path, lastLineJson + "\n");
        File.SetLastWriteTimeUtc(path, mtimeUtc);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
