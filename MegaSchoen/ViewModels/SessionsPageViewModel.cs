#if WINDOWS
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Channels;
using System.Windows.Input;
using Claude.Core;
using Claude.Core.Models;

namespace MegaSchoen.ViewModels;

public sealed class SessionsPageViewModel : INotifyPropertyChanged, IDisposable
{
    readonly ActiveSessionEnumerator _enumerator;
    readonly IClaudeWindowFocuser _focuser;
    readonly IDispatcher _dispatcher;

    readonly Channel<byte> _refreshSignal =
        Channel.CreateBounded<byte>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite
            });
    readonly CancellationTokenSource _cts = new();
    FileSystemWatcher? _stateWatcher;
    FileSystemWatcher? _transcriptsWatcher;
    Task? _consumerTask;

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

    public void Start()
    {
        var stateFile = Paths.NeedySessionsFile;
        var stateDir = Path.GetDirectoryName(stateFile);
        if (!string.IsNullOrEmpty(stateDir))
        {
            Directory.CreateDirectory(stateDir);
            _stateWatcher = new FileSystemWatcher(stateDir, Path.GetFileName(stateFile))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _stateWatcher.Changed += OnAnyEvent;
            _stateWatcher.Created += OnAnyEvent;
        }

        var projectsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        if (Directory.Exists(projectsRoot))
        {
            _transcriptsWatcher = new FileSystemWatcher(projectsRoot, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _transcriptsWatcher.Changed += OnAnyEvent;
            _transcriptsWatcher.Created += OnAnyEvent;
        }

        _consumerTask = Task.Run(() => ConsumeAsync(_cts.Token));
        RefreshNow(); // initial load
    }

    void OnAnyEvent(object? sender, FileSystemEventArgs eventArguments) => _refreshSignal.Writer.TryWrite(0);

    async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in _refreshSignal.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
                while (_refreshSignal.Reader.TryRead(out var __)) { }
                var snapshots = _enumerator.Enumerate();
                await _dispatcher.DispatchAsync(() => UpdateUi(snapshots)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception exception)
        {
            Logger.Log($"SessionsPageViewModel.ConsumeAsync threw: {exception}");
        }
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
        _cts.Cancel();
        _stateWatcher?.Dispose();
        _transcriptsWatcher?.Dispose();
        _consumerTask?.Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
#endif
