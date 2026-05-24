#if WINDOWS
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Claude.Core.Remote;

namespace MegaSchoen.ViewModels;

public sealed class HostStatusViewModel : INotifyPropertyChanged
{
    RemoteConnectionState _state = RemoteConnectionState.Connecting;

    public HostStatusViewModel(string host) => Host = host;

    public string Host { get; }

    public RemoteConnectionState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusColor));
        }
    }

    public string StatusText => _state switch
    {
        RemoteConnectionState.Connected => "connected",
        RemoteConnectionState.Connecting => "connecting…",
        RemoteConnectionState.Disconnected => "disconnected",
        _ => ""
    };

    public string StatusColor => _state switch
    {
        RemoteConnectionState.Connected => "#5CB85C",
        RemoteConnectionState.Connecting => "#F0AD4E",
        RemoteConnectionState.Disconnected => "#D9534F",
        _ => "#999999"
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
#endif
