using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Claude.Core.Remote;

namespace MegaSchoen.Avalonia.ViewModels;

// Avalonia port of MegaSchoen/ViewModels/HostStatusViewModel.cs.
public sealed class HostStatusViewModel(string host) : INotifyPropertyChanged
{
    static readonly IBrush GreenBrush = Brush.Parse("#5CB85C");
    static readonly IBrush AmberBrush = Brush.Parse("#F0AD4E");
    static readonly IBrush RedBrush = Brush.Parse("#D9534F");
    static readonly IBrush FallbackBrush = Brush.Parse("#999999");

    RemoteConnectionState _state = RemoteConnectionState.Connecting;

    public string Host { get; } = host;

    public RemoteConnectionState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
        }
    }

    public string StatusText => _state switch
    {
        RemoteConnectionState.Connected => "connected",
        RemoteConnectionState.Connecting => "connecting…",
        RemoteConnectionState.Disconnected => "disconnected",
        _ => ""
    };

    public IBrush StatusBrush => _state switch
    {
        RemoteConnectionState.Connected => GreenBrush,
        RemoteConnectionState.Connecting => AmberBrush,
        RemoteConnectionState.Disconnected => RedBrush,
        _ => FallbackBrush
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
