using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Claude.Core;
using Claude.Core.Models;

namespace MegaSchoen.Avalonia.ViewModels;

// Avalonia port of MegaSchoen/ViewModels/SessionCardViewModel.cs (the MAUI
// original is #if WINDOWS). Differences: colors surface as IBrush (Avalonia
// bindings don't coerce hex strings), and the string-emptiness converters are
// replaced by explicit Has* booleans.
public sealed class SessionCardViewModel : INotifyPropertyChanged
{
    public SessionCardViewModel(SessionSnapshot snapshot) => _snapshot = snapshot;

    static readonly IBrush GrayBrush = Brush.Parse("#777777");
    static readonly IBrush RedBrush = Brush.Parse("#D9534F");
    static readonly IBrush AmberBrush = Brush.Parse("#F0AD4E");
    static readonly IBrush GreenBrush = Brush.Parse("#5CB85C");
    static readonly IBrush FallbackBrush = Brush.Parse("#999999");

    static readonly string[] SnapshotDerivedProperties =
    [
        nameof(Snapshot), nameof(Title), nameof(HasTitle), nameof(HasTranscriptPath),
        nameof(StateEmoji), nameof(StateText), nameof(StateBrush), nameof(CwdShort),
        nameof(SessionIdStem), nameof(LastActivityRelative), nameof(SubagentSummary),
        nameof(HasSubagents), nameof(IsRemote), nameof(FocusButtonVisible),
        nameof(CanFocus), nameof(HostLabel), nameof(HasHostLabel)
    ];

    SessionSnapshot _snapshot;
    bool _isExpanded;

    public SessionSnapshot Snapshot
    {
        get => _snapshot;
        set
        {
            _snapshot = value;
            foreach (var name in SnapshotDerivedProperties)
            {
                OnPropertyChanged(name);
            }
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

    public IBrush StateBrush => _snapshot.RollupState switch
    {
        SessionState.PendingPermission => RedBrush,
        SessionState.AwaitingInput => AmberBrush,
        SessionState.Working => GreenBrush,
        SessionState.Idle => GrayBrush,
        _ => FallbackBrush
    };

    public string CwdShort
    {
        get
        {
            const int maximum = 60;
            if (_snapshot.Cwd.Length <= maximum) return _snapshot.Cwd;
            var keep = (maximum - 3) / 2;
            return _snapshot.Cwd[..keep] + "..." + _snapshot.Cwd[^keep..];
        }
    }

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

    public bool HasSubagents => _snapshot.Subagents.Count > 0;

    public IReadOnlyList<SubagentSnapshot> Subagents => _snapshot.Subagents;

    public bool IsRemote => _snapshot.Host is not null;

    // Local sessions always show the button (greyed when windowless). Remote
    // sessions show it ONLY once a hosting terminal window was resolved.
    public bool FocusButtonVisible => !IsRemote || !_snapshot.Window.IsZero;

    public bool CanFocus => !_snapshot.Window.IsZero;

    public string HostLabel => _snapshot.Host ?? "";

    public bool HasHostLabel => _snapshot.Host is not null;

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
