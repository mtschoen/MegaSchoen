using DisplayManager.Core.Models;
using MegaSchoen.ViewModels;

namespace MegaSchoen;

public partial class LayoutEditorPage : ContentPage
{
    readonly LayoutEditorViewModel _viewModel;

    public LayoutEditorPage(LayoutEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        _viewModel.Monitors.CollectionChanged += (_, _) => RebuildRects();
        // In-place edits (Set Primary / Rotate / mode change) don't touch the Monitors
        // collection, so they redraw the imperatively-built canvas via this signal.
        _viewModel.LayoutChanged += RebuildRects;
        CanvasLayout.SizeChanged += OnCanvasSizeChanged;
#if WINDOWS
        // Hook mouse-wheel zoom once the canvas Border has a native view.
        CanvasBorder.HandlerChanged += OnCanvasBorderHandlerChanged;
#endif
        RebuildRects();
    }

    // Refit the layout to the canvas whenever it is measured/resized: the VM recomputes
    // the fit-to-view transform from the live viewport size, then we redraw the rects.
    void OnCanvasSizeChanged(object? sender, EventArgs e)
    {
        if (CanvasLayout.Width > 0 && CanvasLayout.Height > 0)
        {
            _viewModel.SetViewport(CanvasLayout.Width, CanvasLayout.Height);
            RebuildRects();
        }
    }

    void RebuildRects()
    {
        CanvasLayout.Children.Clear();
        foreach (var rect in _viewModel.Monitors)
        {
            CanvasLayout.Children.Add(BuildRectView(rect));
        }
    }

    Border BuildRectView(MonitorRectViewModel rect)
    {
        var border = new Border
        {
            BackgroundColor = rect.IsPrimary ? Colors.RoyalBlue : Colors.DimGray,
            Stroke = rect.IsSelected ? Colors.Orange : Colors.Black,
            StrokeThickness = rect.IsSelected ? 3 : 1,
            Content = BuildRectContent(rect)
        };

        // Tag the Border with its view-model so RefreshStrokes can match by reference
        // rather than by Label text (two same-model monitors can share a label).
        border.BindingContext = rect;

        AbsoluteLayout.SetLayoutBounds(border,
            new Rect(rect.CanvasX, rect.CanvasY, rect.CanvasWidth, rect.CanvasHeight));

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            _viewModel.Selected = rect;
            RefreshStrokes();
        };
        border.GestureRecognizers.Add(tap);

        double startX = 0, startY = 0;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    startX = rect.CanvasX;
                    startY = rect.CanvasY;
                    _viewModel.Selected = rect;
                    RefreshStrokes();
                    break;
                case GestureStatus.Running:
                    _viewModel.OnMonitorDragged(rect, startX + e.TotalX, startY + e.TotalY);
                    AbsoluteLayout.SetLayoutBounds(border,
                        new Rect(rect.CanvasX, rect.CanvasY, rect.CanvasWidth, rect.CanvasHeight));
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    // Re-fit on release so a monitor dropped past the edge zooms back into view.
                    // (Only on release — re-fitting mid-drag would cause a zoom/drag feedback loop.)
                    _viewModel.CompleteDrag();
                    break;
            }
        };
        border.GestureRecognizers.Add(pan);

        // Double-click a monitor to flash its name on the matching physical screen.
        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += (_, _) => IdentifyMonitors([rect]);
        border.GestureRecognizers.Add(doubleTap);

        return border;
    }

    // Monitor name, a gold "★ PRIMARY" caption on the primary (the fill alone was hard to read),
    // and an orientation arrow pointing to the display's physical "top" edge so the 0/180 and
    // 90/270 rotations are distinguishable (e.g. ▼ = upside-down).
    static Grid BuildRectContent(MonitorRectViewModel rect)
    {
        var center = new VerticalStackLayout
        {
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        center.Children.Add(new Label
        {
            Text = rect.Label,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold
        });
        if (rect.IsPrimary)
        {
            center.Children.Add(new Label
            {
                Text = "★ PRIMARY",
                HorizontalOptions = LayoutOptions.Center,
                TextColor = Colors.Gold,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold
            });
        }

        var (glyph, horizontal, vertical) = OrientationArrow(rect.Config.Rotation);
        var arrow = new Label
        {
            Text = glyph,
            TextColor = Colors.White,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            Margin = 2,
            HorizontalOptions = horizontal,
            VerticalOptions = vertical
        };

        return new Grid { Padding = 2, Children = { center, arrow } };
    }

    // Arrow glyph + edge placement pointing to where the display's "top" physically sits.
    static (string Glyph, LayoutOptions Horizontal, LayoutOptions Vertical) OrientationArrow(int rotation) => rotation switch
    {
        90 => ("▶", LayoutOptions.End, LayoutOptions.Center),
        180 => ("▼", LayoutOptions.Center, LayoutOptions.End),
        270 => ("◀", LayoutOptions.Start, LayoutOptions.Center),
        _ => ("▲", LayoutOptions.Center, LayoutOptions.Start)
    };

    void RefreshStrokes()
    {
        foreach (var child in CanvasLayout.Children)
        {
            if (child is Border b && b.BindingContext is MonitorRectViewModel rect)
            {
                b.Stroke = rect.IsSelected ? Colors.Orange : Colors.Black;
                b.StrokeThickness = rect.IsSelected ? 3 : 1;
                b.BackgroundColor = rect.IsPrimary ? Colors.RoyalBlue : Colors.DimGray;
            }
        }
    }

    void OnModeSelected(object? sender, EventArgs e)
    {
        if (ModePicker.SelectedItem is DisplayMode mode)
        {
            // ApplyModeToSelection raises LayoutChanged → RebuildRects.
            _viewModel.ApplyModeToSelection(mode);
        }
    }

    // "Identify monitors" button flashes EVERY active live display on its physical screen,
    // independent of the draft. (Double-clicking a single rect identifies just that monitor.)
    void OnIdentifyClicked(object? sender, EventArgs e) => IdentifyAllActiveDisplays();

    void OnAddMonitorClicked(object? sender, EventArgs e)
    {
        if (AddMonitorPicker.SelectedItem is SavedDisplayConfig config)
        {
            _viewModel.AddMonitor(config);
            // Clear the selection so the next pick registers as a change (and the prompt returns).
            AddMonitorPicker.SelectedIndex = -1;
        }
    }

    async void OnResetClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync(
            "Reset layout",
            "Discard all edits and revert to the saved preset? Any stashed draft will be deleted.",
            "Reset", "Cancel");
        if (confirmed)
        {
            await _viewModel.ResetToPresetAsync();
        }
    }

#if WINDOWS
    // Wheel zoom + middle-drag pan: subscribe to the canvas Border's native pointer events once its
    // handler (and thus the WinUI element) exists. These are routed events, so they bubble up from
    // the child monitor rects to the Border — zoom/pan work anywhere over the canvas. The Border's
    // BackgroundColor makes the empty canvas area hit-testable too.
    bool _isPanning;
    double _panLastX, _panLastY;

    void OnCanvasBorderHandlerChanged(object? sender, EventArgs e)
    {
        if (CanvasBorder.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
        {
            element.PointerWheelChanged -= OnCanvasPointerWheelChanged;
            element.PointerWheelChanged += OnCanvasPointerWheelChanged;
            element.PointerPressed -= OnCanvasPointerPressed;
            element.PointerPressed += OnCanvasPointerPressed;
            element.PointerMoved -= OnCanvasPointerMoved;
            element.PointerMoved += OnCanvasPointerMoved;
            element.PointerReleased -= OnCanvasPointerReleased;
            element.PointerReleased += OnCanvasPointerReleased;
            element.PointerCaptureLost -= OnCanvasPointerCaptureLost;
            element.PointerCaptureLost += OnCanvasPointerCaptureLost;
        }
    }

    void OnCanvasPointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint((Microsoft.UI.Xaml.UIElement)sender);
        _viewModel.ZoomBy(point.Properties.MouseWheelDelta, point.Position.X, point.Position.Y);
        e.Handled = true;
    }

    void OnCanvasPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var element = (Microsoft.UI.Xaml.UIElement)sender;
        var point = e.GetCurrentPoint(element);
        // Middle button pans the field — distinct from left-drag, which moves a monitor.
        if (point.Properties.IsMiddleButtonPressed)
        {
            _isPanning = true;
            _panLastX = point.Position.X;
            _panLastY = point.Position.Y;
            element.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    void OnCanvasPointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_isPanning)
        {
            return;
        }
        var point = e.GetCurrentPoint((Microsoft.UI.Xaml.UIElement)sender);
        _viewModel.PanBy(point.Position.X - _panLastX, point.Position.Y - _panLastY);
        _panLastX = point.Position.X;
        _panLastY = point.Position.Y;
        e.Handled = true;
    }

    void OnCanvasPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            ((Microsoft.UI.Xaml.UIElement)sender).ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    void OnCanvasPointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) =>
        _isPanning = false;

    // Identify overlays must be strongly referenced or the GC collects the Window/timer before
    // the close timer fires — leaving the overlay stuck on screen with no way to dismiss it.
    sealed record IdentifyOverlay(
        Microsoft.UI.Xaml.Window Window,
        Microsoft.UI.Dispatching.DispatcherQueueTimer Timer);

    static readonly List<IdentifyOverlay> _identifyOverlays = [];

    static void IdentifyAllActiveDisplays()
    {
        CloseAllOverlays();
        foreach (var display in DisplayManager.Core.DisplayManager.GetAllDisplays()
            .Where(d => d.IsActive && d.Width > 0 && d.Height > 0))
        {
            var label = !string.IsNullOrEmpty(display.MonitorName) ? display.MonitorName
                : !string.IsNullOrEmpty(display.DeviceName) ? display.DeviceName
                : display.EdidSerialNumber;
            ShowIdentifyOverlay(label, display.PositionX, display.PositionY, display.Width, display.Height);
        }
    }

    // Flash a specific draft monitor's name on its matching PHYSICAL screen, using live desktop
    // coordinates (the draft positions are not yet applied). Matched by EDID.
    static void IdentifyMonitors(IReadOnlyList<MonitorRectViewModel> rects)
    {
        CloseAllOverlays();
        var liveDisplays = DisplayManager.Core.DisplayManager.GetAllDisplays();
        foreach (var rect in rects)
        {
            var match = liveDisplays.FirstOrDefault(d => d.IsActive
                && d.EdidManufactureId == rect.Config.EdidManufactureId
                && d.EdidProductCodeId == rect.Config.EdidProductCodeId
                && (string.IsNullOrEmpty(rect.Config.EdidSerialNumber) || d.EdidSerialNumber == rect.Config.EdidSerialNumber));
            if (match is null || match.Width <= 0 || match.Height <= 0)
            {
                continue;
            }
            ShowIdentifyOverlay(rect.Label, match.PositionX, match.PositionY, match.Width, match.Height);
        }
    }

    static void ShowIdentifyOverlay(string label, int screenX, int screenY, int screenWidth, int screenHeight)
    {
        var text = new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = string.IsNullOrEmpty(label) ? "?" : label,
            FontSize = 72,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center
        };
        var root = new Microsoft.UI.Xaml.Controls.Grid
        {
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(230, 0, 120, 215))
        };
        root.Children.Add(text);

        var window = new Microsoft.UI.Xaml.Window { Content = root };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

        var presenter = Microsoft.UI.Windowing.OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        appWindow.SetPresenter(presenter);

        // Keep the transient overlay out of the taskbar and ALT+TAB switcher.
        appWindow.IsShownInSwitchers = false;

        // Centered box on the target monitor (physical pixels, matching CCD coordinates).
        const int boxWidth = 460;
        const int boxHeight = 240;
        var positionX = screenX + ((screenWidth - boxWidth) / 2);
        var positionY = screenY + ((screenHeight - boxHeight) / 2);
        appWindow.MoveAndResize(new Windows.Graphics.RectInt32(positionX, positionY, boxWidth, boxHeight));

        window.Activate();

        var timer = window.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(2.2);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => CloseOverlay(window);
        timer.Start();

        // Click anywhere on the overlay to dismiss it early.
        root.PointerPressed += (_, _) => CloseOverlay(window);

        _identifyOverlays.Add(new IdentifyOverlay(window, timer));
    }

    static void CloseOverlay(Microsoft.UI.Xaml.Window window)
    {
        var index = _identifyOverlays.FindIndex(o => ReferenceEquals(o.Window, window));
        if (index >= 0)
        {
            _identifyOverlays[index].Timer.Stop();
            _identifyOverlays.RemoveAt(index);
        }
        window.Close();
    }

    static void CloseAllOverlays()
    {
        foreach (var overlay in _identifyOverlays.ToList())
        {
            overlay.Timer.Stop();
            overlay.Window.Close();
        }
        _identifyOverlays.Clear();
    }
#else
    static void IdentifyAllActiveDisplays()
    {
        // Identify is only meaningful on Windows (needs live display coordinates).
    }

    static void IdentifyMonitors(IReadOnlyList<MonitorRectViewModel> rects)
    {
        // Identify is only meaningful on Windows (needs live display coordinates).
    }
#endif
}
