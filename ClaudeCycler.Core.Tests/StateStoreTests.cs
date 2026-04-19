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
}
