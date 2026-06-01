using System.Threading.Channels;
using Claude.Core.Models;
using Claude.Core.Remote;

namespace Claude.Core.Tests.Remote;

[TestClass]
public class RemoteSessionStreamClientTests
{
    sealed class FakeStreamProcess : IStreamProcess
    {
        readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
        public bool Started;
        public void Emit(string line) => _lines.Writer.TryWrite(line);
        public void EndStream() => _lines.Writer.TryComplete();
        public void Start() => Started = true;
        public async IAsyncEnumerable<string> ReadLinesAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var l in _lines.Reader.ReadAllAsync(ct)) yield return l;
        }
        public void Kill() => _lines.Writer.TryComplete();
        public void Dispose() { }
    }

    [TestMethod]
    public async Task EmitsSnapshots_TaggedWithHost()
    {
        var fake = new FakeStreamProcess();
        var received = new List<IReadOnlyList<SessionSnapshot>>();
        var client = new RemoteSessionStreamClient("llamabox", () => fake);
        client.SnapshotReceived += snaps => received.Add(snaps);

        var cts = new CancellationTokenSource();
        var run = client.RunAsync(cts.Token);
        fake.Emit("""[{"SessionId":"abc","Cwd":"/home/schoen/pr-crew","TranscriptPath":"/x","LastActivityUtc":"2026-05-24T03:00:00+00:00","State":"PendingPermission","RollupState":"PendingPermission","PendingMessage":null,"WindowTitle":"t","Subagents":[]}]""");
        await Task.Delay(50, TestContext.CancellationToken);

        Assert.HasCount(1, received);
        Assert.AreEqual("abc", received[0][0].SessionId);
        Assert.AreEqual("llamabox", received[0][0].Host);   // tagged by the client

        fake.EndStream();
        cts.Cancel();          // RunAsync catches OperationCanceledException and returns
        await run;
    }

    public TestContext TestContext { get; set; }
}
