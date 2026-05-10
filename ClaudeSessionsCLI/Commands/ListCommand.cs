using System.Text.Json;
using Claude.Core;
using Claude.Core.Models;
using Claude.Core.Windows;

namespace ClaudeSessionsCLI.Commands;

static class ListCommand
{
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
        var locator = new WindowsClaudeProcessLocator();
        var store = new StateStore();
        return new ActiveSessionEnumerator(locator, store);
    }

    static int EmitJsonOnce(ActiveSessionEnumerator enumerator)
    {
        var snapshots = enumerator.Enumerate();
        var json = JsonSerializer.Serialize(
            snapshots.Select(SnapshotDto.From).ToArray(),
            new JsonSerializerOptions { WriteIndented = true });
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

    static Task<int> RunHumanMode(ActiveSessionEnumerator enumerator, CliOptions options) =>
        Task.FromResult(0); // implemented in Task 6.4
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
