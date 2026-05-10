#if WINDOWS
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Claude.Core;
using Claude.Core.Models;

namespace MegaSchoen.ViewModels;

public sealed class SessionsPageViewModel : INotifyPropertyChanged, IDisposable
{
    readonly ActiveSessionEnumerator _enumerator;
    readonly IClaudeWindowFocuser _focuser;
    readonly IDispatcher _dispatcher;

    public ObservableCollection<SessionCardViewModel> Sessions { get; } = new();
    public ICommand FocusCommand { get; }
    public ICommand ToggleExpandCommand { get; }

    public SessionsPageViewModel(
        ActiveSessionEnumerator enumerator,
        IClaudeWindowFocuser focuser,
        IDispatcher dispatcher)
    {
        _enumerator = enumerator;
        _focuser = focuser;
        _dispatcher = dispatcher;

        FocusCommand = new Command<SessionCardViewModel>(card =>
            _focuser.BringToFront(card.Snapshot.Window));
        ToggleExpandCommand = new Command<SessionCardViewModel>(card =>
            card.IsExpanded = !card.IsExpanded);
    }

    public void RefreshNow()
    {
        var snapshots = _enumerator.Enumerate();
        UpdateUi(snapshots);
    }

    void UpdateUi(IReadOnlyList<SessionSnapshot> snapshots)
    {
        var keepIds = new HashSet<string>(snapshots.Select(s => s.SessionId));
        for (var i = Sessions.Count - 1; i >= 0; i--)
        {
            if (!keepIds.Contains(Sessions[i].Snapshot.SessionId))
            {
                Sessions.RemoveAt(i);
            }
        }

        for (var i = 0; i < snapshots.Count; i++)
        {
            var existing = Sessions.FirstOrDefault(c => c.Snapshot.SessionId == snapshots[i].SessionId);
            if (existing is null)
            {
                Sessions.Insert(i, new SessionCardViewModel(snapshots[i]));
            }
            else
            {
                existing.Snapshot = snapshots[i];
                var currentIndex = Sessions.IndexOf(existing);
                if (currentIndex != i)
                {
                    Sessions.Move(currentIndex, i);
                }
            }
        }
    }

    public void Dispose()
    {
        // Watchers added in next task; cancellation belongs there.
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
#endif
