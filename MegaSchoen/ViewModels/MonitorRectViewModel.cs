using System.ComponentModel;
using System.Runtime.CompilerServices;
using DisplayManager.Core.Models;

namespace MegaSchoen.ViewModels;

/// <summary>
/// One monitor on the editor canvas. Holds the underlying SavedDisplayConfig plus
/// scaled canvas geometry (CanvasX/Y/Width/Height) derived from the real position/
/// footprint and the canvas scale factor. Footprint respects rotation.
/// </summary>
public class MonitorRectViewModel : INotifyPropertyChanged
{
    double _canvasX, _canvasY, _canvasWidth, _canvasHeight;
    bool _isSelected;

    public MonitorRectViewModel(SavedDisplayConfig config)
    {
        Config = config;
    }

    public SavedDisplayConfig Config { get; }

    public string Label => string.IsNullOrEmpty(Config.MonitorName) ? Config.EdidSerialNumber : Config.MonitorName;

    /// <summary>Footprint width in real pixels (swapped for 90/270 rotation).</summary>
    public int FootprintWidth => Config.Rotation is 90 or 270 ? Config.Height : Config.Width;
    public int FootprintHeight => Config.Rotation is 90 or 270 ? Config.Width : Config.Height;

    public double CanvasX { get => _canvasX; set { _canvasX = value; OnPropertyChanged(); } }
    public double CanvasY { get => _canvasY; set { _canvasY = value; OnPropertyChanged(); } }
    public double CanvasWidth { get => _canvasWidth; set { _canvasWidth = value; OnPropertyChanged(); } }
    public double CanvasHeight { get => _canvasHeight; set { _canvasHeight = value; OnPropertyChanged(); } }

    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public bool IsPrimary => Config.IsPrimary;

    public void RaisePrimaryChanged() => OnPropertyChanged(nameof(IsPrimary));
    public void RaiseFootprintChanged()
    {
        OnPropertyChanged(nameof(FootprintWidth));
        OnPropertyChanged(nameof(FootprintHeight));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
