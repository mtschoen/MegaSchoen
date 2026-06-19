using Claude.Core.Models;

namespace Claude.Core.Tests;

// Exercises the positional-record accessors for the snapshot models so the
// generated property getters are covered and round-trip the constructed values.
[TestClass]
public class ModelRecordsTests
{
    [TestMethod]
    public void SubagentSnapshot_ExposesConstructorArguments()
    {
        var when = DateTimeOffset.UtcNow;
        var snapshot = new SubagentSnapshot("agent-7", @"C:\transcripts\a.jsonl", when, SessionState.Working);

        Assert.AreEqual("agent-7", snapshot.AgentId);
        Assert.AreEqual(@"C:\transcripts\a.jsonl", snapshot.TranscriptPath);
        Assert.AreEqual(when, snapshot.LastActivityUtc);
        Assert.AreEqual(SessionState.Working, snapshot.State);
    }

    [TestMethod]
    public void SessionSnapshot_ExposesConstructorArguments()
    {
        var when = DateTimeOffset.UtcNow;
        var snapshot = new SessionSnapshot(
            "sess-1", @"C:\work", @"C:\transcripts\s.jsonl", when, SessionState.AwaitingInput,
            "needs input", WindowToken.Null, "My Terminal", Array.Empty<SubagentSnapshot>());

        Assert.AreEqual("sess-1", snapshot.SessionId);
        Assert.AreEqual(@"C:\work", snapshot.Cwd);
        Assert.AreEqual(@"C:\transcripts\s.jsonl", snapshot.TranscriptPath);
        Assert.AreEqual("needs input", snapshot.PendingMessage);
        Assert.AreEqual("My Terminal", snapshot.WindowTitle);
    }

    [TestMethod]
    public void SessionSnapshot_RollupState_UsesOwnStateWhenNoSubagents()
    {
        var snapshot = new SessionSnapshot(
            "sess-2", "cwd", "t.jsonl", DateTimeOffset.UtcNow, SessionState.Working,
            null, WindowToken.Null, null, Array.Empty<SubagentSnapshot>());

        Assert.AreEqual(SessionState.Working, snapshot.RollupState);
    }
}
