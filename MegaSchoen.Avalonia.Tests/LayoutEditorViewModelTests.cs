using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DisplayManager.Core;
using DisplayManager.Core.Models;
using DisplayManager.Core.Services;
using MegaSchoen.Avalonia.ViewModels;
using MegaSchoen.Avalonia.Views;

namespace MegaSchoen.Avalonia.Tests;

[TestClass]
public sealed class LayoutEditorViewModelTests
{
    string temporaryDirectory = "";

    [TestInitialize]
    public void SetUp()
    {
        temporaryDirectory = Path.Combine(
            Path.GetTempPath(), "MegaSchoen.Avalonia.Tests", Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void InitialLayoutClonesPresetAndRendersRotationAwareGeometry()
    {
        var preset = Profile(
            Monitor("primary", 0, 0, primary: true),
            Monitor("portrait", 1920, 0, rotation: 90));
        var viewModel = CreateViewModel(preset);

        viewModel.SetViewport(900, 500);

        Assert.HasCount(2, viewModel.Monitors);
        Assert.AreEqual(1080, viewModel.Monitors[1].FootprintWidth);
        Assert.AreEqual(1920, viewModel.Monitors[1].FootprintHeight);
        Assert.IsGreaterThan(0, viewModel.Monitors[0].CanvasWidth);
        Assert.IsGreaterThan(viewModel.Monitors[0].CanvasX, viewModel.Monitors[1].CanvasX);
        Assert.AreNotSame(preset.Displays[0], viewModel.Monitors[0].Config);
    }

    [TestMethod]
    public void LayoutEditorPageLoadsCompiledAxaml()
    {
        var page = new LayoutEditorPage();

        Assert.IsNotNull(page);
    }

    [TestMethod]
    public void DragUpdatesDesktopPositionAndInvalidatesVerifiedLayout()
    {
        var viewModel = CreateViewModel(Profile(
            Monitor("primary", 0, 0, primary: true),
            Monitor("other", 2200, 0)));
        viewModel.SetViewport(1000, 500);
        var moving = viewModel.Monitors[1];
        var originalX = moving.Config.PositionX;

        viewModel.DragMonitor(moving, moving.CanvasX + 40, moving.CanvasY + 20);

        Assert.AreNotEqual(originalX, moving.Config.PositionX);
        Assert.IsFalse(viewModel.CanCommit);
        Assert.AreEqual("Modified — Test required before commit.", viewModel.Status);
    }

    [TestMethod]
    public void SelectingMonitorDoesNotRebuildCanvasDuringPointerCapture()
    {
        var viewModel = CreateViewModel(Profile(
            Monitor("primary", 0, 0, primary: true),
            Monitor("other", 2200, 0)));
        var redraws = 0;
        viewModel.LayoutChanged += () => redraws++;

        viewModel.Selected = viewModel.Monitors[1];

        Assert.AreEqual(0, redraws);
        Assert.IsTrue(viewModel.Monitors[1].IsSelected);
    }

    [TestMethod]
    public void DragSnapsNearbyMonitorEdgeWhenEnabled()
    {
        var viewModel = CreateViewModel(Profile(
            Monitor("primary", 0, 0, primary: true),
            Monitor("other", 2200, 0)));
        viewModel.SetViewport(1000, 500);
        viewModel.SnappingEnabled = true;
        var moving = viewModel.Monitors[1];
        var canvasDeltaForNearEdge = (1900 - moving.Config.PositionX) * viewModel.RenderScale;

        viewModel.DragMonitor(moving, moving.CanvasX + canvasDeltaForNearEdge, moving.CanvasY);

        Assert.AreEqual(1920, moving.Config.PositionX);
        Assert.StartsWith("Snapped", viewModel.Status);
    }

    [TestMethod]
    public void NormalizeAnchorsPrimaryAndSeparatesOverlaps()
    {
        var viewModel = CreateViewModel(Profile(
            Monitor("primary", 500, 500, primary: true),
            Monitor("other", 700, 500)));

        viewModel.NormalizeCommand.Execute(null);

        var primary = viewModel.Monitors.Single(monitor => monitor.IsPrimary);
        var other = viewModel.Monitors.Single(monitor => !monitor.IsPrimary);
        Assert.AreEqual(0, primary.Config.PositionX);
        Assert.AreEqual(0, primary.Config.PositionY);
        Assert.IsFalse(Overlaps(primary, other));
        Assert.IsTrue(TouchesEdge(primary, other));
        Assert.AreEqual("Normalized.", viewModel.Status);
    }

    [TestMethod]
    public async Task StashLeavesPresetUnchangedAndCanBeRestored()
    {
        var preset = Profile(
            Monitor("primary", 0, 0, primary: true),
            Monitor("other", 2200, 0));
        var store = new LayoutDraftStore(temporaryDirectory);
        var viewModel = CreateViewModel(preset, store);
        viewModel.SetViewport(1000, 500);
        var moving = viewModel.Monitors[1];
        viewModel.DragMonitor(moving, moving.CanvasX + 40, moving.CanvasY);
        var stashedX = moving.Config.PositionX;

        await viewModel.StashAsync();

        Assert.AreEqual(2200, preset.Displays[1].PositionX);
        var restored = CreateViewModel(preset, store);
        await restored.RestoreStashAsync();
        Assert.AreEqual(stashedX, restored.Monitors[1].Config.PositionX);
        Assert.AreEqual("Restored stashed draft.", restored.Status);
    }

    [TestMethod]
    public async Task SuccessfulTestEnablesCommitAndCommitPersistsExactDraft()
    {
        var preset = Profile(
            Monitor("primary", 0, 0, primary: true),
            Monitor("other", 2200, 0));
        var store = new LayoutDraftStore(temporaryDirectory);
        var committed = false;
        var viewModel = CreateViewModel(
            preset,
            store,
            commit: (draft, target) =>
            {
                committed = true;
                target.Displays = draft.Displays.Select(Clone).ToList();
                return Task.CompletedTask;
            });
        viewModel.SetViewport(1000, 500);
        var moving = viewModel.Monitors[1];
        viewModel.DragMonitor(moving, moving.CanvasX + 40, moving.CanvasY);
        await viewModel.StashAsync();

        await viewModel.TestAsync();

        var testedX = viewModel.Monitors[1].Config.PositionX;
        Assert.IsTrue(viewModel.CanCommit);
        Assert.IsTrue(viewModel.CommitCommand.CanExecute(null));
        await viewModel.CommitAsync();
        Assert.IsTrue(committed);
        Assert.AreEqual(testedX, preset.Displays[1].PositionX);
        Assert.IsNull(await store.LoadAsync(preset.Id));
        Assert.AreEqual("✓ Committed to preset.", viewModel.Status);
    }

    [TestMethod]
    public async Task EditingAfterSuccessfulTestDisablesCommit()
    {
        var viewModel = CreateViewModel(Profile(
            Monitor("primary", 0, 0, primary: true),
            Monitor("other", 2200, 0)));
        viewModel.SetViewport(1000, 500);
        await viewModel.TestAsync();
        Assert.IsTrue(viewModel.CanCommit);

        var moving = viewModel.Monitors[1];
        viewModel.DragMonitor(moving, moving.CanvasX + 40, moving.CanvasY);

        Assert.IsFalse(viewModel.CanCommit);
        Assert.IsFalse(viewModel.CommitCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task DriftedTestKeepsCommitBlockedAndReportsFailure()
    {
        var service = new LayoutCommitService(
            apply: _ => new ApplyResult { Success = true },
            compare: _ => new DriftReport
            {
                Monitors =
                [
                    new MonitorDrift
                    {
                        MonitorName = "primary",
                        Kind = DriftKind.FieldMismatch
                    }
                ]
            });
        var viewModel = CreateViewModel(
            Profile(Monitor("primary", 0, 0, primary: true)),
            commitService: service);

        await viewModel.TestAsync();

        Assert.IsFalse(viewModel.CanCommit);
        Assert.StartsWith("✗ Drift detected", viewModel.Status);
    }

    LayoutEditorViewModel CreateViewModel(
        SavedDisplayProfile preset,
        LayoutDraftStore? store = null,
        LayoutCommitService? commitService = null,
        Func<LayoutDraft, SavedDisplayProfile, Task>? commit = null)
    {
        var service = commitService ?? new LayoutCommitService(
            apply: _ => new ApplyResult { Success = true },
            compare: profile => new DriftReport
            {
                Monitors = profile.Displays.Select(display => new MonitorDrift
                {
                    MonitorName = display.MonitorName,
                    Kind = DriftKind.Match
                }).ToList()
            });
        return new LayoutEditorViewModel(
            preset,
            store ?? new LayoutDraftStore(temporaryDirectory),
            service,
            commit);
    }

    static SavedDisplayProfile Profile(params SavedDisplayConfig[] displays) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Desk",
        Displays = displays.ToList()
    };

    static SavedDisplayConfig Monitor(
        string serial,
        int x,
        int y,
        bool primary = false,
        int rotation = 0) => new()
        {
            MonitorName = serial,
            EdidManufactureId = 1,
            EdidProductCodeId = serial.GetHashCode(StringComparison.Ordinal),
            EdidSerialNumber = serial,
            Width = 1920,
            Height = 1080,
            PositionX = x,
            PositionY = y,
            RefreshRate = 60,
            Rotation = rotation,
            IsPrimary = primary
        };

    static SavedDisplayConfig Clone(SavedDisplayConfig display) => new()
    {
        MonitorName = display.MonitorName,
        EdidManufactureId = display.EdidManufactureId,
        EdidProductCodeId = display.EdidProductCodeId,
        EdidSerialNumber = display.EdidSerialNumber,
        EdidManufactureDate = display.EdidManufactureDate,
        EdidContainerId = display.EdidContainerId,
        Width = display.Width,
        Height = display.Height,
        PositionX = display.PositionX,
        PositionY = display.PositionY,
        RefreshRate = display.RefreshRate,
        Rotation = display.Rotation,
        IsPrimary = display.IsPrimary
    };

    static bool Overlaps(
        LayoutMonitorViewModel left,
        LayoutMonitorViewModel right) =>
        left.Config.PositionX < right.Config.PositionX + right.FootprintWidth
        && left.Config.PositionX + left.FootprintWidth > right.Config.PositionX
        && left.Config.PositionY < right.Config.PositionY + right.FootprintHeight
        && left.Config.PositionY + left.FootprintHeight > right.Config.PositionY;

    static bool TouchesEdge(
        LayoutMonitorViewModel left,
        LayoutMonitorViewModel right)
    {
        var yOverlap = Math.Min(
            left.Config.PositionY + left.FootprintHeight,
            right.Config.PositionY + right.FootprintHeight)
            - Math.Max(left.Config.PositionY, right.Config.PositionY);
        var xOverlap = Math.Min(
            left.Config.PositionX + left.FootprintWidth,
            right.Config.PositionX + right.FootprintWidth)
            - Math.Max(left.Config.PositionX, right.Config.PositionX);
        var touchesVertical =
            (left.Config.PositionX + left.FootprintWidth == right.Config.PositionX
                || right.Config.PositionX + right.FootprintWidth == left.Config.PositionX)
            && yOverlap > 0;
        var touchesHorizontal =
            (left.Config.PositionY + left.FootprintHeight == right.Config.PositionY
                || right.Config.PositionY + right.FootprintHeight == left.Config.PositionY)
            && xOverlap > 0;
        return touchesVertical || touchesHorizontal;
    }
}
