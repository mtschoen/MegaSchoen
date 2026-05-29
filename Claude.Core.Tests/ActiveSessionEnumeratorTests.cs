using Claude.Core;
using Claude.Core.Models;
using Claude.Core.Tests.Fakes;

namespace Claude.Core.Tests;

[TestClass]
public class ActiveSessionEnumeratorTests
{
    static ClaudeWindow LiveProc(uint pid, string cwd, IntPtr window, string title = "term", DateTime? startUtc = null) =>
        new(pid, window == IntPtr.Zero ? WindowToken.Null : WindowToken.FromHandle(window),
            window == IntPtr.Zero ? "" : title, cwd,
            new DateTimeOffset(startUtc ?? DateTime.UtcNow, TimeSpan.Zero));

    [TestMethod]
    public void Enumerate_NoLiveProcesses_ReturnsEmpty()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\proj";
        fixture.AddSession(SlugEncoder.Encode(cwd), "abc-123",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);
        var locator = new FakeProcessLocator(); // no live procs
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();
        Assert.IsEmpty(result, "no live process in any cwd => nothing surfaced (zombie pruned)");
    }

    [TestMethod]
    public void Enumerate_LiveProcWithTranscript_SurfacesTrueId()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\proj";
        fixture.AddSession(SlugEncoder.Encode(cwd), "abc-123",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(1)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result);
        Assert.AreEqual("abc-123", result[0].SessionId);
        Assert.AreEqual(cwd, result[0].Cwd);
        Assert.AreEqual(SessionState.Working, result[0].State);
        Assert.IsFalse(result[0].Window.IsZero, "a confident start-time match should attach the window");
    }

    [TestMethod]
    public void Enumerate_StoreEntryNoLiveProc_IsPruned()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\dead";
        var slug = SlugEncoder.Encode(cwd);
        fixture.AddSession(slug, "dead-1", """{"type":"assistant","message":{}}""", DateTime.UtcNow);
        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        store.Upsert("dead-1", new SessionEntry
        {
            Cwd = cwd,
            TranscriptPath = Path.Combine(fixture.Root, slug, "dead-1.jsonl"),
            NotifiedAt = DateTimeOffset.UtcNow,
            Reason = WaitingReason.Working
        });

        var locator = new FakeProcessLocator(); // terminal killed => no live proc
        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.IsEmpty(result, "Working session whose process is gone is a zombie => pruned");
    }

    [TestMethod]
    public void Enumerate_WaitingState_StaleTranscript_StillSurfaced_NoTimeCutoff()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\waiting";
        var slug = SlugEncoder.Encode(cwd);
        // Transcript last touched 3 hours ago; the user walked away from a prompt.
        var stale = DateTime.UtcNow.AddHours(-3);
        fixture.AddSession(slug, "wait-1", """{"type":"assistant","message":{}}""", stale, creationTimeUtc: stale);
        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        store.Upsert("wait-1", new SessionEntry
        {
            Cwd = cwd,
            TranscriptPath = Path.Combine(fixture.Root, slug, "wait-1.jsonl"),
            NotifiedAt = new DateTimeOffset(stale, TimeSpan.Zero),
            Reason = WaitingReason.AwaitingInput
        });

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(1), startUtc: stale)); // process still alive (blocked)
        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result, "waiting sessions never time-expire while their process lives");
        Assert.AreEqual(SessionState.AwaitingInput, result[0].State);
    }

    [TestMethod]
    public void Enumerate_TranscriptOnly_NoStoreEntry_UsesTailReadFallback()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\fresh";
        fixture.AddSession(SlugEncoder.Encode(cwd), "fresh-1",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);
        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(1)));
        var store = new StateStore(Path.Combine(fixture.Root, "state")); // empty store

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result);
        Assert.AreEqual("fresh-1", result[0].SessionId);
        Assert.AreEqual(SessionState.Working, result[0].State, "assistant-last => Working via tail read");
    }

    [TestMethod]
    public void Enumerate_SharedCwd_TwoProcsThreeTranscripts_KeepsTwoFreshest()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\shared";
        var slug = SlugEncoder.Encode(cwd);
        fixture.AddSession(slug, "old", """{"type":"user","message":{}}""", DateTime.UtcNow.AddMinutes(-30));
        fixture.AddSession(slug, "mid", """{"type":"assistant","message":{}}""", DateTime.UtcNow.AddMinutes(-10));
        fixture.AddSession(slug, "new", """{"type":"assistant","message":{}}""", DateTime.UtcNow);

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(1)));
        locator.Sessions.Add(LiveProc(101, cwd, new IntPtr(2)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        var ids = result.Select(s => s.SessionId).ToHashSet();
        Assert.HasCount(2, result, "cap to live-process count (2)");
        Assert.IsTrue(ids.Contains("new") && ids.Contains("mid"), "freshest two kept");
        Assert.DoesNotContain("old", ids, "oldest dropped");
    }

    [TestMethod]
    public void Enumerate_ResumeStartTimeFarFromCreation_StillCorrectId_WindowMayBeNull()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\resumed";
        var slug = SlugEncoder.Encode(cwd);
        var created = DateTime.UtcNow.AddDays(-2); // old transcript (resumed)
        fixture.AddSession(slug, "resumed-1", """{"type":"assistant","message":{}}""",
            mtimeUtc: DateTime.UtcNow, creationTimeUtc: created);

        var locator = new FakeProcessLocator();
        // process started just now, far from the 2-day-old transcript creation
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(1), startUtc: DateTime.UtcNow));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result);
        Assert.AreEqual("resumed-1", result[0].SessionId, "identity is the filename, not a start-time guess");
        Assert.IsTrue(result[0].Window.IsZero, "no confident window match => Focus disabled, but session still shown");
    }

    [TestMethod]
    public void Enumerate_WindowlessLiveProc_SurfacesWithNullWindow()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\headless";
        fixture.AddSession(SlugEncoder.Encode(cwd), "headless-1",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);
        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, IntPtr.Zero)); // windowless headless -p
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result);
        Assert.AreEqual("headless-1", result[0].SessionId);
        Assert.IsTrue(result[0].Window.IsZero);
    }

    [TestMethod]
    public void Enumerate_SlugCollision_DifferentRealCwds_NotCrossAttributed()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwdA = @"C:\foo\bar";   // encodes to C--foo-bar
        var cwdB = @"C:\foo-bar";   // ALSO encodes to C--foo-bar
        var slug = SlugEncoder.Encode(cwdA);
        Assert.AreEqual(slug, SlugEncoder.Encode(cwdB), "precondition: real collision");

        // Two transcripts in the shared slug dir, each recording its own cwd.
        fixture.AddSession(slug, "in-bar",
            """{"type":"assistant","message":{},"cwd":"C:\\foo\\bar"}""", DateTime.UtcNow);
        fixture.AddSession(slug, "in-foobar",
            """{"type":"assistant","message":{},"cwd":"C:\\foo-bar"}""", DateTime.UtcNow);

        // Only cwdA has a live process.
        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwdA, new IntPtr(1)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result, "only the transcript recorded with the live cwd surfaces");
        Assert.AreEqual("in-bar", result[0].SessionId);
    }

    [TestMethod]
    public void Enumerate_SessionWithSubagents_RollsUp()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\proj";
        var slug = SlugEncoder.Encode(cwd);
        fixture.AddSession(slug, "parent-1", """{"type":"user","message":{}}""", DateTime.UtcNow.AddSeconds(-10));
        fixture.AddSubagent(slug, "parent-1", "abc", """{"type":"assistant","message":{}}""", DateTime.UtcNow);
        fixture.AddSubagent(slug, "parent-1", "def", """{"type":"user","message":{}}""", DateTime.UtcNow.AddSeconds(-5));

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(1)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result);
        Assert.AreEqual(SessionState.Idle, result[0].State);
        Assert.HasCount(2, result[0].Subagents);
        Assert.AreEqual(SessionState.Working, result[0].RollupState);
    }

    [TestMethod]
    public void Enumerate_WaitingSortsAboveWorking()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwdA = @"C:\repo\a";
        var cwdB = @"C:\repo\b";
        var slugA = SlugEncoder.Encode(cwdA);
        var slugB = SlugEncoder.Encode(cwdB);
        fixture.AddSession(slugA, "session-a", """{"type":"assistant","message":{}}""", DateTime.UtcNow);
        fixture.AddSession(slugB, "session-b", """{"type":"user","message":{}}""", DateTime.UtcNow);

        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        store.Upsert("session-b", new SessionEntry
        {
            Cwd = cwdB,
            TranscriptPath = Path.Combine(fixture.Root, slugB, "session-b.jsonl"),
            NotifiedAt = DateTimeOffset.UtcNow,
            Reason = WaitingReason.AwaitingInput
        });

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwdA, new IntPtr(1)));
        locator.Sessions.Add(LiveProc(101, cwdB, new IntPtr(2)));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(2, result);
        Assert.AreEqual("session-b", result[0].SessionId);
        Assert.AreEqual(SessionState.AwaitingInput, result[0].State);
        Assert.AreEqual("session-a", result[1].SessionId);
    }
}
