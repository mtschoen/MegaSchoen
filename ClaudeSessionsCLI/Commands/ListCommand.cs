using System.Text.Json;
using Claude.Core;
using Claude.Core.Models;
#if WINDOWS
using Claude.Core.Windows;
#endif
using Spectre.Console;

namespace ClaudeSessionsCLI.Commands;

static class ListCommand
{
    // Cached per CA1869: a JsonSerializerOptions instance is reusable and thread-safe
    // once configured, so it should not be reallocated on every serialize call.
    static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public static async Task<int> Run(string[] arguments)
    {
        var options = CliOptions.Parse(arguments);
        var enumerator = BuildEnumerator();

        if (options.Json)
        {
            return EmitJsonOnce(enumerator);
        }
        if (options.JsonStream)
        {
            return await EmitJsonStream(enumerator, options).ConfigureAwait(false);
        }
        return await RunHumanMode(enumerator, options).ConfigureAwait(false);
    }

    static ActiveSessionEnumerator BuildEnumerator()
    {
        // Replay/test seams (no-ops when the env vars are unset):
        //   MEGASCHOEN_FAKE_PROCESSES → run with no real claude.exe (windowless procs)
        //   MEGASCHOEN_STATE_DIR      → isolate the needy-sessions state directory
        IClaudeProcessLocator locator;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentProcessLocator.EnvironmentVariable)))
        {
            locator = new EnvironmentProcessLocator();
        }
        else
        {
#if WINDOWS
            locator = new WindowsClaudeProcessLocator();
#else
            locator = new Claude.Core.Linux.LinuxClaudeProcessLocator();
#endif
        }

        var stateDir = Environment.GetEnvironmentVariable("MEGASCHOEN_STATE_DIR");
        var store = string.IsNullOrWhiteSpace(stateDir) ? new StateStore() : new StateStore(stateDir);
        return new ActiveSessionEnumerator(locator, store);
    }

    static int EmitJsonOnce(ActiveSessionEnumerator enumerator)
    {
        var snapshots = enumerator.Enumerate();
        var json = JsonSerializer.Serialize(
            snapshots.Select(SnapshotDto.From).ToArray(),
            IndentedJson);
        Console.Out.Write(json);
        Console.Out.Write('\n');
        return 0;
    }

    static async Task<int> EmitJsonStream(ActiveSessionEnumerator enumerator, CliOptions options)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var json = new JsonSerializerOptions { WriteIndented = false };
        var ct = cts.Token;

        while (!ct.IsCancellationRequested)
        {
            var snapshots = enumerator.Enumerate();
            var serialized = JsonSerializer.Serialize(
                snapshots.Select(SnapshotDto.From).ToArray(),
                json);
            await Console.Out.WriteLineAsync(serialized.AsMemory(), ct).ConfigureAwait(false);
            await Console.Out.FlushAsync(ct).ConfigureAwait(false);

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        return 0;
    }

    static async Task<int> RunHumanMode(ActiveSessionEnumerator enumerator, CliOptions options)
    {
        // Pipe-aware: if stdout is redirected, render once and exit (NOT ANSI live).
        if (Console.IsOutputRedirected)
        {
            var snapshots = enumerator.Enumerate();
            var json = JsonSerializer.Serialize(
                snapshots.Select(SnapshotDto.From).ToArray(),
                IndentedJson);
            Console.Out.Write(json);
            Console.Out.Write('\n');
            return 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        var ct = cts.Token;

        var table = BuildTable(Array.Empty<SessionSnapshot>());

        await Spectre.Console.AnsiConsole.Live(table)
            .StartAsync(async live =>
            {
                while (!ct.IsCancellationRequested)
                {
                    var snapshots = enumerator.Enumerate();
                    Repopulate(table, snapshots);
                    live.Refresh();
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }).ConfigureAwait(false);

        Spectre.Console.AnsiConsole.WriteLine("Stopped.");
        return 0;
    }

    static Spectre.Console.Table BuildTable(IReadOnlyList<SessionSnapshot> snapshots)
    {
        var table = new Spectre.Console.Table();
        table.AddColumns("State", "Cwd", "Session", "Last activity");
        Repopulate(table, snapshots);
        return table;
    }

    static void Repopulate(Spectre.Console.Table table, IReadOnlyList<SessionSnapshot> snapshots)
    {
        table.Rows.Clear();
        if (snapshots.Count == 0)
        {
            table.AddRow("[grey](no active sessions)[/]", "", "", "");
            return;
        }
        foreach (var s in snapshots)
        {
            table.AddRow(
                FormatState(s.RollupState),
                Spectre.Console.Markup.Escape(TruncateMiddle(s.Cwd, 50)),
                s.SessionId.Length >= 8 ? s.SessionId[..8] : s.SessionId,
                $"{s.LastActivityUtc.ToLocalTime():HH:mm:ss}");
        }
    }

    static string FormatState(SessionState state) => state switch
    {
        SessionState.PendingPermission => "[red]PERM[/]",
        SessionState.AwaitingInput => "[yellow]INPUT[/]",
        SessionState.Working => "[green]WORK[/]",
        SessionState.Idle => "[grey]idle[/]",
        _ => "[red]?[/]"
    };

    static string TruncateMiddle(string s, int maximum)
    {
        if (s.Length <= maximum) return s;
        var keep = (maximum - 3) / 2;
        return s[..keep] + "..." + s[^keep..];
    }
}

sealed record SnapshotDto(
    string SessionId,
    string Cwd,
    string TranscriptPath,
    DateTimeOffset LastActivityUtc,
    string State,
    string RollupState,
    string? PendingMessage,
    string? WindowTitle,
    SubagentDto[] Subagents)
{
    public static SnapshotDto From(SessionSnapshot snapshot) => new(
        snapshot.SessionId,
        snapshot.Cwd,
        snapshot.TranscriptPath,
        snapshot.LastActivityUtc,
        snapshot.State.ToString(),
        snapshot.RollupState.ToString(),
        snapshot.PendingMessage,
        snapshot.WindowTitle,
        snapshot.Subagents.Select(s => new SubagentDto(s.AgentId, s.LastActivityUtc, s.State.ToString())).ToArray());
}

sealed record SubagentDto(string AgentId, DateTimeOffset LastActivityUtc, string State);
