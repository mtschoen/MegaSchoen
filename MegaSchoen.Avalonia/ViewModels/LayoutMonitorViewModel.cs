using System.ComponentModel;
using System.Runtime.CompilerServices;
using DisplayManager.Core.Models;

namespace MegaSchoen.Avalonia.ViewModels;

public sealed class LayoutMonitorViewModel : INotifyPropertyChanged
{
    double canvasX;
    double canvasY;
    double canvasWidth;
    double canvasHeight;
    bool isSelected;

    public LayoutMonitorViewModel(SavedDisplayConfig config)
    {
        Config = config;
    }

    public SavedDisplayConfig Config { get; }
    public string Label => string.IsNullOrEmpty(Config.MonitorName)
        ? Config.EdidSerialNumber
        : Config.MonitorName;
    public int FootprintWidth => Config.Rotation is 90 or 270
        ? Config.Height
        : Config.Width;
    public int FootprintHeight => Config.Rotation is 90 or 270
        ? Config.Width
        : Config.Height;
    public bool IsPrimary => Config.IsPrimary;

    public double CanvasX
    {
        get => canvasX;
        set
        {
            canvasX = value;
            OnPropertyChanged();
        }
    }

    public double CanvasY
    {
        get => canvasY;
        set
        {
            canvasY = value;
            OnPropertyChanged();
        }
    }

    public double CanvasWidth
    {
        get => canvasWidth;
        set
        {
            canvasWidth = value;
            OnPropertyChanged();
        }
    }

    public double CanvasHeight
    {
        get => canvasHeight;
        set
        {
            canvasHeight = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            isSelected = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
