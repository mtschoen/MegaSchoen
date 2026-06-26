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
        const string cwd = @"C:\repo\proj";
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
        const string cwd = @"C:\repo\proj";
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
    public void Enumerate_PopulatesTitleFromTranscript()
    {
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\titled";
        var lines = new[]
        {
            """{"type":"ai-title","aiTitle":"Wire up the title","sessionId":"titled-1"}""",
            """{"type":"assistant","message":{}}""",
        };
        fixture.AddSession(SlugEncoder.Encode(cwd), "titled-1", lines, DateTime.UtcNow);

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(1)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result);
        Assert.AreEqual("Wire up the title", result[0].Title);
    }

    [TestMethod]
    public void Enumerate_NoTitleInTranscript_LeavesTitleNull()
    {
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\untitled";
        fixture.AddSession(SlugEncoder.Encode(cwd), "untitled-1",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(1)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result);
        Assert.IsNull(result[0].Title);
    }

    [TestMethod]
    public void Enumerate_StoreEntryNoLiveProc_IsPruned()
    {
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\dead";
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
        const string cwd = @"C:\repo\waiting";
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
        const string cwd = @"C:\repo\fresh";
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
        const string cwd = @"C:\repo\shared";
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
    public void Enumerate_ResumeStartTimeFarFromCreation_StillCorrectId_WindowAttachedViaSingleProcessRule()
    {
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\resumed";
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
        // The start-time match fails (2 days apart), but a single live process
        // in the cwd is an unambiguous association: a resumed session in the
        // cwd's only terminal is focusable.
        Assert.IsFalse(result[0].Window.IsZero, "single-process cwd attaches the window even on a start-time miss");
    }

    [TestMethod]
    public void Enumerate_WindowlessLiveProc_SurfacesWithNullWindow()
    {
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\headless";
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
        const string cwdA = @"C:\foo\bar";   // encodes to C--foo-bar
        const string cwdB = @"C:\foo-bar";   // ALSO encodes to C--foo-bar
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
        const string cwd = @"C:\repo\proj";
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

    static ClaudeWindow LiveProcWithPort(uint pid, string cwd, int sshClientPort, DateTime startUtc) =>
        new(pid, WindowToken.Null, "", cwd, new DateTimeOffset(startUtc, TimeSpan.Zero),
            SessionId: null, SshClientPort: sshClientPort);

    // Background/daemon worker: windowless, carries its authoritative --session-id.
    static ClaudeWindow BackgroundProc(uint pid, string sessionId, string cwd) =>
        new(pid, WindowToken.Null, "", cwd, DateTimeOffset.UtcNow, sessionId);

    [TestMethod]
    public void Enumerate_AuthoritativeId_NoStoreNoTranscript_StillSurfaces()
    {
        using var fixture = new ClaudeProjectsFixture(); // no transcript, empty store
        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        var locator = new FakeProcessLocator();
        locator.Sessions.Add(BackgroundProc(5000, "bg-375e9c68", @"C:\work\proj"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        var session = result.SingleOrDefault(s => s.SessionId == "bg-375e9c68");
        Assert.IsNotNull(session, "a live background worker surfaces by its authoritative id with no transcript/state yet");
        Assert.IsTrue(session!.Window.IsZero, "background worker is windowless");
    }

    [TestMethod]
    public void Enumerate_AuthoritativeId_PrefersStoreCwdOverProcessHomeCwd()
    {
        using var fixture = new ClaudeProjectsFixture(); // no transcript
        var store = new StateStore(Path.Combine(fixture.Root, "state"));
        // Live finding: a background worker's PEB cwd is often the shared home dir,
        // but its hook recorded the real task cwd. Trust the id; prefer that cwd.
        store.Upsert("bg-674a8820", new SessionEntry
        {
            Cwd = @"C:\actual\task",
            NotifiedAt = DateTimeOffset.UtcNow,
            Reason = WaitingReason.AwaitingInput
        });

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(BackgroundProc(5001, "bg-674a8820", @"C:\Users\mtsch"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        var session = result.SingleOrDefault(s => s.SessionId == "bg-674a8820");
        Assert.IsNotNull(session, "authoritative id surfaces even when cwd-keying would mis-bucket the home dir");
        Assert.AreEqual(SessionState.AwaitingInput, session!.State);
        Assert.AreEqual(@"C:\actual\task", session.Cwd, "prefer the hook-recorded cwd over the worker's home-dir PEB cwd");
    }

    [TestMethod]
    public void Enumerate_WaitingSortsAboveWorking()
    {
        using var fixture = new ClaudeProjectsFixture();
        const string cwdA = @"C:\repo\a";
        const string cwdB = @"C:\repo\b";
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

    [TestMethod]
    public void Enumerate_ThreadsSshClientPort_FromMatchedProcess()
    {
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\ssh";
        var created = DateTime.UtcNow;
        fixture.AddSession(SlugEncoder.Encode(cwd), "ssh-1",
            """{"type":"assistant","message":{}}""", created);

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProcWithPort(100, cwd, 51000, created));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result);
        Assert.AreEqual(51000, result[0].SshClientPort);
    }

    [TestMethod]
    public void Enumerate_SingleProcess_CarriesSshClientPort_EvenWhenStartTimeDoesNotMatch()
    {
        // Regression (found on llamabox): on Linux .NET's file creation time
        // tracks mtime, so an active remote session's transcript drifts past
        // AttachWindow's 30s tolerance and no window attaches. The SshClientPort
        // must still thread through when the cwd has a single unambiguous live
        // process, instead of being dropped with the failed window match.
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\ssh-drift";
        fixture.AddSession(SlugEncoder.Encode(cwd), "ssh-2",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);

        var locator = new FakeProcessLocator();
        // Process start time deliberately far from the transcript creation time,
        // so AttachWindow's start-time match fails (no window attaches).
        locator.Sessions.Add(LiveProcWithPort(100, cwd, 54861, DateTime.UtcNow.AddMinutes(-5)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result);
        Assert.AreEqual(54861, result[0].SshClientPort, "port must thread even without a window match");
        Assert.IsTrue(result[0].Window.IsZero, "no window should attach on a failed start-time match");
    }

    [TestMethod]
    public void Enumerate_SingleProcessWithWindow_AttachesWindow_EvenWhenStartTimeDoesNotMatch()
    {
        // Rider rig find (2026-06-10): a freshly started claude creates no
        // transcript until the first prompt, so the cwd's freshest OLD
        // transcript surfaces as the session and its creation time is days
        // away from the process start. With a single live process in the cwd
        // the association is unambiguous - attach its window (the same rule
        // the SshClientPort carry uses), otherwise Focus stays grayed out.
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\rider";
        fixture.AddSession(SlugEncoder.Encode(cwd), "rider-1",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow.AddDays(-6));

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(0x1234), "liminal - Rider", DateTime.UtcNow.AddMinutes(-5)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(1, result);
        Assert.IsFalse(result[0].Window.IsZero, "single live process in cwd must attach its window");
        Assert.AreEqual("liminal - Rider", result[0].WindowTitle);
    }

    [TestMethod]
    public void Enumerate_SharedCwd_OneMatches_OtherAttachedByElimination()
    {
        // Real-world (2026-06-20, schoen-lab): two sessions share a cwd. One
        // process's start time lands within tolerance of its transcript
        // creation (a confident match); the other transcript was created >30s
        // after its process started - the user took a while to type the first
        // prompt - so its start-time match fails. After the confident match
        // consumes its process, exactly one candidate and one process remain
        // unmatched: an unambiguous association by elimination, so its window
        // must attach (Focus must not gray out).
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\shared-elim";
        var slug = SlugEncoder.Encode(cwd);
        var now = DateTime.UtcNow;
        // Confident match: transcript created right as its process started.
        fixture.AddSession(slug, "matched", """{"type":"assistant","message":{}}""",
            mtimeUtc: now, creationTimeUtc: now.AddSeconds(-5));
        // Drifted: transcript created 55s after its process started (slow first prompt).
        fixture.AddSession(slug, "drifted", """{"type":"assistant","message":{}}""",
            mtimeUtc: now.AddSeconds(-2), creationTimeUtc: now.AddSeconds(-55));

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(0xAAA), "matched-term", now.AddSeconds(-5)));
        locator.Sessions.Add(LiveProc(101, cwd, new IntPtr(0xBBB), "drifted-term", now.AddMinutes(-2)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(2, result);
        Assert.IsTrue(result.All(s => !s.Window.IsZero),
            "confident match plus last-one-standing elimination focuses both sessions");
    }

    [TestMethod]
    public void Enumerate_SharedCwd_BothDrift_AttachedByCausalOrder()
    {
        // Real-world (2026-06-20, schoen-lab monorepo): two sessions in one cwd,
        // each started with a slow first prompt, so BOTH transcripts were
        // created >30s after their process started and neither matches by
        // start-time. The pairing is still unambiguous by causality: each
        // transcript was created after exactly one process's start, so each
        // session's window must attach (Focus must not gray out for either).
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\monorepo";
        var slug = SlugEncoder.Encode(cwd);
        var now = DateTime.UtcNow;
        // Session A: process started ~31 min ago, transcript created ~30 min ago.
        var procAStart = now.AddMinutes(-31);
        fixture.AddSession(slug, "sess-a", """{"type":"assistant","message":{}}""",
            mtimeUtc: now.AddMinutes(-1), creationTimeUtc: now.AddMinutes(-30));
        // Session B: process started ~4 min ago, transcript created ~1 min ago.
        var procBStart = now.AddMinutes(-4);
        fixture.AddSession(slug, "sess-b", """{"type":"assistant","message":{}}""",
            mtimeUtc: now, creationTimeUtc: now.AddMinutes(-1));

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(0xA1), "term-a", procAStart));
        locator.Sessions.Add(LiveProc(101, cwd, new IntPtr(0xB1), "term-b", procBStart));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(2, result);
        Assert.IsTrue(result.All(s => !s.Window.IsZero),
            "both sessions attach unambiguously by causal start-order even when both drift past tolerance");
        var byId = result.ToDictionary(s => s.SessionId);
        Assert.AreEqual("term-a", byId["sess-a"].WindowTitle, "older transcript pairs with the older process");
        Assert.AreEqual("term-b", byId["sess-b"].WindowTitle, "newer transcript pairs with the newer process");
    }

    [TestMethod]
    public void Enumerate_MultipleProcessesInCwd_DoesNotGuessWindow()
    {
        // The single-process rule must not extend to ambiguous cwds: with two
        // live windowed processes and stale transcripts, attaching either
        // window would risk focusing the wrong terminal.
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\rider-multi";
        fixture.AddSession(SlugEncoder.Encode(cwd), "rider-2",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow.AddDays(-6));
        fixture.AddSession(SlugEncoder.Encode(cwd), "rider-3",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow.AddDays(-3));

        var locator = new FakeProcessLocator();
        locator.Sessions.Add(LiveProc(100, cwd, new IntPtr(0x1111), "term-a", DateTime.UtcNow.AddMinutes(-5)));
        locator.Sessions.Add(LiveProc(101, cwd, new IntPtr(0x2222), "term-b", DateTime.UtcNow.AddMinutes(-5)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(2, result);
        Assert.IsTrue(result[0].Window.IsZero, "ambiguous multi-process cwd must not guess a window");
        Assert.IsTrue(result[1].Window.IsZero, "ambiguous multi-process cwd must not guess a window");
    }

    [TestMethod]
    public void Enumerate_MultipleProcessesInCwd_DoesNotGuessSshClientPort()
    {
        // Pins the documented best-effort rule: with more than one live process
        // in a cwd the session-to-process association is ambiguous, so no port
        // may be carried unless the start-time window match attributes one.
        using var fixture = new ClaudeProjectsFixture();
        const string cwd = @"C:\repo\ssh-multi";
        fixture.AddSession(SlugEncoder.Encode(cwd), "ssh-3",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);
        fixture.AddSession(SlugEncoder.Encode(cwd), "ssh-4",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);

        var locator = new FakeProcessLocator();
        // Both start times far from the transcript creation times, so the
        // window match fails and neither port has an unambiguous owner.
        locator.Sessions.Add(LiveProcWithPort(100, cwd, 51000, DateTime.UtcNow.AddMinutes(-5)));
        locator.Sessions.Add(LiveProcWithPort(101, cwd, 52000, DateTime.UtcNow.AddMinutes(-5)));
        var store = new StateStore(Path.Combine(fixture.Root, "state"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.HasCount(2, result);
        Assert.IsNull(result[0].SshClientPort, "ambiguous multi-process cwd must not guess a port");
        Assert.IsNull(result[1].SshClientPort, "ambiguous multi-process cwd must not guess a port");
    }
}
