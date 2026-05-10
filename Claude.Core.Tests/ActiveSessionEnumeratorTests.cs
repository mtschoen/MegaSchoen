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
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));

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
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));

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
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));

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
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));
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
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));
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
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(SessionState.Idle, result[0].State);
        Assert.AreEqual(2, result[0].Subagents.Count);
        Assert.AreEqual(SessionState.Working, result[0].RollupState);
    }
}
