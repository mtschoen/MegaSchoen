using Claude.Core;
using Claude.Core.Models;
using Claude.Core.Tests.Fakes;

namespace Claude.Core.Tests;

// Smoke tests for the data pipeline backing SessionsPageViewModel.
// SessionCardViewModel and SessionsPageViewModel live in the MegaSchoen (MAUI) project,
// which cannot be referenced from this test project without pulling in the full MAUI SDK.
// These tests therefore verify the domain-layer inputs (ActiveSessionEnumerator + SessionSnapshot)
// that feed the view model — covering the same logic path that RefreshNow() exercises.
[TestClass]
public class SessionsPageViewModelSmokeTests
{
    [TestMethod]
    public void Enumerate_WithNoWindows_ReturnsEmptyList_DoesNotThrow()
    {
        using var fixture = new ClaudeProjectsFixture();
        var locator = new FakeProcessLocator();
        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);

        var snapshots = enumerator.Enumerate();

        Assert.AreEqual(0, snapshots.Count);
    }

    [TestMethod]
    public void SessionSnapshot_RollupState_ReflectsWorstSubagentState()
    {
        var subagents = new[]
        {
            new SubagentSnapshot("a1", "/path/a1.jsonl", DateTimeOffset.UtcNow, SessionState.Working),
            new SubagentSnapshot("a2", "/path/a2.jsonl", DateTimeOffset.UtcNow, SessionState.PendingPermission)
        };

        var snapshot = new SessionSnapshot(
            SessionId: "abc123",
            Cwd: @"C:\repo\proj",
            TranscriptPath: "/path/abc123.jsonl",
            LastActivityUtc: DateTimeOffset.UtcNow,
            State: SessionState.Working,
            PendingMessage: null,
            Window: WindowToken.FromHandle(new IntPtr(1)),
            WindowTitle: "cmd",
            Subagents: subagents);

        Assert.AreEqual(SessionState.PendingPermission, snapshot.RollupState);
    }

    [TestMethod]
    public void SessionSnapshot_RollupState_UsesSessionStateWhenSubagentsAreBetter()
    {
        var subagents = new[]
        {
            new SubagentSnapshot("a1", "/path/a1.jsonl", DateTimeOffset.UtcNow, SessionState.Idle)
        };

        var snapshot = new SessionSnapshot(
            SessionId: "abc123",
            Cwd: @"C:\repo\proj",
            TranscriptPath: "/path/abc123.jsonl",
            LastActivityUtc: DateTimeOffset.UtcNow,
            State: SessionState.AwaitingInput,
            PendingMessage: null,
            Window: WindowToken.FromHandle(new IntPtr(1)),
            WindowTitle: "cmd",
            Subagents: subagents);

        Assert.AreEqual(SessionState.AwaitingInput, snapshot.RollupState);
    }

    [TestMethod]
    public void SessionSnapshot_RollupState_NoSubagents_EqualsSessionState()
    {
        var snapshot = new SessionSnapshot(
            SessionId: "abc123",
            Cwd: @"C:\repo\proj",
            TranscriptPath: "/path/abc123.jsonl",
            LastActivityUtc: DateTimeOffset.UtcNow,
            State: SessionState.PendingPermission,
            PendingMessage: "allow?",
            Window: WindowToken.FromHandle(new IntPtr(1)),
            WindowTitle: "cmd",
            Subagents: Array.Empty<SubagentSnapshot>());

        Assert.AreEqual(SessionState.PendingPermission, snapshot.RollupState);
    }

    [TestMethod]
    public void Enumerate_WithSessionAndSubagent_ProducesOneSnapshot()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\myproject";
        var slug = SlugEncoder.Encode(cwd);
        var sessionId = "test-session-id-1";

        fixture.AddSession(slug, sessionId,
            """{"type":"user","message":{"role":"user","content":[{"type":"tool_result"}]}}""",
            DateTime.UtcNow);
        fixture.AddSubagent(slug, sessionId, "agent-1",
            """{"type":"assistant","message":{}}""",
            DateTime.UtcNow);

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(new ClaudeWindow(
            ProcessId: 42,
            Window: WindowToken.FromHandle(new IntPtr(2)),
            Title: "cmd",
            WorkingDirectory: cwd));

        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);

        var snapshots = enumerator.Enumerate();

        Assert.AreEqual(1, snapshots.Count);
        Assert.AreEqual(sessionId, snapshots[0].SessionId);
        Assert.AreEqual(1, snapshots[0].Subagents.Count);
    }
}
