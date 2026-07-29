using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using DisplayManager.Core.Models;
using DisplayManager.Core.Services;

namespace MegaSchoen.Avalonia.ViewModels;

public sealed class LayoutEditorViewModel : INotifyPropertyChanged
{
    const double CanvasPadding = 16;
    const double DefaultRenderScale = 0.1;
    const double MinimumRenderScale = 0.004;
    const double MaximumRenderScale = 0.2;
    const double SnapCanvasPixels = 28;

    readonly SavedDisplayProfile preset;
    readonly LayoutDraftStore draftStore;
    readonly LayoutCommitService commitService;
    readonly Func<LayoutDraft, SavedDisplayProfile, Task> commit;
    readonly RelayCommand normalizeCommand;
    readonly AsyncRelayCommand testCommand;
    readonly AsyncRelayCommand stashCommand;
    readonly AsyncRelayCommand commitCommand;

    LayoutDraft draft;
    LayoutMonitorViewModel? selected;
    bool snappingEnabled;
    bool isBusy;
    string status;
    double viewportWidth;
    double viewportHeight;
    double renderScale = DefaultRenderScale;
    double originRealX;
    double originRealY;
    double offsetX = CanvasPadding;
    double offsetY = CanvasPadding;

    public LayoutEditorViewModel(
        SavedDisplayProfile preset,
        LayoutDraftStore? draftStore = null,
        LayoutCommitService? commitService = null,
        Func<LayoutDraft, SavedDisplayProfile, Task>? commit = null)
    {
        this.preset = preset;
        this.draftStore = draftStore ?? new LayoutDraftStore();
        this.commitService = commitService ?? new LayoutCommitService();
        this.commit = commit ?? ((layoutDraft, target) =>
            this.commitService.CommitAsync(layoutDraft, target));
        draft = DraftFromPreset(preset);
        status = "Editing a draft of the preset.";

        normalizeCommand = new RelayCommand(_ => Normalize(), _ => !IsBusy);
        testCommand = new AsyncRelayCommand(TestAsync, () => !IsBusy && Monitors.Count > 0);
        stashCommand = new AsyncRelayCommand(StashAsync, () => !IsBusy);
        commitCommand = new AsyncRelayCommand(CommitAsync, () => !IsBusy && CanCommit);
        NormalizeCommand = normalizeCommand;
        TestCommand = testCommand;
        StashCommand = stashCommand;
        CommitCommand = commitCommand;

        RebuildMonitors();
    }

    public string Title => $"Edit Layout — {preset.Name}";
    public ObservableCollection<LayoutMonitorViewModel> Monitors { get; } = [];
    public ICommand NormalizeCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand StashCommand { get; }
    public ICommand CommitCommand { get; }
    public event Action? LayoutChanged;

    public LayoutMonitorViewModel? Selected
    {
        get => selected;
        set
        {
            if (ReferenceEquals(selected, value))
            {
                return;
            }
            if (selected is not null)
            {
                selected.IsSelected = false;
            }
            selected = value;
            if (selected is not null)
            {
                selected.IsSelected = true;
            }
            OnPropertyChanged();
        }
    }

    public bool SnappingEnabled
    {
        get => snappingEnabled;
        set
        {
            if (snappingEnabled == value)
            {
                return;
            }
            snappingEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (isBusy == value)
            {
                return;
            }
            isBusy = value;
            OnPropertyChanged();
            RefreshCommandStates();
        }
    }

    public string Status
    {
        get => status;
        set
        {
            if (status == value)
            {
                return;
            }
            status = value;
            OnPropertyChanged();
        }
    }

    public bool CanCommit => commitService.CanCommit(draft);
    internal double RenderScale => renderScale;

    public async Task RestoreStashAsync()
    {
        try
        {
            var existing = await draftStore.LoadAsync(preset.Id);
            if (existing is null)
            {
                return;
            }
            draft = existing;
            RebuildMonitors();
            Status = "Restored stashed draft.";
        }
        catch (Exception exception)
        {
            Status = $"✗ Restore failed: {exception.Message}";
        }
    }

    public void SetViewport(double width, double height)
    {
        viewportWidth = width;
        viewportHeight = height;
        RebuildGeometry();
    }

    public void DragMonitor(
        LayoutMonitorViewModel monitor,
        double newCanvasX,
        double newCanvasY)
    {
        monitor.CanvasX = newCanvasX;
        monitor.CanvasY = newCanvasY;
        monitor.Config.PositionX = (int)Math.Round(
            (newCanvasX - offsetX) / renderScale + originRealX);
        monitor.Config.PositionY = (int)Math.Round(
            (newCanvasY - offsetY) / renderScale + originRealY);

        var snapped = SnappingEnabled && SnapToNeighbors(monitor);
        MarkDirty();
        if (snapped)
        {
            Status = "Snapped ✓ to a neighboring edge.";
        }
    }

    public void CompleteDrag() => RebuildGeometry();

    internal async Task TestAsync()
    {
        try
        {
            IsBusy = true;
            Status = "Testing — applying to hardware…";
            var report = await commitService.TestAsync(draft);
            RebuildMonitors();
            if (report.Matches)
            {
                Status = "✓ Verified — layout applied with no drift. You can commit.";
            }
            else
            {
                var detail = string.Join(
                    "; ",
                    report.Monitors
                        .Where(monitor => monitor.Kind != DriftKind.Match)
                        .Select(monitor => $"{monitor.MonitorName}: {monitor.Kind}"));
                Status = $"✗ Drift detected — commit blocked. {detail}";
            }
        }
        catch (Exception exception)
        {
            Status = $"✗ Test failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            NotifyCommitStateChanged();
        }
    }

    internal async Task StashAsync()
    {
        try
        {
            IsBusy = true;
            await draftStore.SaveAsync(draft);
            Status = "Stashed (preset unchanged).";
        }
        catch (Exception exception)
        {
            Status = $"✗ Stash failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal async Task CommitAsync()
    {
        try
        {
            IsBusy = true;
            await commit(draft, preset);
            await draftStore.DeleteAsync(preset.Id);
            Status = "✓ Committed to preset.";
        }
        catch (Exception exception)
        {
            Status = $"✗ Commit failed: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
            NotifyCommitStateChanged();
        }
    }

    void Normalize()
    {
        draft.Displays = LayoutNormalizer.Normalize(draft.Displays);
        RebuildMonitors();
        MarkDirty();
        Status = "Normalized.";
    }

    bool SnapToNeighbors(LayoutMonitorViewModel monitor)
    {
        var threshold = (int)Math.Round(SnapCanvasPixels / renderScale);
        if (threshold <= 0)
        {
            return false;
        }
        var moving = new LayoutSnapper.SnapRect(
            monitor.Config.PositionX,
            monitor.Config.PositionY,
            monitor.FootprintWidth,
            monitor.FootprintHeight);
        var others = Monitors
            .Where(other => !ReferenceEquals(other, monitor))
            .Select(other => new LayoutSnapper.SnapRect(
                other.Config.PositionX,
                other.Config.PositionY,
                other.FootprintWidth,
                other.FootprintHeight));
        var (snappedX, snappedY) = LayoutSnapper.Snap(moving, others, threshold);
        var changed = snappedX != monitor.Config.PositionX
            || snappedY != monitor.Config.PositionY;
        monitor.Config.PositionX = snappedX;
        monitor.Config.PositionY = snappedY;
        ApplyGeometry(monitor);
        return changed;
    }

    void RebuildMonitors()
    {
        var selectedConfig = selected?.Config;
        selected = null;
        Monitors.Clear();
        MeasureLayout();
        foreach (var config in draft.Displays)
        {
            var monitor = new LayoutMonitorViewModel(config);
            ApplyGeometry(monitor);
            Monitors.Add(monitor);
        }
        if (selectedConfig is not null)
        {
            Selected = Monitors.FirstOrDefault(monitor =>
                SameMonitor(monitor.Config, selectedConfig));
        }
        NotifyCommitStateChanged();
        LayoutChanged?.Invoke();
    }

    void RebuildGeometry()
    {
        MeasureLayout();
        foreach (var monitor in Monitors)
        {
            ApplyGeometry(monitor);
        }
        LayoutChanged?.Invoke();
    }

    void MeasureLayout()
    {
        originRealX = 0;
        originRealY = 0;
        renderScale = DefaultRenderScale;
        if (draft.Displays.Count == 0)
        {
            offsetX = CanvasPadding;
            offsetY = CanvasPadding;
            return;
        }

        var minX = draft.Displays.Min(display => display.PositionX);
        var minY = draft.Displays.Min(display => display.PositionY);
        var maxX = draft.Displays.Max(display =>
            display.PositionX + Footprint(display).Width);
        var maxY = draft.Displays.Max(display =>
            display.PositionY + Footprint(display).Height);
        originRealX = minX;
        originRealY = minY;
        var realWidth = Math.Max(1, maxX - minX);
        var realHeight = Math.Max(1, maxY - minY);

        if (viewportWidth > 1 && viewportHeight > 1)
        {
            var scaleX = (viewportWidth - 2 * CanvasPadding) / realWidth;
            var scaleY = (viewportHeight - 2 * CanvasPadding) / realHeight;
            renderScale = Math.Clamp(
                Math.Min(scaleX, scaleY),
                MinimumRenderScale,
                MaximumRenderScale);
        }
        offsetX = viewportWidth > 1
            ? (viewportWidth - realWidth * renderScale) / 2
            : CanvasPadding;
        offsetY = viewportHeight > 1
            ? (viewportHeight - realHeight * renderScale) / 2
            : CanvasPadding;
    }

    void ApplyGeometry(LayoutMonitorViewModel monitor)
    {
        monitor.CanvasX =
            (monitor.Config.PositionX - originRealX) * renderScale + offsetX;
        monitor.CanvasY =
            (monitor.Config.PositionY - originRealY) * renderScale + offsetY;
        monitor.CanvasWidth = monitor.FootprintWidth * renderScale;
        monitor.CanvasHeight = monitor.FootprintHeight * renderScale;
    }

    void MarkDirty()
    {
        Status = "Modified — Test required before commit.";
        NotifyCommitStateChanged();
    }

    void NotifyCommitStateChanged()
    {
        OnPropertyChanged(nameof(CanCommit));
        RefreshCommandStates();
    }

    void RefreshCommandStates()
    {
        normalizeCommand.RaiseCanExecuteChanged();
        testCommand.RaiseCanExecuteChanged();
        stashCommand.RaiseCanExecuteChanged();
        commitCommand.RaiseCanExecuteChanged();
    }

    static LayoutDraft DraftFromPreset(SavedDisplayProfile source) => new()
    {
        PresetId = source.Id,
        PresetName = source.Name,
        Displays = source.Displays.Select(Clone).ToList(),
        VerifiedHash = ""
    };

    static (int Width, int Height) Footprint(SavedDisplayConfig display) =>
        display.Rotation is 90 or 270
            ? (display.Height, display.Width)
            : (display.Width, display.Height);

    static bool SameMonitor(SavedDisplayConfig left, SavedDisplayConfig right) =>
        left.EdidManufactureId == right.EdidManufactureId
        && left.EdidProductCodeId == right.EdidProductCodeId
        && left.EdidSerialNumber == right.EdidSerialNumber;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
