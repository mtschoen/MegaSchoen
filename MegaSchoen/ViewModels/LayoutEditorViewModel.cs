using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DisplayManager.Core.Models;
using DisplayManager.Core.Services;

namespace MegaSchoen.ViewModels;

public class LayoutEditorViewModel : INotifyPropertyChanged
{
    readonly SavedDisplayProfile _preset;
    readonly LayoutDraftStore _draftStore;
    readonly LayoutCommitService _commitService;

    LayoutDraft _draft = new();
    MonitorRectViewModel? _selected;
    bool _snappingEnabled;
    bool _isBusy;
    string _status = "";

    // Transform model (real desktop px → canvas units). Two intents are tracked separately:
    //
    //   • MOUSE intent  (_mouseScale, _mousePanX/Y) — what the user asked for via wheel-zoom and
    //     middle-drag pan. This is the baseline the user controls directly.
    //   • LAYOUT fit    — the scale/centering needed to show the WHOLE current layout.
    //
    // The EFFECTIVE transform (_renderScale, _offsetX/Y) used to draw is reconciled from the two
    // in RecomputeTransform so that a drag/drop can only ever EXPAND past the mouse baseline (zoom
    // out / pan to keep a monitor in view), never tighten it — and relaxes back to the mouse
    // baseline once the layout no longer needs the extra room. Held fixed during a drag.
    double _viewportWidth, _viewportHeight;
    double _renderScale = CanvasScale;
    double _originRealX, _originRealY;
    double _offsetX = CanvasPadding, _offsetY = CanvasPadding;
    double _layoutRealWidth = 1, _layoutRealHeight = 1;

    // User-desired (mouse) zoom + pan. Initialised to the first fit so the editor opens framed.
    double _mouseScale = CanvasScale;
    double _mousePanX, _mousePanY;
    bool _mouseInitialized;

    // Fallback scale used before the canvas has been measured. 1 canvas unit per 10 real px.
    public const double CanvasScale = 0.10;

    // Hard zoom clamps (real px → canvas px). Min keeps very large multi-monitor layouts
    // fit-able (so a dragged-out monitor is never stranded off-canvas); Max caps zoom-in.
    public const double MinRenderScale = 0.004;
    public const double MaxRenderScale = 0.2;
    // Mouse-wheel zoom multiplier per notch.
    const double ZoomStep = 1.1;
    // Keep at least this much of the layout (canvas px) on screen when middle-drag panning.
    const double MinVisiblePan = 60;

    public LayoutEditorViewModel(SavedDisplayProfile preset,
        LayoutDraftStore? draftStore = null,
        LayoutCommitService? commitService = null)
    {
        _preset = preset;
        _draftStore = draftStore ?? new LayoutDraftStore();
        _commitService = commitService ?? new LayoutCommitService();

        Title = $"Edit Layout — {preset.Name}";

        NormalizeCommand = new Command(Normalize, () => !IsBusy);
        TestCommand = new Command(async () => await TestAsync(), () => !IsBusy && Monitors.Count > 0);
        StashCommand = new Command(async () => await StashAsync(), () => !IsBusy);
        CommitCommand = new Command(async () => await CommitAsync(), () => !IsBusy && CanCommit);
        SetPrimaryCommand = new Command<MonitorRectViewModel>(SetPrimary, CanSetPrimary);
        RotateCommand = new Command<string>(RotateSelected);
        RemoveSelectedCommand = new Command(RemoveSelected, () => HasSelection && Monitors.Count > 1);

        _ = InitializeAsync();
    }

    public string Title { get; }
    public ObservableCollection<MonitorRectViewModel> Monitors { get; } = [];
    public ObservableCollection<DisplayMode> AvailableModes { get; } = [];

    /// <summary>Active live displays not already in the draft — candidates for "Add monitor".</summary>
    public ObservableCollection<SavedDisplayConfig> AddableMonitors { get; } = [];

    /// <summary>
    /// Raised when a command mutates the layout in place (primary/rotation/mode) without changing
    /// the <see cref="Monitors"/> collection. The view redraws its imperatively-built canvas in
    /// response. Collection-changing edits (add/remove/normalize/reset) redraw via Monitors'
    /// CollectionChanged instead, and drag updates the dragged border inline (no full redraw).
    /// </summary>
    public event Action? LayoutChanged;

    public MonitorRectViewModel? Selected
    {
        get => _selected;
        set
        {
            if (_selected is not null) _selected.IsSelected = false;
            _selected = value;
            if (_selected is not null) _selected.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            (RemoveSelectedCommand as Command)?.ChangeCanExecute();
            (SetPrimaryCommand as Command<MonitorRectViewModel>)?.ChangeCanExecute();
            LoadAvailableModesForSelection();
        }
    }

    public bool HasSelection => _selected is not null;

    public bool SnappingEnabled
    {
        get => _snappingEnabled;
        set { _snappingEnabled = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            _isBusy = value;
            OnPropertyChanged();
            RefreshCommandStates();
        }
    }

    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }

    public bool CanCommit => _commitService.CanCommit(_draft);

    public ICommand NormalizeCommand { get; }
    public ICommand TestCommand { get; }
    public ICommand StashCommand { get; }
    public ICommand CommitCommand { get; }
    public ICommand SetPrimaryCommand { get; }
    public ICommand RotateCommand { get; }
    public ICommand RemoveSelectedCommand { get; }

    async Task InitializeAsync()
    {
        // Prefer an existing stashed draft; else start from the preset's stored layout.
        var existing = await _draftStore.LoadAsync(_preset.Id);
        _draft = existing ?? new LayoutDraft
        {
            PresetId = _preset.Id,
            PresetName = _preset.Name,
            Displays = _preset.Displays.Select(CloneConfig).ToList(),
            VerifiedHash = ""
        };
        RebuildCanvas();
        // Select a monitor up front (the primary, else the first) so the selection-detail panel
        // is present from the moment the editor opens, rather than popping in on first click.
        if (Selected is null && Monitors.Count > 0)
        {
            Selected = Monitors.FirstOrDefault(m => m.Config.IsPrimary) ?? Monitors[0];
            LayoutChanged?.Invoke(); // repaint so the selected rect's highlight shows
        }
        Status = existing is not null ? "Restored stashed draft." : "Editing a draft of the preset.";
    }

    void RebuildCanvas()
    {
        // Rebuilding replaces every MonitorRectViewModel, so the old selection points at a
        // detached instance. Remember which monitor was selected (by EDID identity — the rebuilt
        // configs are clones, not the same references) and re-select it afterward, so an edit like
        // Normalize keeps the monitor selected instead of collapsing the side panel and jumping.
        var previousSelection = _selected?.Config;

        Selected = null;
        Monitors.Clear();
        RecomputeTransform();
        foreach (var config in _draft.Displays)
        {
            var rect = new MonitorRectViewModel(config);
            ApplyGeometry(rect);
            Monitors.Add(rect);
        }
        RefreshAddableMonitors();

        if (previousSelection is not null)
        {
            // Null when the selected monitor was removed — selection correctly stays cleared.
            Selected = Monitors.FirstOrDefault(m => SameMonitor(m.Config, previousSelection));
        }

        OnPropertyChanged(nameof(CanCommit));
        RefreshCommandStates();
        // Redraw so the restored selection's highlight (and final geometry) is painted; the
        // per-Add CollectionChanged redraws ran before the selection was restored.
        LayoutChanged?.Invoke();
    }

    // Same physical monitor across a rebuild, matched by EDID (clones don't share references).
    static bool SameMonitor(SavedDisplayConfig a, SavedDisplayConfig b) =>
        a.EdidManufactureId == b.EdidManufactureId
        && a.EdidProductCodeId == b.EdidProductCodeId
        && a.EdidSerialNumber == b.EdidSerialNumber;

    // Canvas-space padding kept around the laid-out monitors.
    const double CanvasPadding = 16;

    /// <summary>
    /// Reconcile the MOUSE intent (wheel zoom + middle-drag pan) with the LAYOUT fit into the
    /// effective draw transform. Effective scale = min(mouse, fit) clamped to the zoom band, so a
    /// drag/drop can only ever zoom OUT past the mouse baseline, never tighter. Effective pan
    /// blends from the mouse pan toward the centered layout pan as the layout becomes the binding
    /// constraint, so an expanded view relaxes back to the user's pan once the layout fits again.
    /// Held stable during a drag (recomputed on rebuild / resize / wheel / pan / drag-release).
    /// </summary>
    void RecomputeTransform()
    {
        MeasureLayout(out var fitScale);

        // Frame the editor on first measure: open at the layout's fit, centered.
        if (!_mouseInitialized && _viewportWidth > 1 && _viewportHeight > 1)
        {
            _mouseScale = fitScale;
            _mousePanX = CenteredPan(_viewportWidth, _layoutRealWidth, fitScale);
            _mousePanY = CenteredPan(_viewportHeight, _layoutRealHeight, fitScale);
            _mouseInitialized = true;
        }

        var effScale = Math.Clamp(Math.Min(_mouseScale, fitScale), MinRenderScale, MaxRenderScale);
        var layoutPanX = CenteredPan(_viewportWidth, _layoutRealWidth, effScale);
        var layoutPanY = CenteredPan(_viewportHeight, _layoutRealHeight, effScale);

        // 0 = mouse fully in control; 1 = layout took over (zoomed out to fit). Blends the pan so
        // the handoff between "the user's view" and "fit everything" is smooth, not a jump.
        var t = _mouseScale > fitScale ? Math.Clamp(1 - fitScale / _mouseScale, 0, 1) : 0;

        _renderScale = effScale;
        _offsetX = Lerp(_mousePanX, layoutPanX, t);
        _offsetY = Lerp(_mousePanY, layoutPanY, t);
    }

    // Compute the layout bounding box (real px) into _originReal*/_layoutReal*, plus the scale at
    // which the whole box fits the viewport (clamped to the zoom band). Monitors can sit at
    // negative desktop coordinates, so we translate by the min corner. Falls back to CanvasScale
    // before the canvas is measured.
    void MeasureLayout(out double fitScale)
    {
        _originRealX = 0;
        _originRealY = 0;
        _layoutRealWidth = 1;
        _layoutRealHeight = 1;
        fitScale = CanvasScale;
        if (_draft.Displays.Count == 0)
        {
            return;
        }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var d in _draft.Displays)
        {
            var (footprintWidth, footprintHeight) = Footprint(d);
            minX = Math.Min(minX, d.PositionX);
            minY = Math.Min(minY, d.PositionY);
            maxX = Math.Max(maxX, d.PositionX + footprintWidth);
            maxY = Math.Max(maxY, d.PositionY + footprintHeight);
        }
        _originRealX = minX;
        _originRealY = minY;
        _layoutRealWidth = Math.Max(1, maxX - minX);
        _layoutRealHeight = Math.Max(1, maxY - minY);

        if (_viewportWidth > 1 && _viewportHeight > 1)
        {
            var scaleX = (_viewportWidth - 2 * CanvasPadding) / _layoutRealWidth;
            var scaleY = (_viewportHeight - 2 * CanvasPadding) / _layoutRealHeight;
            fitScale = Math.Min(scaleX, scaleY);
        }
        fitScale = Math.Clamp(fitScale, MinRenderScale, MaxRenderScale);
    }

    static double CenteredPan(double viewport, double layoutReal, double scale) =>
        viewport > 1 ? (viewport - layoutReal * scale) / 2 : CanvasPadding;

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    void ApplyGeometry(MonitorRectViewModel rect)
    {
        rect.CanvasX = (rect.Config.PositionX - _originRealX) * _renderScale + _offsetX;
        rect.CanvasY = (rect.Config.PositionY - _originRealY) * _renderScale + _offsetY;
        rect.CanvasWidth = rect.FootprintWidth * _renderScale;
        rect.CanvasHeight = rect.FootprintHeight * _renderScale;
    }

    void RedrawAll()
    {
        foreach (var rect in Monitors)
        {
            ApplyGeometry(rect);
        }
        LayoutChanged?.Invoke();
    }

    /// <summary>Mouse-wheel zoom, anchored at the cursor so the point under the pointer stays put.
    /// Adjusts the MOUSE baseline (scale + pan); the effective view follows via RecomputeTransform,
    /// clamped to the hard zoom band.</summary>
    public void Zoom(double factor, double anchorCanvasX, double anchorCanvasY)
    {
        var target = Math.Clamp(_mouseScale * factor, MinRenderScale, MaxRenderScale);
        if (target == _mouseScale)
        {
            return; // already at a clamp limit
        }
        // Hold the real point under the cursor fixed (in mouse-transform space) across the zoom.
        var valX = (anchorCanvasX - _mousePanX) / _mouseScale;
        var valY = (anchorCanvasY - _mousePanY) / _mouseScale;
        _mouseScale = target;
        _mousePanX = anchorCanvasX - valX * _mouseScale;
        _mousePanY = anchorCanvasY - valY * _mouseScale;
        ClampMousePan();
        RecomputeTransform();
        RedrawAll();
    }

    /// <summary>Wheel one notch in (positive delta) or out, anchored at the cursor.</summary>
    public void ZoomBy(int wheelDelta, double anchorCanvasX, double anchorCanvasY) =>
        Zoom(wheelDelta > 0 ? ZoomStep : 1.0 / ZoomStep, anchorCanvasX, anchorCanvasY);

    /// <summary>Middle-drag pan: shift the MOUSE baseline by a canvas-space delta.</summary>
    public void PanBy(double deltaCanvasX, double deltaCanvasY)
    {
        _mousePanX += deltaCanvasX;
        _mousePanY += deltaCanvasY;
        ClampMousePan();
        RecomputeTransform();
        RedrawAll();
    }

    // Keep the layout from being panned entirely off-canvas: at least MinVisiblePan canvas px of
    // the bounding box must stay within the viewport on each axis.
    void ClampMousePan()
    {
        _mousePanX = ClampPanAxis(_mousePanX, _viewportWidth, _layoutRealWidth * _mouseScale);
        _mousePanY = ClampPanAxis(_mousePanY, _viewportHeight, _layoutRealHeight * _mouseScale);
    }

    static double ClampPanAxis(double pan, double viewport, double scaledLayout)
    {
        if (viewport <= 1)
        {
            return pan;
        }
        var minPan = MinVisiblePan - scaledLayout; // box's far edge stays ≥ MinVisiblePan from 0
        var maxPan = viewport - MinVisiblePan;     // box's near edge stays ≤ viewport − MinVisiblePan
        return minPan <= maxPan ? Math.Clamp(pan, minPan, maxPan) : pan;
    }

    /// <summary>Re-fit when a drag gesture ends so a monitor dropped past the edge returns to view.
    /// Re-fitting only on release (never mid-drag) avoids the zoom/drag feedback loop; the mouse
    /// baseline is preserved, so the view only expands when needed and relaxes back afterward.</summary>
    public void CompleteDrag()
    {
        RecomputeTransform();
        RedrawAll();
    }

    static (int Width, int Height) Footprint(SavedDisplayConfig d) =>
        d.Rotation is 90 or 270 ? (d.Height, d.Width) : (d.Width, d.Height);

    /// <summary>Called by the view when the canvas is (re)sized; refits the layout.</summary>
    public void SetViewport(double width, double height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
        RecomputeTransform();
        foreach (var rect in Monitors)
        {
            ApplyGeometry(rect);
        }
    }

    /// <summary>Called by the view as a monitor is dragged: update the real position from canvas coords.</summary>
    public void OnMonitorDragged(MonitorRectViewModel rect, double newCanvasX, double newCanvasY)
    {
        rect.CanvasX = newCanvasX;
        rect.CanvasY = newCanvasY;
        rect.Config.PositionX = (int)Math.Round((newCanvasX - _offsetX) / _renderScale + _originRealX);
        rect.Config.PositionY = (int)Math.Round((newCanvasY - _offsetY) / _renderScale + _originRealY);

        var snapped = SnappingEnabled && SnapToNeighbors(rect);

        MarkDirty();
        if (snapped)
        {
            Status = "Snapped ✓ to a neighboring edge.";
        }
    }

    // Snapping should "feel" the same at any zoom: this many canvas (DIP) pixels of slack,
    // converted to real desktop pixels via the current render scale. The first fix scaled the
    // threshold but kept it at ~12 canvas px — barely wider than the original broken 80-real-px
    // (~7 canvas px) zone, so by hand snapping still rarely caught. ~28 canvas px is a grabby,
    // noticeable zone (≈200–350 real px at typical fit-to-view scales).
    const double SnapCanvasPixels = 28;

    // Returns true if a neighbor edge was within range and the position actually moved.
    bool SnapToNeighbors(MonitorRectViewModel rect)
    {
        var threshold = (int)Math.Round(SnapCanvasPixels / _renderScale);
        if (threshold <= 0)
        {
            return false;
        }
        var moving = new LayoutSnapper.SnapRect(
            rect.Config.PositionX, rect.Config.PositionY, rect.FootprintWidth, rect.FootprintHeight);
        var others = Monitors
            .Where(m => !ReferenceEquals(m, rect))
            .Select(m => new LayoutSnapper.SnapRect(
                m.Config.PositionX, m.Config.PositionY, m.FootprintWidth, m.FootprintHeight));
        var (snappedX, snappedY) = LayoutSnapper.Snap(moving, others, threshold);
        var changed = snappedX != rect.Config.PositionX || snappedY != rect.Config.PositionY;
        rect.Config.PositionX = snappedX;
        rect.Config.PositionY = snappedY;
        ApplyGeometry(rect);
        return changed;
    }

    void MarkDirty()
    {
        OnPropertyChanged(nameof(CanCommit));
        RefreshCommandStates();
        Status = "Modified — Test required before commit.";
    }

    void SetPrimary(MonitorRectViewModel? rect)
    {
        if (rect is null) return;
        foreach (var m in Monitors)
        {
            m.Config.IsPrimary = ReferenceEquals(m, rect);
            m.RaisePrimaryChanged();
        }
        MarkDirty();
        LayoutChanged?.Invoke();
    }

    // The selected monitor can be made primary only when it isn't already the primary.
    static bool CanSetPrimary(MonitorRectViewModel? rect) => rect is not null && !rect.Config.IsPrimary;

    void RotateSelected(string? degreesText)
    {
        if (_selected is null || !int.TryParse(degreesText, out var degrees)) return;
        _selected.Config.Rotation = degrees;
        _selected.RaiseFootprintChanged();
        ApplyGeometry(_selected);
        MarkDirty();
        LayoutChanged?.Invoke();
    }

    void LoadAvailableModesForSelection()
    {
        AvailableModes.Clear();
        if (_selected is null) return;
#if WINDOWS
        var modes = DisplayManager.Core.DisplayManager.GetSupportedModes(
            _selected.Config.EdidManufactureId, _selected.Config.EdidProductCodeId);
        foreach (var mode in modes)
        {
            AvailableModes.Add(mode);
        }
#endif
    }

    /// <summary>Apply a chosen resolution/refresh to the selected monitor (from the Advanced panel).</summary>
    public void ApplyModeToSelection(DisplayMode mode)
    {
        if (_selected is null) return;
        _selected.Config.Width = mode.Width;
        _selected.Config.Height = mode.Height;
        _selected.Config.RefreshRate = mode.RefreshRate;
        _selected.RaiseFootprintChanged();
        ApplyGeometry(_selected);
        MarkDirty();
        LayoutChanged?.Invoke();
    }

    void Normalize()
    {
        _draft.Displays = LayoutNormalizer.Normalize(_draft.Displays);
        RebuildCanvas();
        MarkDirty();
        Status = "Normalized.";
    }

    async Task TestAsync()
    {
        try
        {
            IsBusy = true;
            Status = "Testing — applying to hardware…";
            var report = await _commitService.TestAsync(_draft);
            RebuildCanvas(); // TestAsync normalizes the draft
            if (report.Matches)
            {
                Status = "✓ Verified — layout applied with no drift. You can commit.";
            }
            else
            {
                var detail = string.Join("; ",
                    report.Monitors.Where(m => m.Kind != DriftKind.Match)
                        .Select(m => $"{m.MonitorName}: {m.Kind}"));
                Status = $"✗ Drift detected — commit blocked. {detail}";
            }
        }
        catch (Exception ex)
        {
            Status = $"✗ Test failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanCommit));
        }
    }

    async Task StashAsync()
    {
        await _draftStore.SaveAsync(_draft);
        Status = "Stashed (preset unchanged).";
    }

    async Task CommitAsync()
    {
        try
        {
            IsBusy = true;
            await _commitService.CommitAsync(_draft, _preset);
            await _draftStore.DeleteAsync(_preset.Id); // committed; clear the stash
            Status = "✓ Committed to preset.";
        }
        catch (Exception ex)
        {
            Status = $"✗ Commit failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Add a live display (chosen from <see cref="AddableMonitors"/>) to the draft, then normalize.</summary>
    public void AddMonitor(SavedDisplayConfig candidate)
    {
        _draft.Displays.Add(CloneConfig(candidate));
        _draft.Displays = LayoutNormalizer.Normalize(_draft.Displays);
        _draft.VerifiedHash = "";
        RebuildCanvas();
        MarkDirty();
        var name = string.IsNullOrEmpty(candidate.MonitorName) ? candidate.EdidSerialNumber : candidate.MonitorName;
        Status = $"Added {name}. Test before commit.";
    }

    /// <summary>Remove the selected monitor from the draft. A profile's apply disables displays not in its list.</summary>
    public void RemoveSelected()
    {
        if (_selected is null || Monitors.Count <= 1)
        {
            return; // never remove the last monitor — a profile must keep at least one display
        }
        var removed = _selected.Config;
        var removedIndex = _draft.Displays.IndexOf(removed);
        _draft.Displays.Remove(removed);
        // Don't strand the layout without a primary if the removed monitor was it.
        if (removed.IsPrimary && _draft.Displays.Count > 0 && !_draft.Displays.Any(d => d.IsPrimary))
        {
            _draft.Displays[0].IsPrimary = true;
        }
        _draft.VerifiedHash = "";
        RebuildCanvas(); // clears selection (the removed monitor has no match)
        // Move the selection to a neighbor so the side panel stays populated instead of
        // collapsing and jumping the UI. Prefer the monitor that shifted into the removed slot.
        if (Monitors.Count > 0)
        {
            Selected = Monitors[Math.Min(Math.Max(removedIndex, 0), Monitors.Count - 1)];
            LayoutChanged?.Invoke(); // repaint so the newly-selected rect's highlight shows
        }
        MarkDirty();
        Status = "Removed monitor. Test before commit.";
    }

    /// <summary>Discard all edits: revert the draft to the preset's stored layout and drop any stash.</summary>
    public async Task ResetToPresetAsync()
    {
        _draft = new LayoutDraft
        {
            PresetId = _preset.Id,
            PresetName = _preset.Name,
            Displays = _preset.Displays.Select(CloneConfig).ToList(),
            VerifiedHash = ""
        };
        await _draftStore.DeleteAsync(_preset.Id);
        RebuildCanvas();
        Status = "Reset to the saved preset.";
    }

    // Active live displays whose EDID isn't already in the draft. Windows-only (needs the native
    // enumeration); empty elsewhere, like LoadAvailableModesForSelection.
    void RefreshAddableMonitors()
    {
        AddableMonitors.Clear();
#if WINDOWS
        var present = _draft.Displays
            .Select(d => (d.EdidManufactureId, d.EdidProductCodeId, d.EdidSerialNumber))
            .ToHashSet();
        foreach (var live in DisplayManager.Core.DisplayManager.GetAllDisplays()
            .Where(d => d.IsActive && d.Width > 0 && d.Height > 0))
        {
            if (present.Contains((live.EdidManufactureId, live.EdidProductCodeId, live.EdidSerialNumber)))
            {
                continue;
            }
            AddableMonitors.Add(ConfigFromLive(live));
        }
#endif
    }

#if WINDOWS
    static SavedDisplayConfig ConfigFromLive(DisplayManager.Core.DisplayInfo d) => new()
    {
        MonitorName = d.MonitorName,
        EdidManufactureId = d.EdidManufactureId,
        EdidProductCodeId = d.EdidProductCodeId,
        EdidSerialNumber = d.EdidSerialNumber,
        EdidManufactureDate = d.EdidManufactureDate,
        EdidContainerId = d.EdidContainerId,
        Width = d.Width,
        Height = d.Height,
        PositionX = d.PositionX,
        PositionY = d.PositionY,
        RefreshRate = d.RefreshRate,
        Rotation = d.Rotation,
        IsPrimary = false
    };
#endif

    void RefreshCommandStates()
    {
        (NormalizeCommand as Command)?.ChangeCanExecute();
        (TestCommand as Command)?.ChangeCanExecute();
        (StashCommand as Command)?.ChangeCanExecute();
        (CommitCommand as Command)?.ChangeCanExecute();
        (RemoveSelectedCommand as Command)?.ChangeCanExecute();
        (SetPrimaryCommand as Command<MonitorRectViewModel>)?.ChangeCanExecute();
    }

    static SavedDisplayConfig CloneConfig(SavedDisplayConfig d) => new()
    {
        MonitorName = d.MonitorName,
        EdidManufactureId = d.EdidManufactureId,
        EdidProductCodeId = d.EdidProductCodeId,
        EdidSerialNumber = d.EdidSerialNumber,
        EdidManufactureDate = d.EdidManufactureDate,
        EdidContainerId = d.EdidContainerId,
        Width = d.Width,
        Height = d.Height,
        PositionX = d.PositionX,
        PositionY = d.PositionY,
        RefreshRate = d.RefreshRate,
        Rotation = d.Rotation,
        IsPrimary = d.IsPrimary
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
