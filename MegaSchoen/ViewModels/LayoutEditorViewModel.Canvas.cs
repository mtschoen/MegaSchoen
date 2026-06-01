using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DisplayManager.Core.Models;
using DisplayManager.Core.Services;

namespace MegaSchoen.ViewModels;

// Canvas geometry, viewport transform, zoom/pan, drag-and-snap, and the
// per-monitor selection mutations (primary, rotation, mode, normalize) for
// the layout editor, split from the main view model to keep each file focused.
public partial class LayoutEditorViewModel
{
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
}
