using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core.Tests;

[TestClass]
public class HookDispatcherTests
{
    string _tempFile = "";
    StateStore _store = null!;
    HookDispatcher _dispatcher = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"needy-{Guid.NewGuid():N}.json");
        _store = new StateStore(_tempFile);
        _dispatcher = new HookDispatcher(_store);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
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

        var file = _store.Read();
        Assert.IsTrue(file.Sessions.ContainsKey("s1"));
        Assert.AreEqual("C:\\foo", file.Sessions["s1"].Cwd);
        Assert.AreEqual("Claude needs your permission", file.Sessions["s1"].Message);
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

        Assert.AreEqual(0, _store.Read().Sessions.Count);
    }

    [TestMethod]
    public void UserPromptSubmit_DeletesSession()
    {
        _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        _dispatcher.Dispatch(new HookPayload { HookEventName = "UserPromptSubmit", SessionId = "s1" });

        Assert.AreEqual(0, _store.Read().Sessions.Count);
    }

    [TestMethod]
    public void Stop_DeletesSession()
    {
        _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        _dispatcher.Dispatch(new HookPayload { HookEventName = "Stop", SessionId = "s1" });

        Assert.AreEqual(0, _store.Read().Sessions.Count);
    }

    [TestMethod]
    public void UnknownEvent_IsNoop()
    {
        _dispatcher.Dispatch(new HookPayload { HookEventName = "SomeOtherEvent", SessionId = "s1" });
        Assert.AreEqual(0, _store.Read().Sessions.Count);
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
        Assert.AreEqual(0, _store.Read().Sessions.Count);
    }
}
