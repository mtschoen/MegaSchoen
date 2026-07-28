#if WINDOWS
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Claude.Core;
using Claude.Core.Models;

namespace MegaSchoen.ViewModels;

public sealed partial class SessionCardViewModel : INotifyPropertyChanged
{
    SessionSnapshot _snapshot;
    bool _isExpanded;

    public SessionCardViewModel(SessionSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public SessionSnapshot Snapshot
    {
        get => _snapshot;
        set
        {
            _snapshot = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(HasTitle));
            OnPropertyChanged(nameof(HasTranscriptPath));
            OnPropertyChanged(nameof(StateEmoji));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(StateColor));
            OnPropertyChanged(nameof(ModeText));
            OnPropertyChanged(nameof(ModeColor));
            OnPropertyChanged(nameof(CwdShort));
            OnPropertyChanged(nameof(CurrentCwdShort));
            OnPropertyChanged(nameof(SessionIdStem));
            OnPropertyChanged(nameof(LastActivityRelative));
            OnPropertyChanged(nameof(SubagentSummary));
            OnPropertyChanged(nameof(IsRemote));
            OnPropertyChanged(nameof(FocusButtonVisible));
            OnPropertyChanged(nameof(CanFocus));
            OnPropertyChanged(nameof(HostLabel));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public string Title => _snapshot.Title ?? "";

    public bool HasTitle => !string.IsNullOrEmpty(_snapshot.Title);

    public bool HasTranscriptPath => !string.IsNullOrEmpty(_snapshot.TranscriptPath);

    public string StateEmoji => SessionStateEmoji.For(_snapshot.RollupState);

    public string StateText => _snapshot.RollupState.ToString();

    public string StateColor => _snapshot.RollupState switch
    {
        SessionState.PendingPermission => "#D9534F",
        SessionState.AwaitingInput => "#F0AD4E",
        SessionState.Working => "#5CB85C",
        SessionState.Idle => "#777777",
        SessionState.Wrapped => "#5BC0DE",
        _ => "#999999"
    };

    public string ModeText => _snapshot.Mode.ToString();

    public string ModeColor => _snapshot.Mode switch
    {
        SessionMode.Plan => "#3B82F6",
        SessionMode.Build => "#5CB85C",
        SessionMode.Auto => "#A855F7",
        _ => "#777777"
    };

    public string CwdShort
    {
        get => TruncatePath(_snapshot.Cwd);
    }

    public string CurrentCwdShort => TruncatePath(_snapshot.CurrentCwd);

    public string SessionIdStem => _snapshot.SessionId.Length >= 8 ? _snapshot.SessionId[..8] : _snapshot.SessionId;

    public string LastActivityRelative
    {
        get
        {
            var delta = DateTimeOffset.UtcNow - _snapshot.LastActivityUtc;
            return delta.TotalSeconds < 60
                ? $"{(int)delta.TotalSeconds}s ago"
                : delta.TotalMinutes < 60
                    ? $"{(int)delta.TotalMinutes}m ago"
                    : $"{(int)delta.TotalHours}h ago";
        }
    }

    public string SubagentSummary => _snapshot.Subagents.Count == 0
        ? ""
        : $"{_snapshot.Subagents.Count} subagent{(_snapshot.Subagents.Count == 1 ? "" : "s")}";

    public IReadOnlyList<SubagentSnapshot> Subagents => _snapshot.Subagents;

    public bool IsRemote => _snapshot.Host is not null;
    // Local sessions always show the button (greyed when windowless). Remote
    // sessions show it ONLY once a hosting ssh/cmd window was resolved.
    public bool FocusButtonVisible => !IsRemote || !_snapshot.Window.IsZero;
    // Enabled whenever a window is attached - local terminal OR resolved remote ssh.
    public bool CanFocus => !_snapshot.Window.IsZero;
    public string HostLabel => _snapshot.Host ?? "";

    public event PropertyChangedEventHandler? PropertyChanged;

    static string TruncatePath(string path)
    {
        const int maximum = 60;
        if (path.Length <= maximum) return path;
        var keep = (maximum - 3) / 2;
        return path[..keep] + "..." + path[^keep..];
    }

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
#endif
