using Claude.Core.Models;

namespace Claude.Core.Tests;

[TestClass]
public class HookDispatcherTests
{
    string _tempDir = "";
    StateStore _store = null!;
    HookDispatcher _dispatcher = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"needy-{Guid.NewGuid():N}");
        _store = new StateStore(_tempDir);
        _dispatcher = new HookDispatcher(_store);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void Notification_PermissionPrompt_UpsertsSession()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification",
            NotificationType = "permission_prompt",
            SessionId = "s1",
            Cwd = "C:\\foo",
            Message = "Claude needs your permission"
        });

        var entries = _store.Read();
        Assert.IsTrue(entries.ContainsKey("s1"));
        Assert.AreEqual("C:\\foo", entries["s1"].Cwd);
        Assert.AreEqual("Claude needs your permission", entries["s1"].Message);
        Assert.AreEqual(WaitingReason.Permission, entries["s1"].Reason);
    }

    [TestMethod]
    public void Notification_IdlePrompt_UpsertsAwaitingInput()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification",
            NotificationType = "idle_prompt",
            SessionId = "s1",
            Cwd = "C:\\foo"
        });

        var entries = _store.Read();
        Assert.IsTrue(entries.ContainsKey("s1"));
        Assert.AreEqual(WaitingReason.AwaitingInput, entries["s1"].Reason);
    }

    [TestMethod]
    public void Notification_ElicitationDialog_UpsertsAwaitingInput()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification",
            NotificationType = "elicitation_dialog",
            SessionId = "s1",
            Cwd = "C:\\foo",
            Message = "Claude is asking a question"
        });

        var entries = _store.Read();
        Assert.IsTrue(entries.ContainsKey("s1"));
        Assert.AreEqual(WaitingReason.AwaitingInput, entries["s1"].Reason);
        Assert.AreEqual("Claude is asking a question", entries["s1"].Message);
    }

    [TestMethod]
    public void Notification_ElicitationComplete_OverwritesAwaitingInputWithWorking()
    {
        _store.Upsert("s1", new SessionEntry
        {
            Cwd = "C:\\foo",
            NotifiedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            Reason = WaitingReason.AwaitingInput
        });

        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification",
            NotificationType = "elicitation_complete",
            SessionId = "s1",
            Cwd = "C:\\foo"
        });

        Assert.AreEqual(WaitingReason.Working, _store.Read()["s1"].Reason);
    }

    [TestMethod]
    public void Permission_ThenNextToolEvent_ClearsLatch_DenyPath()
    {
        // Deny fires no PostToolUse for the denied call, but the session keeps
        // going: the next PreToolUse (or Stop) must move it off PendingPermission.
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification", NotificationType = "permission_prompt",
            SessionId = "s1", Cwd = "C:\\foo", Message = "needs permission"
        });
        Assert.AreEqual(WaitingReason.Permission, _store.Read()["s1"].Reason);

        // user denies; assistant continues; next tool attempt:
        _dispatcher.Dispatch(new HookPayload { HookEventName = "PreToolUse", SessionId = "s1", Cwd = "C:\\foo" });

        Assert.AreEqual(WaitingReason.Working, _store.Read()["s1"].Reason,
            "the next tool event after a deny clears the stale permission latch");
    }

    [TestMethod]
    public void Notification_OtherType_LeavesEntryIntact()
    {
        var existing = new SessionEntry
        {
            Cwd = "C:\\foo",
            NotifiedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            Reason = WaitingReason.Working
        };
        _store.Upsert("s1", existing);

        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification",
            NotificationType = "auth_success",
            SessionId = "s1"
        });

        Assert.AreEqual(WaitingReason.Working, _store.Read()["s1"].Reason);
        Assert.AreEqual(existing.NotifiedAt, _store.Read()["s1"].NotifiedAt);
    }

    [TestMethod]
    public void UserPromptSubmit_UpsertsWorking()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "UserPromptSubmit",
            SessionId = "s1",
            Cwd = "C:\\foo"
        });

        var entries = _store.Read();
        Assert.IsTrue(entries.ContainsKey("s1"));
        Assert.AreEqual(WaitingReason.Working, entries["s1"].Reason);
    }

    [TestMethod]
    public void UserPromptSubmit_OverwritesAwaitingInputWithWorking()
    {
        _store.Upsert("s1", new SessionEntry
        {
            Cwd = "C:\\foo",
            NotifiedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            Reason = WaitingReason.AwaitingInput
        });

        _dispatcher.Dispatch(new HookPayload { HookEventName = "UserPromptSubmit", SessionId = "s1", Cwd = "C:\\foo" });

        Assert.AreEqual(WaitingReason.Working, _store.Read()["s1"].Reason);
    }

    [TestMethod]
    public void PreToolUse_UpsertsWorking()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "PreToolUse",
            SessionId = "s1",
            Cwd = "C:\\foo"
        });

        Assert.AreEqual(WaitingReason.Working, _store.Read()["s1"].Reason);
    }

    [TestMethod]
    public void Notification_PermissionPrompt_CapturesTranscriptPath()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification",
            NotificationType = "permission_prompt",
            SessionId = "s1",
            Cwd = "C:\\foo",
            TranscriptPath = "C:\\Users\\me\\.claude\\projects\\C--foo\\s1.jsonl",
            Message = "Claude needs your permission"
        });

        var entries = _store.Read();
        Assert.AreEqual(
            "C:\\Users\\me\\.claude\\projects\\C--foo\\s1.jsonl",
            entries["s1"].TranscriptPath);
    }

    [TestMethod]
    public void Stop_UpsertsSessionAsAwaitingInput()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Stop",
            SessionId = "s1",
            Cwd = "C:\\foo",
            TranscriptPath = "C:\\foo\\transcript.jsonl"
        });

        var entries = _store.Read();
        Assert.IsTrue(entries.ContainsKey("s1"));
        Assert.AreEqual(WaitingReason.AwaitingInput, entries["s1"].Reason);
        Assert.AreEqual("C:\\foo", entries["s1"].Cwd);
        Assert.AreEqual("C:\\foo\\transcript.jsonl", entries["s1"].TranscriptPath);
    }

    [TestMethod]
    public void Stop_AfterPermissionPrompt_OverwritesReason()
    {
        _store.Upsert("s1", new SessionEntry
        {
            Cwd = "C:\\foo",
            NotifiedAt = DateTimeOffset.UtcNow,
            Reason = WaitingReason.Permission
        });

        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Stop",
            SessionId = "s1",
            Cwd = "C:\\foo"
        });

        Assert.AreEqual(WaitingReason.AwaitingInput, _store.Read()["s1"].Reason);
    }

    [TestMethod]
    public void Stop_RefreshesNotifiedAt()
    {
        var earlier = DateTimeOffset.UtcNow.AddMinutes(-10);
        _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = earlier });

        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Stop",
            SessionId = "s1",
            Cwd = "C:\\foo"
        });

        Assert.IsTrue(_store.Read()["s1"].NotifiedAt > earlier);
    }

    [TestMethod]
    public void PostToolUse_OverwritesPermissionWithWorking()
    {
        // The core bug: approving a permission runs the tool (PostToolUse fires),
        // which must clear the stale PendingPermission latch back to Working.
        _store.Upsert("s1", new SessionEntry
        {
            Cwd = "C:\\foo",
            NotifiedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            Reason = WaitingReason.Permission
        });

        _dispatcher.Dispatch(new HookPayload { HookEventName = "PostToolUse", SessionId = "s1", Cwd = "C:\\foo" });

        Assert.AreEqual(WaitingReason.Working, _store.Read()["s1"].Reason);
    }

    [TestMethod]
    public void PostToolUse_NoMatchingEntry_CreatesWorking()
    {
        _dispatcher.Dispatch(new HookPayload { HookEventName = "PostToolUse", SessionId = "s1", Cwd = "C:\\foo" });

        var entries = _store.Read();
        Assert.IsTrue(entries.ContainsKey("s1"));
        Assert.AreEqual(WaitingReason.Working, entries["s1"].Reason);
    }

    [TestMethod]
    public void PostToolUse_WhenAlreadyWorking_DoesNotRewrite()
    {
        // PostToolUse fires after every tool; a no-op state must not rewrite the
        // file (which would wake the dashboard watcher dozens of times per turn).
        var existing = new SessionEntry
        {
            Cwd = "C:\\foo",
            TranscriptPath = "C:\\foo\\t.jsonl",
            NotifiedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
            Reason = WaitingReason.Working
        };
        _store.Upsert("s1", existing);

        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "PostToolUse",
            SessionId = "s1",
            Cwd = "C:\\foo",
            TranscriptPath = "C:\\foo\\t.jsonl"
        });

        Assert.AreEqual(existing.NotifiedAt, _store.Read()["s1"].NotifiedAt);
    }

    [TestMethod]
    public void SessionEnd_DeletesSession()
    {
        _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        _dispatcher.Dispatch(new HookPayload { HookEventName = "SessionEnd", SessionId = "s1" });

        Assert.IsEmpty(_store.Read());
    }

    [TestMethod]
    public void SessionEnd_DeletesAwaitingInputSession()
    {
        _store.Upsert("s1", new SessionEntry
        {
            Cwd = "C:\\foo",
            NotifiedAt = DateTimeOffset.UtcNow,
            Reason = WaitingReason.AwaitingInput
        });
        _dispatcher.Dispatch(new HookPayload { HookEventName = "SessionEnd", SessionId = "s1" });

        Assert.IsEmpty(_store.Read());
    }

    [TestMethod]
    public void UnknownEvent_IsNoop()
    {
        _dispatcher.Dispatch(new HookPayload { HookEventName = "SomeOtherEvent", SessionId = "s1" });
        Assert.IsEmpty(_store.Read());
    }

    [TestMethod]
    public void MissingSessionId_IsNoop()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification",
            NotificationType = "permission_prompt"
            // SessionId deliberately null
        });
        Assert.IsEmpty(_store.Read());
    }
}
