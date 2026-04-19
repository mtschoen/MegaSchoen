using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core.Tests;

[TestClass]
public class StateStoreTests
{
    string _tempFile = "";

    [TestInitialize]
    public void Setup()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"needy-{Guid.NewGuid():N}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [TestMethod]
    public void Read_MissingFile_ReturnsEmpty()
    {
        var store = new StateStore(_tempFile);
        var file = store.Read();
        Assert.AreEqual(0, file.Sessions.Count);
        Assert.AreEqual(1, file.Version);
    }

    [TestMethod]
    public void Read_CorruptJson_ReturnsEmpty()
    {
        File.WriteAllText(_tempFile, "{ not valid json");
        var store = new StateStore(_tempFile);
        var file = store.Read();
        Assert.AreEqual(0, file.Sessions.Count);
    }

    [TestMethod]
    public void Read_ValidFile_Parses()
    {
        File.WriteAllText(_tempFile, """
        {
          "version": 1,
          "sessions": {
            "abc": { "cwd": "C:\\foo", "notifiedAt": "2026-04-18T12:00:00Z", "message": "hi" }
          }
        }
        """);
        var store = new StateStore(_tempFile);
        var file = store.Read();
        Assert.AreEqual(1, file.Sessions.Count);
        Assert.AreEqual("C:\\foo", file.Sessions["abc"].Cwd);
        Assert.AreEqual("hi", file.Sessions["abc"].Message);
    }

    [TestMethod]
    public void Upsert_NewSession_PersistsEntry()
    {
        var store = new StateStore(_tempFile);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow, Message = "hi" });

        var file = store.Read();
        Assert.AreEqual(1, file.Sessions.Count);
        Assert.AreEqual("C:\\foo", file.Sessions["sess1"].Cwd);
    }

    [TestMethod]
    public void Upsert_ExistingSession_Overwrites()
    {
        var store = new StateStore(_tempFile);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\bar", NotifiedAt = DateTimeOffset.UtcNow });

        var file = store.Read();
        Assert.AreEqual("C:\\bar", file.Sessions["sess1"].Cwd);
    }

    [TestMethod]
    public void Delete_ExistingSession_RemovesEntry()
    {
        var store = new StateStore(_tempFile);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        store.Delete("sess1");

        var file = store.Read();
        Assert.AreEqual(0, file.Sessions.Count);
    }

    [TestMethod]
    public void Delete_MissingSession_IsNoop()
    {
        var store = new StateStore(_tempFile);
        store.Delete("nope"); // should not throw
        var file = store.Read();
        Assert.AreEqual(0, file.Sessions.Count);
    }

    [TestMethod]
    public void Write_UsesTempFileThenRename()
    {
        var store = new StateStore(_tempFile);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });

        Assert.IsTrue(File.Exists(_tempFile));
        Assert.IsFalse(File.Exists(_tempFile + ".tmp"), "tmp file should have been renamed away");
    }

    [TestMethod]
    public void ReadFresh_OmitsEntriesOlderThanCutoff()
    {
        var store = new StateStore(_tempFile);
        var now = DateTimeOffset.UtcNow;
        store.Upsert("fresh", new SessionEntry { Cwd = "C:\\a", NotifiedAt = now });
        store.Upsert("stale", new SessionEntry { Cwd = "C:\\b", NotifiedAt = now - TimeSpan.FromMinutes(45) });

        var file = store.ReadFresh(TimeSpan.FromMinutes(30));

        Assert.AreEqual(1, file.Sessions.Count);
        Assert.IsTrue(file.Sessions.ContainsKey("fresh"));
        Assert.IsFalse(file.Sessions.ContainsKey("stale"));
    }
}
