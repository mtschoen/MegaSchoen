using System.Text.Json;
using Claude.Core.Models;

namespace Claude.Core.Remote;

public enum RemoteConnectionState { Connecting, Connected, Disconnected }

public sealed class RemoteSessionStreamClient
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    readonly string _host;
    readonly Func<IStreamProcess> _processFactory;

    public RemoteSessionStreamClient(string host, Func<IStreamProcess> processFactory)
    {
        _host = host;
        _processFactory = processFactory;
    }

    public event Action<IReadOnlyList<SessionSnapshot>>? SnapshotReceived;
    public event Action<RemoteConnectionState>? ConnectionStateChanged;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            ConnectionStateChanged?.Invoke(RemoteConnectionState.Connecting);
            using var process = _processFactory();
            try
            {
                // Connected is reported on the FIRST PARSED LINE, not on
                // process start: Start() only spawns the local ssh binary,
                // which then sits in its TCP connect/auth phase - an
                // unreachable host would otherwise show "connected" for the
                // whole timeout (observed 2026-07-19 on the steamdeck away
                // from the fleet's network). Data on stdout is the only proof
                // the remote CLI is actually streaming.
                process.Start();
                var receivedData = false;
                await foreach (var line in process.ReadLinesAsync(cancellationToken))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var snapshots = Parse(line);
                    if (snapshots is null) continue;
                    if (!receivedData)
                    {
                        receivedData = true;
                        ConnectionStateChanged?.Invoke(RemoteConnectionState.Connected);
                        // Reset backoff only once real data has flowed, so a
                        // host that spawns ssh but never streams keeps backing
                        // off instead of hammering reconnects.
                        backoff = TimeSpan.FromSeconds(1);
                    }
                    SnapshotReceived?.Invoke(snapshots);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* fall through to reconnect */ }

            ConnectionStateChanged?.Invoke(RemoteConnectionState.Disconnected);
            if (cancellationToken.IsCancellationRequested) break;
            try { await Task.Delay(backoff, cancellationToken); } catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
    }

    List<SessionSnapshot>? Parse(string ndjsonLine)
    {
        try
        {
            var snaps = JsonSerializer.Deserialize<List<SessionSnapshot>>(ndjsonLine, JsonOptions);
            if (snaps is null) return null;
            return snaps.ConvertAll(s => s with { Host = _host });
        }
        catch { return null; }   // skip malformed line, keep stream alive
    }

    // Test seam: exercise the NDJSON -> SessionSnapshot mapping without a process.
    internal List<SessionSnapshot>? ParseForTest(string ndjsonLine) => Parse(ndjsonLine);
}
