using System.Threading.Channels;

namespace Claude.Core;

// Consumes refresh signals from a bounded channel and runs a supplied refresh
// action per tick, debounced and drained. Each iteration is individually
// guarded: a faulting refresh (a process exiting mid-snapshot, a transient
// memory/handle blip, an enumeration hiccup) is logged and skipped, and the
// loop keeps consuming subsequent signals. Only OperationCanceledException -
// the clean-shutdown signal - is allowed to stop the loop.
//
// Extracted from SessionsPageViewModel so the resilience contract lives in the
// (unit-tested, coverage-measured) domain layer rather than the MAUI view model,
// which the test project cannot reference. See issue #28: previously a single
// stray exception escaping the loop body permanently killed the refresh task and
// the Sessions dashboard went stale until the app was restarted.
public sealed class SessionRefreshLoop
{
    readonly ChannelReader<byte> _signal;
    readonly Func<CancellationToken, Task> _refresh;
    readonly int _debounceMilliseconds;
    readonly Func<int, CancellationToken, Task> _delay;

    public SessionRefreshLoop(
        ChannelReader<byte> signal,
        Func<CancellationToken, Task> refresh,
        int debounceMilliseconds = 250,
        Func<int, CancellationToken, Task>? delay = null)
    {
        _signal = signal;
        _refresh = refresh;
        _debounceMilliseconds = debounceMilliseconds;
        _delay = delay ?? ((milliseconds, token) => Task.Delay(milliseconds, token));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var _ in _signal.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    // Debounce a burst of watcher events, then drain any signals
                    // that piled up during the wait so they collapse into this tick.
                    await _delay(_debounceMilliseconds, cancellationToken).ConfigureAwait(false);
                    while (_signal.TryRead(out var __)) { }
                    await _refresh(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw; // honor shutdown; do not treat as a per-iteration fault
                }
                catch (Exception exception)
                {
                    // Keep looping; the next watcher event retries. A single bad
                    // tick must not take down the whole dashboard refresh.
                    Logger.Log($"SessionRefreshLoop iteration failed: {exception}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* normal shutdown */
        }
    }
}
