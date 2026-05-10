namespace Claude.Core.Tests.Fakes;

internal sealed class ClaudeProjectsFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"claude-projects-{Guid.NewGuid():N}");

    public ClaudeProjectsFixture()
    {
        Directory.CreateDirectory(Root);
    }

    public string AddSession(string slug, string sessionId, string lastLineJson, DateTime mtimeUtc)
    {
        var dir = Path.Combine(Root, slug);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{sessionId}.jsonl");
        File.WriteAllText(path, lastLineJson + "\n");
        File.SetLastWriteTimeUtc(path, mtimeUtc);
        return path;
    }

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
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
