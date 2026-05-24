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
    public void Notification_IdlePrompt_DoesNotUpsert()
    {
        _dispatcher.Dispatch(new HookPayload
        {
            HookEventName = "Notification",
            NotificationType = "idle_prompt",
            SessionId = "s1",
            Cwd = "C:\\foo"
        });

        Assert.IsEmpty(_store.Read());
    }

    [TestMethod]
    public void UserPromptSubmit_DeletesSession()
    {
        _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        _dispatcher.Dispatch(new HookPayload { HookEventName = "UserPromptSubmit", SessionId = "s1" });

        Assert.IsEmpty(_store.Read());
    }

    [TestMethod]
    public void UserPromptSubmit_DeletesAwaitingInputSession()
    {
        _store.Upsert("s1", new SessionEntry
        {
            Cwd = "C:\\foo",
            NotifiedAt = DateTimeOffset.UtcNow,
            Reason = WaitingReason.AwaitingInput
        });
        _dispatcher.Dispatch(new HookPayload { HookEventName = "UserPromptSubmit", SessionId = "s1" });

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
    public void PostToolUse_LeavesEntryIntact()
    {
        var existing = new SessionEntry
        {
            Cwd = "C:\\foo",
            NotifiedAt = DateTimeOffset.UtcNow.AddSeconds(-1),
            Reason = WaitingReason.Permission
        };
        _store.Upsert("s1", existing);

        _dispatcher.Dispatch(new HookPayload { HookEventName = "PostToolUse", SessionId = "s1" });

        var entries = _store.Read();
        Assert.IsTrue(entries.ContainsKey("s1"));
        Assert.AreEqual(WaitingReason.Permission, entries["s1"].Reason);
        Assert.AreEqual(existing.NotifiedAt, entries["s1"].NotifiedAt);
    }

    [TestMethod]
    public void PostToolUse_NoMatchingEntry_IsNoop()
    {
        _dispatcher.Dispatch(new HookPayload { HookEventName = "PostToolUse", SessionId = "unrelated" });
        Assert.IsEmpty(_store.Read());
    }

    [TestMethod]
    public void SessionEnd_DeletesSession()
    {
        _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
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
