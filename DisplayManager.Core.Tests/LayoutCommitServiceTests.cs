using DisplayManager.Core;
using DisplayManager.Core.Models;
using DisplayManager.Core.Services;

namespace DisplayManager.Core.Tests;

[TestClass]
public class LayoutCommitServiceTests
{
    static SavedDisplayConfig Monitor(string serial, int x = 0, bool primary = false) =>
        new()
        {
            EdidManufactureId = 1, EdidProductCodeId = 2, EdidSerialNumber = serial,
            PositionX = x, Width = 1920, Height = 1080, RefreshRate = 60, IsPrimary = primary
        };

    static SavedDisplayProfile Preset() => new()
    {
        Name = "P",
        Displays = [Monitor("A", 0, primary: true)]
    };

    [TestMethod]
    public async Task Test_NoDrift_SetsVerifiedHash()
    {
        var draft = new LayoutDraft { Displays = [Monitor("A", 0, primary: true)] };
        var service = new LayoutCommitService(
            apply: _ => new ApplyResult { Success = true },
            compare: _ => new DriftReport { Monitors = [new MonitorDrift { Kind = DriftKind.Match }] });

        var report = await service.TestAsync(draft);

        Assert.IsTrue(report.Matches);
        Assert.AreEqual(LayoutHasher.Hash(draft.Displays), draft.VerifiedHash);
        Assert.IsTrue(service.CanCommit(draft));
    }

    [TestMethod]
    public async Task Test_WithDrift_DoesNotSetHash_BlocksCommit()
    {
        var draft = new LayoutDraft { Displays = [Monitor("A", 0, primary: true)] };
        var service = new LayoutCommitService(
            apply: _ => new ApplyResult { Success = true },
            compare: _ => new DriftReport { Monitors = [new MonitorDrift { Kind = DriftKind.FieldMismatch }] });

        var report = await service.TestAsync(draft);

        Assert.IsFalse(report.Matches);
        Assert.AreEqual("", draft.VerifiedHash);
        Assert.IsFalse(service.CanCommit(draft));
    }

    [TestMethod]
    public void EditingAfterVerify_InvalidatesStamp()
    {
        var draft = new LayoutDraft { Displays = [Monitor("A", 0, primary: true)] };
        draft.VerifiedHash = LayoutHasher.Hash(draft.Displays);
        var service = new LayoutCommitService(_ => new ApplyResult { Success = true }, _ => new DriftReport());

        Assert.IsTrue(service.CanCommit(draft));
        draft.Displays[0].PositionX = 100; // edit after verify
        Assert.IsFalse(service.CanCommit(draft));
    }

    [TestMethod]
    public async Task Commit_WhenNotVerified_Throws()
    {
        var draft = new LayoutDraft { PresetId = Guid.NewGuid(), Displays = [Monitor("A", 0, primary: true)] };
        var service = new LayoutCommitService(_ => new ApplyResult { Success = true }, _ => new DriftReport());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(draft, Preset()));
    }

    [TestMethod]
    public async Task Commit_WhenPresetDeleted_Throws()
    {
        var preset = new SavedDisplayProfile { Name = "Gone", Displays = [Monitor("A", 0, primary: true)] };
        // Preset is NOT saved anywhere; presetExists returns false.
        var draft = new LayoutDraft { PresetId = preset.Id, Displays = [Monitor("A", 0, primary: true)] };
        draft.VerifiedHash = LayoutHasher.Hash(draft.Displays);

        var service = new LayoutCommitService(
            apply: _ => new ApplyResult { Success = true },
            compare: _ => new DriftReport { Monitors = [new MonitorDrift { Kind = DriftKind.Match }] },
            presetExists: _ => Task.FromResult(false));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(draft, preset, requireExisting: true));
    }
}
