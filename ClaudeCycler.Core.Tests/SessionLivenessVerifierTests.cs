using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core.Tests;

[TestClass]
public class SessionLivenessVerifierTests
{
    string _tempTranscript = "";

    [TestInitialize]
    public void Setup()
    {
        _tempTranscript = Path.Combine(Path.GetTempPath(), $"transcript-{Guid.NewGuid():N}.jsonl");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempTranscript))
        {
            File.Delete(_tempTranscript);
        }
    }

    [TestMethod]
    public void IsStillWaiting_TranscriptMissing_ReturnsFalse()
    {
        var verifier = new SessionLivenessVerifier();
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        Assert.IsFalse(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void IsStillWaiting_TranscriptPathNull_ReturnsFalse()
    {
        var verifier = new SessionLivenessVerifier();
        var entry = new SessionEntry
        {
            TranscriptPath = null,
            NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        Assert.IsFalse(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void IsStillWaiting_TranscriptOlderThanNotification_ReturnsTrue()
    {
        File.WriteAllText(_tempTranscript, "{}");
        var transcriptModTime = File.GetLastWriteTimeUtc(_tempTranscript);

        var verifier = new SessionLivenessVerifier();
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = new DateTimeOffset(transcriptModTime.AddSeconds(10), TimeSpan.Zero)
        };

        Assert.IsTrue(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void IsStillWaiting_TranscriptWithinGraceWindow_ReturnsTrue()
    {
        File.WriteAllText(_tempTranscript, "{}");
        var transcriptModTime = File.GetLastWriteTimeUtc(_tempTranscript);

        var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(5));
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = new DateTimeOffset(transcriptModTime.AddSeconds(-2), TimeSpan.Zero)
        };

        Assert.IsTrue(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void IsStillWaiting_TranscriptTouchedAfterGraceWindow_ReturnsFalse()
    {
        File.WriteAllText(_tempTranscript, "{}");
        var transcriptModTime = File.GetLastWriteTimeUtc(_tempTranscript);

        var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(5));
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = new DateTimeOffset(transcriptModTime.AddSeconds(-30), TimeSpan.Zero)
        };

        Assert.IsFalse(verifier.IsStillWaiting(entry));
    }
}
