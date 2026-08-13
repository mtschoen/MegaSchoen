using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Claude.Core;
using Claude.Core.Models;
using Claude.Core.Remote;

namespace MegaSchoen.Avalonia.ViewModels;

// Avalonia port of MegaSchoen/ViewModels/SessionsPageViewModel.cs (the MAUI
// original is #if WINDOWS). Same event-driven design: two FileSystemWatchers
// funnel into a bounded Channel consumed by SessionRefreshLoop (250ms debounce),
// plus NDJSON remote-host streams merged in. MAUI's IDispatcher becomes
// Avalonia's Dispatcher.UIThread and the clipboard is an injected delegate
// (Avalonia's clipboard hangs off the TopLevel, which the view owns).
public sealed class SessionsViewModel : INotifyPropertyChanged, IDisposable
{
    readonly ActiveSessionEnumerator _enumerator;
    readonly ISshSessionWindowResolver _sshWindowResolver;

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
    IReadOnlyList<SessionSnapshot> _localSnapshots = Array.Empty<SessionSnapshot>();

    public ObservableCollection<SessionCardViewModel> Sessions { get; } = new();
    public ObservableCollection<HostStatusViewModel> HostStatuses { get; } = new();
    public ICommand FocusCommand { get; }
    public ICommand CopyTranscriptPathCommand { get; }
    public ICommand RefreshCommand { get; }

    bool _hasNoSessions = true;
    public bool HasNoSessions
    {
        get => _hasNoSessions;
        private set
        {
            if (_hasNoSessions == value) return;
            _hasNoSessions = value;
            OnPropertyChanged();
        }
    }

    public SessionsViewModel(
        ActiveSessionEnumerator enumerator,
        IClaudeWindowFocuser focuser,
        ISshSessionWindowResolver sshWindowResolver,
        Func<string, Task> setClipboardText)
    {
        _enumerator = enumerator;
        _sshWindowResolver = sshWindowResolver;

        FocusCommand = new RelayCommand(parameter =>
        {
            if (parameter is not SessionCardViewModel card || card.Snapshot.Window.IsZero) return;
            focuser.BringToFront(card.Snapshot.Window);
        });
        CopyTranscriptPathCommand = new RelayCommand(parameter => RunCommand(async () =>
        {
            if (parameter is not SessionCardViewModel card) return;
            var path = card.Snapshot.TranscriptPath;
            if (string.IsNullOrEmpty(path)) return;
            await setClipboardText(path);
        }));
        RefreshCommand = new RelayCommand(_ => RefreshNow());
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
        _stateWatcher.Renamed += (_, _) => _refreshSignal.Writer.TryWrite(0);

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

        var loop = new SessionRefreshLoop(_refreshSignal.Reader, RefreshDispatchedAsync);
        _consumerTask = Task.Run(() => loop.RunAsync(_cts.Token));
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
                Dispatcher.UIThread.Post(() => MergeRemote(capturedHost.Name, snapshots));
            client.ConnectionStateChanged += state =>
                Dispatcher.UIThread.Post(() => status.State = state);
            _ = client.RunAsync(_cts.Token);
        }
    }

    void OnAnyEvent(object? sender, FileSystemEventArgs eventArguments) => _refreshSignal.Writer.TryWrite(0);

    // The per-tick refresh body driven by SessionRefreshLoop. Enumeration runs on
    // the loop's background thread; the view mutation is marshalled to the UI
    // thread. SessionRefreshLoop guards this against per-iteration faults.
    Task RefreshDispatchedAsync(CancellationToken cancellationToken)
    {
        var snapshots = _enumerator.Enumerate();
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            _localSnapshots = snapshots;
            RebuildMergedView();
        }).GetTask();
    }

    public void RefreshNow()
    {
        // Initial load + Refresh button path. Guarded for the same reason the
        // loop is (issue #28): a transient enumeration fault must not bubble out
        // and crash the window or leave the button dead.
        try
        {
            _localSnapshots = _enumerator.Enumerate();
            RebuildMergedView();
        }
        catch (Exception exception)
        {
            Logger.Log($"SessionsViewModel.RefreshNow failed: {exception}");
        }
    }

    void MergeRemote(string host, IReadOnlyList<SessionSnapshot> snapshots)
    {
        _remoteByHost[host] = snapshots.Select(EnrichRemoteWindow).ToList();
        RebuildMergedView();
    }

    // For a remote session that reported an ssh client port, find the local
    // terminal window hosting it and stamp it onto the snapshot so Focus works.
    // (No-op through NullSshSessionWindowResolver on hosts without window glue.)
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

            // Search from i onward only: items at [0, i) are already finalized,
            // so the match (if any) is at an index >= i. Searching from 0 could
            // return a stale duplicate at an index < i, making the Move below
            // remove-then-insert past the shortened list's end and throw.
            var existingIndex = -1;
            for (var j = i; j < Sessions.Count; j++)
            {
                if (Key(Sessions[j].Snapshot) == key)
                {
                    existingIndex = j;
                    break;
                }
            }

            if (existingIndex < 0)
            {
                Sessions.Insert(i, new SessionCardViewModel(merged[i]));
            }
            else
            {
                Sessions[existingIndex].Snapshot = merged[i];
                if (existingIndex != i)
                {
                    Sessions.Move(existingIndex, i);
                }
            }
        }

        // Drop any leftover cards past the merged set (including duplicates a
        // prior crashed rebuild may have left in the collection).
        while (Sessions.Count > merged.Count)
        {
            Sessions.RemoveAt(Sessions.Count - 1);
        }

        HasNoSessions = Sessions.Count == 0;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _stateWatcher?.Dispose();
        _transcriptsWatcher?.Dispose();
        _consumerTask?.Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
        HostStatuses.Clear();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Fire-and-forget bridge for ICommand: RelayCommand takes a void-returning
    // Action, so an `async () => ...` body would be an async void lambda whose
    // exceptions are lost. Route async command bodies through here so a failure
    // surfaces in the log instead of being swallowed by the dispatcher.
    static void RunCommand(Func<Task> operation) => _ = RunAndLogAsync(operation);

    static async Task RunAndLogAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            Logger.Log($"SessionsViewModel command failed: {exception}");
        }
    }
}
