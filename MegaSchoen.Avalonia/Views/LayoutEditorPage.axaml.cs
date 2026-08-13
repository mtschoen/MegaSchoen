using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MegaSchoen.Avalonia.ViewModels;

namespace MegaSchoen.Avalonia.Views;

public partial class LayoutEditorPage : UserControl
{
    LayoutEditorViewModel? viewModel;
    bool stashRestored;

    public LayoutEditorPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        LayoutCanvas.SizeChanged += OnCanvasSizeChanged;
        AttachedToVisualTree += (_, _) => RestoreStash();
    }

    public LayoutEditorPage(LayoutEditorViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    void OnDataContextChanged(object? sender, EventArgs eventArguments)
    {
        if (viewModel is not null)
        {
            viewModel.Monitors.CollectionChanged -= OnMonitorsChanged;
            viewModel.LayoutChanged -= RebuildMonitorRects;
        }

        viewModel = DataContext as LayoutEditorViewModel;
        stashRestored = false;
        if (viewModel is not null)
        {
            viewModel.Monitors.CollectionChanged += OnMonitorsChanged;
            viewModel.LayoutChanged += RebuildMonitorRects;
        }
        RestoreStash();
        RebuildMonitorRects();
    }

    void RestoreStash()
    {
        if (viewModel is null || stashRestored)
        {
            return;
        }
        stashRestored = true;
        _ = viewModel.RestoreStashAsync();
    }

    void OnMonitorsChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArguments) =>
        RebuildMonitorRects();

    void OnCanvasSizeChanged(object? sender, SizeChangedEventArgs eventArguments)
    {
        if (viewModel is null
            || LayoutCanvas.Bounds.Width <= 0
            || LayoutCanvas.Bounds.Height <= 0)
        {
            return;
        }
        viewModel.SetViewport(
            LayoutCanvas.Bounds.Width,
            LayoutCanvas.Bounds.Height);
        RebuildMonitorRects();
    }

    void RebuildMonitorRects()
    {
        LayoutCanvas.Children.Clear();
        if (viewModel is null)
        {
            return;
        }

        foreach (var monitor in viewModel.Monitors)
        {
            LayoutCanvas.Children.Add(BuildMonitorRect(viewModel, monitor));
        }
    }

    Border BuildMonitorRect(
        LayoutEditorViewModel owner,
        LayoutMonitorViewModel monitor)
    {
        var border = new Border
        {
            DataContext = monitor,
            Width = monitor.CanvasWidth,
            Height = monitor.CanvasHeight,
            MinWidth = 28,
            MinHeight = 20,
            Background = monitor.IsPrimary ? Brushes.RoyalBlue : Brushes.DimGray,
            BorderBrush = monitor.IsSelected ? Brushes.Orange : Brushes.Black,
            BorderThickness = new Thickness(monitor.IsSelected ? 3 : 1),
            CornerRadius = new CornerRadius(3),
            Child = BuildMonitorContent(monitor)
        };
        Canvas.SetLeft(border, monitor.CanvasX);
        Canvas.SetTop(border, monitor.CanvasY);

        var dragging = false;
        var startPointer = default(Point);
        var startCanvas = default(Point);
        border.PointerPressed += (_, eventArguments) =>
        {
            var point = eventArguments.GetCurrentPoint(LayoutCanvas);
            if (!point.Properties.IsLeftButtonPressed)
            {
                return;
            }
            owner.Selected = monitor;
            RefreshMonitorStyles();
            dragging = true;
            startPointer = point.Position;
            startCanvas = new Point(monitor.CanvasX, monitor.CanvasY);
            eventArguments.Pointer.Capture(border);
            eventArguments.Handled = true;
        };
        border.PointerMoved += (_, eventArguments) =>
        {
            if (!dragging)
            {
                return;
            }
            var position = eventArguments.GetPosition(LayoutCanvas);
            owner.DragMonitor(
                monitor,
                startCanvas.X + position.X - startPointer.X,
                startCanvas.Y + position.Y - startPointer.Y);
            Canvas.SetLeft(border, monitor.CanvasX);
            Canvas.SetTop(border, monitor.CanvasY);
            eventArguments.Handled = true;
        };
        border.PointerReleased += (_, eventArguments) =>
        {
            if (!dragging)
            {
                return;
            }
            dragging = false;
            eventArguments.Pointer.Capture(null);
            owner.CompleteDrag();
            eventArguments.Handled = true;
        };
        border.PointerCaptureLost += (_, _) =>
        {
            if (dragging)
            {
                dragging = false;
                owner.CompleteDrag();
            }
        };
        return border;
    }

    void RefreshMonitorStyles()
    {
        foreach (var child in LayoutCanvas.Children)
        {
            if (child is not Border { DataContext: LayoutMonitorViewModel monitor } border)
            {
                continue;
            }
            border.BorderBrush = monitor.IsSelected ? Brushes.Orange : Brushes.Black;
            border.BorderThickness = new Thickness(monitor.IsSelected ? 3 : 1);
        }
    }

    static Grid BuildMonitorContent(LayoutMonitorViewModel monitor)
    {
        var content = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new TextBlock
        {
            Text = monitor.Label,
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold,
            TextAlignment = TextAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = Math.Max(20, monitor.CanvasWidth - 8)
        });
        if (monitor.IsPrimary)
        {
            content.Children.Add(new TextBlock
            {
                Text = "★ PRIMARY",
                Foreground = Brushes.Gold,
                FontSize = 11,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
        return new Grid
        {
            Margin = new Thickness(4),
            Children = { content }
        };
    }
}
