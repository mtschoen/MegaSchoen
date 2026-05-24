using Claude.Core.Models;

namespace Claude.Core.Tests;

[TestClass]
public class StateStoreTests
{
    string _tempDir = "";

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"needy-{Guid.NewGuid():N}");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void Read_MissingDirectory_ReturnsEmpty()
    {
        var store = new StateStore(_tempDir);
        var entries = store.Read();
        Assert.IsEmpty(entries);
    }

    [TestMethod]
    public void Read_CorruptSessionFile_SkipsItButReturnsOthers()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "bad.json"), "{ not valid json");
        var store = new StateStore(_tempDir);
        store.Upsert("good", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });

        var entries = store.Read();
        Assert.HasCount(1, entries);
        Assert.IsTrue(entries.ContainsKey("good"));
    }

    [TestMethod]
    public void Read_OneSession_Parses()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(
            Path.Combine(_tempDir, "abc.json"),
            """{"cwd":"C:\\foo","notifiedAt":"2026-04-18T12:00:00Z","message":"hi"}""");

        var entries = new StateStore(_tempDir).Read();
        Assert.HasCount(1, entries);
        Assert.AreEqual("C:\\foo", entries["abc"].Cwd);
        Assert.AreEqual("hi", entries["abc"].Message);
    }

    [TestMethod]
    public void Upsert_NewSession_PersistsEntry()
    {
        var store = new StateStore(_tempDir);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow, Message = "hi" });

        var entries = store.Read();
        Assert.HasCount(1, entries);
        Assert.AreEqual("C:\\foo", entries["sess1"].Cwd);
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "sess1.json")));
    }

    [TestMethod]
    public void Upsert_ExistingSession_Overwrites()
    {
        var store = new StateStore(_tempDir);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\bar", NotifiedAt = DateTimeOffset.UtcNow });

        Assert.AreEqual("C:\\bar", store.Read()["sess1"].Cwd);
    }

    [TestMethod]
    public void Upsert_ConcurrentSessionsDoNotCorruptEachOther()
    {
        var store = new StateStore(_tempDir);
        store.Upsert("alpha", new SessionEntry { Cwd = "C:\\a", NotifiedAt = DateTimeOffset.UtcNow });
        store.Upsert("beta", new SessionEntry { Cwd = "C:\\b", NotifiedAt = DateTimeOffset.UtcNow });
        store.Upsert("gamma", new SessionEntry { Cwd = "C:\\c", NotifiedAt = DateTimeOffset.UtcNow });

        var entries = store.Read();
        Assert.HasCount(3, entries);
        Assert.AreEqual("C:\\a", entries["alpha"].Cwd);
        Assert.AreEqual("C:\\b", entries["beta"].Cwd);
        Assert.AreEqual("C:\\c", entries["gamma"].Cwd);
    }

    [TestMethod]
    public void Delete_ExistingSession_RemovesFile()
    {
        var store = new StateStore(_tempDir);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
        store.Delete("sess1");

        Assert.IsEmpty(store.Read());
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "sess1.json")));
    }

    [TestMethod]
    public void Delete_DoesNotAffectOtherSessions()
    {
        var store = new StateStore(_tempDir);
        store.Upsert("keep", new SessionEntry { Cwd = "C:\\keep", NotifiedAt = DateTimeOffset.UtcNow });
        store.Upsert("drop", new SessionEntry { Cwd = "C:\\drop", NotifiedAt = DateTimeOffset.UtcNow });
        store.Delete("drop");

        var entries = store.Read();
        Assert.HasCount(1, entries);
        Assert.IsTrue(entries.ContainsKey("keep"));
    }

    [TestMethod]
    public void Delete_MissingSession_IsNoop()
    {
        var store = new StateStore(_tempDir);
        store.Delete("nope"); // should not throw
        Assert.IsEmpty(store.Read());
    }

    [TestMethod]
    public void DeleteAll_RemovesEverySessionFile()
    {
        var store = new StateStore(_tempDir);
        store.Upsert("a", new SessionEntry { Cwd = "C:\\a", NotifiedAt = DateTimeOffset.UtcNow });
        store.Upsert("b", new SessionEntry { Cwd = "C:\\b", NotifiedAt = DateTimeOffset.UtcNow });
        store.DeleteAll();

        Assert.IsEmpty(store.Read());
    }

    [TestMethod]
    public void EnumerateSessionIds_ReturnsIdsWithoutDeserializing()
    {
        var store = new StateStore(_tempDir);
        store.Upsert("a", new SessionEntry { Cwd = "C:\\a", NotifiedAt = DateTimeOffset.UtcNow });
        store.Upsert("b", new SessionEntry { Cwd = "C:\\b", NotifiedAt = DateTimeOffset.UtcNow });

        CollectionAssert.AreEquivalent(new[] { "a", "b" }, store.EnumerateSessionIds().ToList());
    }

    [TestMethod]
    public void Upsert_LeavesNoTempFileBehind()
    {
        var store = new StateStore(_tempDir);
        store.Upsert("sess1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });

        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "sess1.json")));
        Assert.IsFalse(File.Exists(Path.Combine(_tempDir, "sess1.json.tmp")));
    }

    [TestMethod]
    public void Reason_RoundtripsThroughStore()
    {
        var store = new StateStore(_tempDir);
        store.Upsert("s1", new SessionEntry
        {
            Cwd = "C:\\foo",
            NotifiedAt = DateTimeOffset.UtcNow,
            Reason = WaitingReason.AwaitingInput
        });

        Assert.AreEqual(WaitingReason.AwaitingInput, new StateStore(_tempDir).Read()["s1"].Reason);
    }

    [TestMethod]
    public void Reason_LegacyEntryWithoutField_DefaultsToPermission()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(
            Path.Combine(_tempDir, "s1.json"),
            """
            {
              "cwd": "C:\\foo",
              "transcriptPath": null,
              "notifiedAt": "2026-04-01T00:00:00+00:00",
              "message": "old"
            }
            """);

        Assert.AreEqual(WaitingReason.Permission, new StateStore(_tempDir).Read()["s1"].Reason);
    }
}
