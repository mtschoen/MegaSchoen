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
}
