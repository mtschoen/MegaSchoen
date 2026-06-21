using System.Threading.Channels;
using Claude.Core;

namespace Claude.Core.Tests;

// Issue #28: a single stray exception used to escape the refresh loop body and
// permanently kill the consumer. SessionRefreshLoop must instead log-and-skip a
// faulting tick and keep consuming subsequent signals.
[TestClass]
public class SessionRefreshLoopTests
{
    // A zero-delay so the debounce does not slow the test down.
    static Task NoDelay(int milliseconds, CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
            ? Task.FromCanceled(cancellationToken)
            : Task.CompletedTask;

    [TestMethod]
    public async Task FaultingTick_DoesNotStopSubsequentGoodTick()
    {
        var channel = Channel.CreateUnbounded<byte>();
        var ticks = 0;
        var firstTickRan = new TaskCompletionSource();
        var secondTickRan = new TaskCompletionSource();

        Task Refresh(CancellationToken _)
        {
            ticks++;
            if (ticks == 1)
            {
                firstTickRan.TrySetResult();
                throw new InvalidOperationException("transient enumeration fault");
            }

            secondTickRan.TrySetResult();
            return Task.CompletedTask;
        }

        using var cts = new CancellationTokenSource();
        var loop = new SessionRefreshLoop(channel.Reader, Refresh, debounceMilliseconds: 0, delay: NoDelay);
        var run = loop.RunAsync(cts.Token);

        // Signal the first tick and wait for it to fault. Only then signal the
        // second - otherwise the loop's drain would collapse both signals into
        // the single faulting tick.
        await channel.Writer.WriteAsync(0);
        await firstTickRan.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await channel.Writer.WriteAsync(0);

        // The loop survived the faulting tick and runs the second.
        await secondTickRan.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();
        channel.Writer.TryComplete();
        await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.AreEqual(2, ticks);
    }

    [TestMethod]
    public async Task Cancellation_StopsLoopCleanly()
    {
        var channel = Channel.CreateUnbounded<byte>();
        using var cts = new CancellationTokenSource();
        var loop = new SessionRefreshLoop(channel.Reader, _ => Task.CompletedTask, debounceMilliseconds: 0, delay: NoDelay);
        var run = loop.RunAsync(cts.Token);

        cts.Cancel();

        // RunAsync swallows the shutdown OperationCanceledException and completes
        // without faulting.
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(run.IsCompletedSuccessfully);
    }

    [TestMethod]
    public async Task RefreshThrowingCancellation_StopsLoop()
    {
        var channel = Channel.CreateUnbounded<byte>();
        using var cts = new CancellationTokenSource();

        Task Refresh(CancellationToken token)
        {
            cts.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        var loop = new SessionRefreshLoop(channel.Reader, Refresh, debounceMilliseconds: 0, delay: NoDelay);
        var run = loop.RunAsync(cts.Token);

        await channel.Writer.WriteAsync(0);

        // An OperationCanceledException from the refresh action is treated as
        // shutdown, not a per-iteration fault, so the loop stops cleanly.
        await run.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(run.IsCompletedSuccessfully);
    }
}
