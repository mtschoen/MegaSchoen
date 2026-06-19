using Claude.Core;
using Claude.Core.Models;

namespace Claude.Core.Tests;

[TestClass]
public class SessionStateClassifierTests
{
    string _tempFile = "";

    [TestInitialize]
    public void Setup()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"sscl-{Guid.NewGuid():N}.jsonl");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [TestMethod]
    public void Classify_StateStoreHitWithPermissionReason_ReturnsPendingPermission()
    {
        var entry = new SessionEntry { Reason = WaitingReason.Permission, TranscriptPath = _tempFile };
        Assert.AreEqual(SessionState.PendingPermission,
            SessionStateClassifier.Classify(entry, _tempFile));
    }

    [TestMethod]
    public void Classify_StateStoreHitWithAwaitingInputReason_ReturnsAwaitingInput()
    {
        var entry = new SessionEntry { Reason = WaitingReason.AwaitingInput, TranscriptPath = _tempFile };
        Assert.AreEqual(SessionState.AwaitingInput,
            SessionStateClassifier.Classify(entry, _tempFile));
    }

    [TestMethod]
    public void Classify_StateStoreHitWithWorkingReason_ReturnsWorking()
    {
        var entry = new SessionEntry { Reason = WaitingReason.Working, TranscriptPath = _tempFile };
        Assert.AreEqual(SessionState.Working,
            SessionStateClassifier.Classify(entry, _tempFile));
    }

    [TestMethod]
    public void Classify_NoStateEntryAndAssistantLast_ReturnsWorking()
    {
        File.WriteAllText(_tempFile, """{"type":"assistant","message":{}}""" + "\n");
        Assert.AreEqual(SessionState.Working, SessionStateClassifier.Classify(stateEntry: null, _tempFile));
    }

    [TestMethod]
    public void Classify_NoStateEntryAndUserLast_ReturnsIdle()
    {
        File.WriteAllText(_tempFile, """{"type":"user","message":{}}""" + "\n");
        Assert.AreEqual(SessionState.Idle, SessionStateClassifier.Classify(stateEntry: null, _tempFile));
    }

    [TestMethod]
    public void Classify_TranscriptMissing_ReturnsUnknown()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jsonl");
        Assert.AreEqual(SessionState.Unknown, SessionStateClassifier.Classify(stateEntry: null, missingPath));
    }

    [TestMethod]
    public void Classify_StateStoreHitWithUnrecognizedReason_ReturnsUnknown()
    {
        // Defensive default of the reason switch: an out-of-range WaitingReason
        // (e.g. a future value read from an older/newer state file) maps to Unknown.
        var entry = new SessionEntry { Reason = (WaitingReason)99, TranscriptPath = _tempFile };
        Assert.AreEqual(SessionState.Unknown, SessionStateClassifier.Classify(entry, _tempFile));
    }
}
