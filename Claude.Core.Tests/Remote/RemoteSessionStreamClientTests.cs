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
        // Signal on receipt instead of sleeping a fixed delay: RunAsync reads the
        // emitted line on a thread-pool continuation, which a loaded CI runner does
        // not reliably finish inside a fixed window (the source of the flake).
        var firstSnapshot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RemoteSessionStreamClient("llamabox", () => fake);
        client.SnapshotReceived += snaps =>
        {
            received.Add(snaps);
            firstSnapshot.TrySetResult();
        };

        var cts = new CancellationTokenSource();
        var run = client.RunAsync(cts.Token);
        fake.Emit("""[{"SessionId":"abc","Cwd":"/home/schoen/pr-crew","CurrentCwd":"/home/schoen/pr-crew/other","TranscriptPath":"/x","LastActivityUtc":"2026-05-24T03:00:00+00:00","State":"PendingPermission","RollupState":"PendingPermission","PendingMessage":null,"WindowTitle":"t","Subagents":[]}]""");
        await firstSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

        Assert.IsTrue(fake.Started);   // the client starts the process before reading
        Assert.HasCount(1, received);
        Assert.AreEqual("abc", received[0][0].SessionId);
        Assert.AreEqual("/home/schoen/pr-crew/other", received[0][0].CurrentCwd);
        Assert.AreEqual("llamabox", received[0][0].Host);   // tagged by the client

        fake.EndStream();
        cts.Cancel();          // RunAsync catches OperationCanceledException and returns
        await run;
    }

    [TestMethod]
    public async Task ConnectedState_ReportedOnFirstParsedLine_NotOnProcessStart()
    {
        var fake = new FakeStreamProcess();
        var states = new List<RemoteConnectionState>();
        var firstSnapshot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new RemoteSessionStreamClient("llamabox", () => fake);
        client.ConnectionStateChanged += state => states.Add(state);
        client.SnapshotReceived += _ => firstSnapshot.TrySetResult();

        var cts = new CancellationTokenSource();
        var run = client.RunAsync(cts.Token);

        // ssh spawned but nothing received yet: an unreachable host looks
        // exactly like this, and must NOT read as Connected.
        fake.Emit("");                          // blank keepalive noise
        fake.Emit("not json");                  // malformed line
        Assert.DoesNotContain(RemoteConnectionState.Connected, states);

        fake.Emit("""[{"SessionId":"abc","Cwd":"/x","TranscriptPath":"/t","LastActivityUtc":"2026-07-19T00:00:00+00:00","State":"Working","RollupState":"Working","PendingMessage":null,"WindowTitle":null,"Subagents":[]}]""");
        await firstSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

        Assert.Contains(RemoteConnectionState.Connected, states);

        fake.EndStream();
        cts.Cancel();
        await run;
    }

    [TestMethod]
    public void Parse_DeserializesSshClientPort_AndStampsHost()
    {
        // A wire line as emitted by the remote CLI (SnapshotDto shape).
        const string line = """
        [{"SessionId":"r1","Cwd":"/home/schoen/site","CurrentCwd":"/home/schoen/site/src","TranscriptPath":"/t.jsonl","LastActivityUtc":"2026-06-04T00:00:00+00:00","State":"Working","RollupState":"Working","Mode":"Auto","PendingMessage":null,"WindowTitle":null,"Subagents":[],"SshClientPort":51000}]
        """;

        var client = new RemoteSessionStreamClient("llamabox", () => throw new InvalidOperationException("not used"));
        var snaps = client.ParseForTest(line);

        Assert.IsNotNull(snaps);
        Assert.HasCount(1, snaps);
        Assert.AreEqual(SessionMode.Auto, snaps[0].Mode);
        Assert.AreEqual("/home/schoen/site/src", snaps[0].CurrentCwd);
        Assert.AreEqual(51000, snaps[0].SshClientPort);
        Assert.AreEqual("llamabox", snaps[0].Host);
    }

    [TestMethod]
    public void Parse_LegacyPayloadWithoutCurrentCwd_DefaultsToRootCwd()
    {
        const string line = """
        [{"SessionId":"r1","Cwd":"/home/schoen/site","TranscriptPath":"/t.jsonl","LastActivityUtc":"2026-06-04T00:00:00+00:00","State":"Working","RollupState":"Working","PendingMessage":null,"WindowTitle":null,"Subagents":[]}]
        """;

        var client = new RemoteSessionStreamClient("llamabox", () => throw new InvalidOperationException("not used"));
        var snaps = client.ParseForTest(line);

        Assert.IsNotNull(snaps);
        Assert.AreEqual("/home/schoen/site", snaps[0].CurrentCwd);
    }

    public TestContext TestContext { get; set; } = null!;
}
