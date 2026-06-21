using DisplayManager.Core.Models;
using DisplayManager.Core.Services;

namespace DisplayManager.Core.Tests;

[TestClass]
public class LayoutDraftStoreTests
{
    readonly string _directory;
    readonly LayoutDraftStore _store;

    public LayoutDraftStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "MegaSchoenTest", Guid.NewGuid().ToString("N"));
        _store = new LayoutDraftStore(_directory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task SaveThenLoad_RoundTrips()
    {
        var id = Guid.NewGuid();
        var draft = new LayoutDraft
        {
            PresetId = id,
            PresetName = "Test",
            Displays = [new SavedDisplayConfig { EdidSerialNumber = "A", PositionX = 0 }],
            VerifiedHash = "DEADBEEF"
        };
        await _store.SaveAsync(draft);

        var loaded = await _store.LoadAsync(id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("DEADBEEF", loaded!.VerifiedHash);
        Assert.HasCount(1, loaded.Displays);
    }

    [TestMethod]
    public async Task Load_MissingDraft_ReturnsNull()
    {
        Assert.IsNull(await _store.LoadAsync(Guid.NewGuid()));
    }

    [TestMethod]
    public async Task Delete_RemovesDraft()
    {
        var id = Guid.NewGuid();
        await _store.SaveAsync(new LayoutDraft { PresetId = id });
        await _store.DeleteAsync(id);
        Assert.IsNull(await _store.LoadAsync(id));
    }

    [TestMethod]
    public async Task Load_CorruptDraft_ReturnsNullAndLogs()
    {
        var id = Guid.NewGuid();
        // Create the draft so it lands at the store's path, then corrupt it.
        await _store.SaveAsync(new LayoutDraft { PresetId = id });
        var file = Directory.EnumerateFiles(_directory).Single();
        await File.WriteAllTextAsync(file, "{ not valid json");

        var logged = new List<string>();
        var previousSink = DiagnosticLog.Sink;
        DiagnosticLog.Sink = logged.Add;
        try
        {
            Assert.IsNull(await _store.LoadAsync(id));
            Assert.HasCount(1, logged);
            StringAssert.Contains(logged[0], id.ToString());
        }
        finally
        {
            DiagnosticLog.Sink = previousSink;
        }
    }
}
