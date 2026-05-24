using Claude.Core;
using Claude.Core.Models;
using Claude.Core.Tests.Fakes;

namespace Claude.Core.Tests;

[TestClass]
public class ActiveSessionEnumeratorTests
{
    [TestMethod]
    public void Enumerate_NoWindows_ReturnsEmpty()
    {
        using var fixture = new ClaudeProjectsFixture();
        var locator = new FakeProcessLocator();
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);

        var result = enumerator.Enumerate();
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Enumerate_WindowCwdHasNoProjectsDir_ProducesNoSessions()
    {
        using var fixture = new ClaudeProjectsFixture();
        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(
            ProcessId: 100,
            Window: WindowToken.FromHandle(new IntPtr(1)),
            Title: "cmd",
            WorkingDirectory: @"C:\nowhere\that\matches"));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);
        Assert.AreEqual(0, enumerator.Enumerate().Count);
    }

    [TestMethod]
    public void Enumerate_OneWindowOneTranscriptAssistantLast_ReturnsWorking()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\proj";
        var slug = SlugEncoder.Encode(cwd);
        fixture.AddSession(slug, "abc-123",
            """{"type":"assistant","message":{}}""",
            DateTime.UtcNow);

        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(
            ProcessId: 100,
            Window: WindowToken.FromHandle(new IntPtr(1)),
            Title: "cmd",
            WorkingDirectory: cwd));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);
        var result = enumerator.Enumerate();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("abc-123", result[0].SessionId);
        Assert.AreEqual(cwd, result[0].Cwd);
        Assert.AreEqual(SessionState.Working, result[0].State);
        Assert.AreEqual(0, result[0].Subagents.Count);
    }

    [TestMethod]
    public void Enumerate_MultipleTranscriptsSameSlug_PicksFreshest()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\proj";
        var slug = SlugEncoder.Encode(cwd);

        var older = DateTime.UtcNow.AddMinutes(-30);
        var newer = DateTime.UtcNow;
        fixture.AddSession(slug, "old-id",
            """{"type":"user","message":{}}""", older);
        fixture.AddSession(slug, "new-id",
            """{"type":"assistant","message":{}}""", newer);

        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(100, WindowToken.FromHandle(new IntPtr(1)), "cmd", cwd));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);

        var result = enumerator.Enumerate();
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("new-id", result[0].SessionId);
    }

    [TestMethod]
    public void Enumerate_FirstLineCwdMismatch_SkipsTranscript()
    {
        using var fixture = new ClaudeProjectsFixture();
        var actualCwd = @"C:\foo\bar";
        var slug = SlugEncoder.Encode(actualCwd);

        // The window says C:\foo\bar, but the transcript was written with cwd=C:\foo-bar
        // (the colliding partner). The classifier should detect mismatch and skip.
        fixture.AddSession(slug, "wrong-cwd-id",
            """{"type":"assistant","message":{},"cwd":"C:\\foo-bar"}""",
            DateTime.UtcNow);

        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(100, WindowToken.FromHandle(new IntPtr(1)), "cmd", actualCwd));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);

        Assert.AreEqual(0, enumerator.Enumerate().Count);
    }

    [TestMethod]
    public void Enumerate_SessionWithSubagents_RollsUpAndExposesIndividualStates()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\proj";
        var slug = SlugEncoder.Encode(cwd);

        // Parent: Idle (last user line).
        fixture.AddSession(slug, "parent-1",
            """{"type":"user","message":{}}""", DateTime.UtcNow.AddSeconds(-10));
        // Two subagents: one Working, one Idle.
        fixture.AddSubagent(slug, "parent-1", "abc",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);
        fixture.AddSubagent(slug, "parent-1", "def",
            """{"type":"user","message":{}}""", DateTime.UtcNow.AddSeconds(-5));

        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(100, WindowToken.FromHandle(new IntPtr(1)), "cmd", cwd));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(SessionState.Idle, result[0].State);
        Assert.AreEqual(2, result[0].Subagents.Count);
        Assert.AreEqual(SessionState.Working, result[0].RollupState);
    }

    [TestMethod]
    public void Enumerate_TwoWindowsSameCwd_DistinctJsonls_AttributedByStartTime()
    {
        // Regression: when multiple Claude CLI processes share a cwd, the old
        // freshest-pick logic collapsed them onto one SessionId, producing
        // duplicate snapshots and a downstream UpdateUi crash on the UI side.
        // With per-window start-time matching each window should be attributed
        // to its own JSONL.
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\proj";
        var slug = SlugEncoder.Encode(cwd);

        var sessionAStart = DateTime.UtcNow.AddMinutes(-10);
        var sessionBStart = DateTime.UtcNow.AddMinutes(-2);
        fixture.AddSession(slug, "session-a",
            """{"type":"assistant","message":{}}""",
            mtimeUtc: sessionAStart.AddSeconds(30),
            creationTimeUtc: sessionAStart);
        fixture.AddSession(slug, "session-b",
            """{"type":"assistant","message":{}}""",
            mtimeUtc: sessionBStart.AddSeconds(30),
            creationTimeUtc: sessionBStart);

        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(
            ProcessId: 100,
            Window: WindowToken.FromHandle(new IntPtr(1)),
            Title: "cmd-a",
            WorkingDirectory: cwd,
            StartTimeUtc: new DateTimeOffset(sessionAStart, TimeSpan.Zero)));
        locator.Windows.Add(new ClaudeWindow(
            ProcessId: 101,
            Window: WindowToken.FromHandle(new IntPtr(2)),
            Title: "cmd-b",
            WorkingDirectory: cwd,
            StartTimeUtc: new DateTimeOffset(sessionBStart, TimeSpan.Zero)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);

        var result = enumerator.Enumerate();

        Assert.AreEqual(2, result.Count, "expected one snapshot per window, not collapsed-to-freshest");
        var sessionIds = result.Select(s => s.SessionId).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "session-a", "session-b" }, sessionIds.ToList());

        var aSnapshot = result.Single(s => s.SessionId == "session-a");
        var bSnapshot = result.Single(s => s.SessionId == "session-b");
        Assert.AreEqual("cmd-a", aSnapshot.WindowTitle, "session-a should be attributed to the older claude process's window");
        Assert.AreEqual("cmd-b", bSnapshot.WindowTitle, "session-b should be attributed to the newer claude process's window");
    }

    [TestMethod]
    public void Enumerate_StateStoreUpgradesIdleToAwaitingInput_AndSortsToTop()
    {
        using var fixture = new ClaudeProjectsFixture();

        var cwdA = @"C:\repo\a";
        var cwdB = @"C:\repo\b";
        var slugA = SlugEncoder.Encode(cwdA);
        var slugB = SlugEncoder.Encode(cwdB);

        fixture.AddSession(slugA, "session-a",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);
        fixture.AddSession(slugB, "session-b",
            """{"type":"user","message":{}}""", DateTime.UtcNow);

        var stateDir = Path.Combine(fixture.Root, "state");
        var store = new StateStore(stateDir);
        store.Upsert("session-b", new SessionEntry
        {
            Cwd = cwdB,
            TranscriptPath = Path.Combine(fixture.Root, slugB, "session-b.jsonl"),
            NotifiedAt = DateTimeOffset.UtcNow,
            Reason = WaitingReason.AwaitingInput
        });

        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(100, WindowToken.FromHandle(new IntPtr(1)), "cmd-a", cwdA));
        locator.Windows.Add(new ClaudeWindow(101, WindowToken.FromHandle(new IntPtr(2)), "cmd-b", cwdB));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.AreEqual(2, result.Count);
        Assert.AreEqual("session-b", result[0].SessionId);
        Assert.AreEqual(SessionState.AwaitingInput, result[0].State);
        Assert.AreEqual("session-a", result[1].SessionId);
        Assert.AreEqual(SessionState.Working, result[1].State);
    }
}
