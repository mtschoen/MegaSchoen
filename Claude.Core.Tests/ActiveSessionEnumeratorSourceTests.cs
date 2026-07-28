using Claude.Core.Models;
using Claude.Core.Tests.Fakes;

namespace Claude.Core.Tests;

[TestClass]
public class ActiveSessionEnumeratorSourceTests
{
    static readonly string[] CombinedSessionIds = ["first", "second"];
    static readonly string[] ExpectedSort =
        ["permission", "newer-working", "older-working", "idle"];

    sealed class FakeSessionSource(params SessionSnapshot[] snapshots) : ISessionSource
    {
        public int EnumerationCount { get; private set; }

        public IReadOnlyList<SessionSnapshot> Enumerate()
        {
            EnumerationCount++;
            return snapshots;
        }
    }

    [TestMethod]
    public void Enumerate_CombinesEveryConfiguredSource()
    {
        var first = new FakeSessionSource(Snapshot("first", SessionState.Working));
        var second = new FakeSessionSource(Snapshot("second", SessionState.Idle));
        var enumerator = new ActiveSessionEnumerator([first, second]);

        var result = enumerator.Enumerate();

        CollectionAssert.AreEquivalent(CombinedSessionIds, result.Select(snapshot => snapshot.SessionId).ToArray());
        Assert.AreEqual(1, first.EnumerationCount);
        Assert.AreEqual(1, second.EnumerationCount);
    }

    [TestMethod]
    public void Enumerate_SortsCombinedSourcesByAttentionThenRecentActivity()
    {
        var now = DateTimeOffset.UtcNow;
        var first = new FakeSessionSource(
            Snapshot("idle", SessionState.Idle, now),
            Snapshot("older-working", SessionState.Working, now.AddMinutes(-2)));
        var second = new FakeSessionSource(
            Snapshot("permission", SessionState.PendingPermission, now.AddMinutes(-10)),
            Snapshot("newer-working", SessionState.Working, now));
        var enumerator = new ActiveSessionEnumerator([first, second]);

        var result = enumerator.Enumerate();

        CollectionAssert.AreEqual(ExpectedSort, result.Select(snapshot => snapshot.SessionId).ToArray());
    }

    [TestMethod]
    public void ClaudeCompatibilityConstructor_WithNoLiveProcesses_ReturnsEmpty()
    {
        var stateDirectory = Path.Combine(Path.GetTempPath(), $"source-store-{Guid.NewGuid():N}");
        try
        {
            var enumerator = new ActiveSessionEnumerator(
                new FakeProcessLocator(),
                new StateStore(stateDirectory));

            Assert.IsEmpty(enumerator.Enumerate());
        }
        finally
        {
            if (Directory.Exists(stateDirectory))
            {
                Directory.Delete(stateDirectory, recursive: true);
            }
        }
    }

    static SessionSnapshot Snapshot(
        string sessionId,
        SessionState state,
        DateTimeOffset? lastActivityUtc = null) =>
        new(
            sessionId,
            "/repo",
            "",
            lastActivityUtc ?? DateTimeOffset.UtcNow,
            state,
            null,
            WindowToken.Null,
            null,
            Array.Empty<SubagentSnapshot>());
}
