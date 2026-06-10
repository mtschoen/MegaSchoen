#if WINDOWS
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Channels;
using System.Windows.Input;
using Claude.Core;
using Claude.Core.Models;
using Claude.Core.Remote;

namespace MegaSchoen.ViewModels;

public sealed class SessionsPageViewModel : IDisposable
{
    readonly ActiveSessionEnumerator _enumerator;
    readonly IClaudeWindowFocuser _focuser;
    readonly ISshSessionWindowResolver _sshWindowResolver;
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

    readonly Dictionary<string, IReadOnlyList<SessionSnapshot>> _remoteByHost = new();
    readonly List<RemoteSessionStreamClient> _remoteClients = new();
    IReadOnlyList<SessionSnapshot> _localSnapshots = Array.Empty<SessionSnapshot>();

    public ObservableCollection<SessionCardViewModel> Sessions { get; } = new();
    public ObservableCollection<HostStatusViewModel> HostStatuses { get; } = new();
    public ICommand FocusCommand { get; }
    public ICommand ToggleExpandCommand { get; }
    public ICommand RefreshCommand { get; }

    public SessionsPageViewModel(
        ActiveSessionEnumerator enumerator,
        IClaudeWindowFocuser focuser,
        ISshSessionWindowResolver sshWindowResolver,
        IDispatcher dispatcher)
    {
        _enumerator = enumerator;
        _focuser = focuser;
        _sshWindowResolver = sshWindowResolver;
        _dispatcher = dispatcher;

        FocusCommand = new Command<SessionCardViewModel>(card =>
        {
            if (card.Snapshot.Window.IsZero) return;
            _focuser.BringToFront(card.Snapshot.Window);
        });
        ToggleExpandCommand = new Command<SessionCardViewModel>(card =>
            card.IsExpanded = !card.IsExpanded);
        RefreshCommand = new Command(RefreshNow);
    }

    public void Start()
    {
        if (_consumerTask is not null) return;

        Paths.EnsureNeedySessionsDirectoryExists();
        _stateWatcher = new FileSystemWatcher(Paths.NeedySessionsDirectory, "*.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _stateWatcher.Changed += OnAnyEvent;
        _stateWatcher.Created += OnAnyEvent;
        _stateWatcher.Deleted += OnAnyEvent;
        _stateWatcher.Renamed += (s, e) => _refreshSignal.Writer.TryWrite(0);

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

        foreach (var host in RemoteHostConfig.Load())
        {
            var capturedHost = host;
            var status = new HostStatusViewModel(capturedHost.Name);
            HostStatuses.Add(status);
            var client = new RemoteSessionStreamClient(
                capturedHost.Name,
                () => new SshStreamProcess(capturedHost.SshTarget, capturedHost.RemoteCli));
            client.SnapshotReceived += snapshots =>
                _dispatcher.Dispatch(() => MergeRemote(capturedHost.Name, snapshots));
            client.ConnectionStateChanged += state =>
                _dispatcher.Dispatch(() => status.State = state);
            _remoteClients.Add(client);
            _ = client.RunAsync(_cts.Token);
        }
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
                await _dispatcher.DispatchAsync(() =>
                {
                    _localSnapshots = snapshots;
                    RebuildMergedView();
                }).ConfigureAwait(false);
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
        _localSnapshots = _enumerator.Enumerate();
        RebuildMergedView();
    }

    void MergeRemote(string host, IReadOnlyList<SessionSnapshot> snapshots)
    {
        _remoteByHost[host] = snapshots.Select(EnrichRemoteWindow).ToList();
        RebuildMergedView();
    }

    // For a remote session that reported an ssh client port, find the local
    // ssh/cmd window hosting it and stamp it onto the snapshot so Focus works.
    SessionSnapshot EnrichRemoteWindow(SessionSnapshot snapshot)
    {
        if (snapshot.SshClientPort is not { } port) return snapshot;
        return _sshWindowResolver.ResolveWindow(port) is { } resolved
            ? snapshot with { Window = resolved.Window, WindowTitle = resolved.Title }
            : snapshot;
    }

    void RebuildMergedView()
    {
        var merged = _localSnapshots
            .Concat(_remoteByHost.Values.SelectMany(list => list))
            .OrderBy(s => (int)s.RollupState)
            .ThenByDescending(s => s.LastActivityUtc)
            .ToList();

        static string Key(SessionSnapshot s) => $"{s.Host ?? "local"} {s.SessionId}";

        var keep = new HashSet<string>(merged.Select(Key));
        for (var i = Sessions.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Key(Sessions[i].Snapshot)))
            {
                Sessions.RemoveAt(i);
            }
        }

        for (var i = 0; i < merged.Count; i++)
        {
            var key = Key(merged[i]);
            var existing = Sessions.FirstOrDefault(c => Key(c.Snapshot) == key);
            if (existing is null)
            {
                Sessions.Insert(i, new SessionCardViewModel(merged[i]));
            }
            else
            {
                existing.Snapshot = merged[i];
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
        _remoteClients.Clear();
        HostStatuses.Clear();
    }
}
#endif
