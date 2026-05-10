using Claude.Core.Models;

namespace Claude.Core.Tests;

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

    [TestMethod]
    public void IsStillWaiting_TranscriptModTimeExactlyAtThreshold_ReturnsTrue()
    {
        File.WriteAllText(_tempTranscript, "{}");
        var transcriptModTime = File.GetLastWriteTimeUtc(_tempTranscript);

        var grace = TimeSpan.FromSeconds(5);
        var verifier = new SessionLivenessVerifier(grace: grace);
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = new DateTimeOffset(transcriptModTime - grace, TimeSpan.Zero)
        };

        Assert.IsTrue(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void SlowPath_LastEntryIsAssistant_ReturnsTrue()
    {
        File.WriteAllText(_tempTranscript,
            """{"type":"user","timestamp":"2026-05-06T00:00:00Z","message":{"role":"user","content":"hi"}}""" + "\n" +
            """{"type":"assistant","timestamp":"2026-05-06T00:00:01Z","message":{"role":"assistant","content":"working..."}}""" + "\n");

        var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        Assert.IsTrue(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void SlowPath_LastEntryIsUser_ReturnsFalse()
    {
        File.WriteAllText(_tempTranscript,
            """{"type":"assistant","timestamp":"2026-05-06T00:00:00Z","message":{"role":"assistant","content":"done"}}""" + "\n" +
            """{"type":"user","timestamp":"2026-05-06T00:00:05Z","message":{"role":"user","content":"thanks"}}""" + "\n");

        var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        Assert.IsFalse(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void SlowPath_LastEntryIsToolResult_ReturnsFalse()
    {
        File.WriteAllText(_tempTranscript,
            """{"type":"assistant","timestamp":"2026-05-06T00:00:00Z","message":{"role":"assistant","content":"running tool"}}""" + "\n" +
            """{"type":"tool_result","timestamp":"2026-05-06T00:00:02Z","tool_use_id":"x"}""" + "\n");

        var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        Assert.IsFalse(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void SlowPath_LastEntryIsSystem_ReturnsFalse()
    {
        File.WriteAllText(_tempTranscript,
            """{"type":"assistant","timestamp":"2026-05-06T00:00:00Z","message":{"role":"assistant","content":"x"}}""" + "\n" +
            """{"type":"system","timestamp":"2026-05-06T00:00:30Z","content":"/model claude-opus-4-7"}""" + "\n");

        var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        Assert.IsFalse(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void SlowPath_MalformedLastLine_ReturnsTrueFailSafe()
    {
        File.WriteAllText(_tempTranscript,
            """{"type":"assistant","timestamp":"2026-05-06T00:00:00Z","message":{"role":"assistant","content":"x"}}""" + "\n" +
            "{not-json" + "\n");

        var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        Assert.IsTrue(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void SlowPath_EmptyFile_ReturnsFalse()
    {
        File.WriteAllText(_tempTranscript, "");

        var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        Assert.IsFalse(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void SlowPath_TrailingBlankLines_ClassifiesLastNonEmpty()
    {
        File.WriteAllText(_tempTranscript,
            """{"type":"assistant","timestamp":"2026-05-06T00:00:00Z","message":{"role":"assistant","content":"x"}}""" + "\n\n\n");

        var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(1));
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };

        Assert.IsTrue(verifier.IsStillWaiting(entry));
    }
}
