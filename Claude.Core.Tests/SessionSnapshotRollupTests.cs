using Claude.Core.Models;

namespace Claude.Core.Tests;

[TestClass]
public class SessionSnapshotRollupTests
{
    static SessionSnapshot Make(SessionState parent, params SessionState[] subagents) =>
        new(
            SessionId: "abc",
            Cwd: @"C:\foo",
            TranscriptPath: @"C:\foo.jsonl",
            LastActivityUtc: DateTimeOffset.UtcNow,
            State: parent,
            PendingMessage: null,
            Window: WindowToken.FromHandle(IntPtr.Zero),
            WindowTitle: null,
            Subagents: subagents
                .Select((s, i) => new SubagentSnapshot($"a{i}", $"p{i}", DateTimeOffset.UtcNow, s))
                .ToArray());

    [TestMethod]
    public void RollupState_NoSubagents_EqualsParent()
    {
        Assert.AreEqual(SessionState.Idle, Make(SessionState.Idle).RollupState);
    }

    [TestMethod]
    public void RollupState_PicksMinOrdinalAcrossParentAndSubagents()
    {
        // Idle parent, one Working subagent -> Working (lower ordinal wins).
        Assert.AreEqual(SessionState.Working, Make(SessionState.Idle, SessionState.Working).RollupState);
    }

    [TestMethod]
    public void RollupState_PendingPermissionSubagentBeatsAwaitingParent()
    {
        Assert.AreEqual(
            SessionState.PendingPermission,
            Make(SessionState.AwaitingInput, SessionState.PendingPermission, SessionState.Idle).RollupState);
    }

    [TestMethod]
    public void RollupState_AllResolvedAcrossParentAndSubagents()
    {
        Assert.AreEqual(SessionState.Idle, Make(SessionState.Idle, SessionState.Idle, SessionState.Unknown).RollupState);
    }
}
