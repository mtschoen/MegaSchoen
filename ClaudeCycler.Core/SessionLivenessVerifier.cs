using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core;

public sealed class SessionLivenessVerifier
{
    static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(5);

    readonly TimeSpan _grace;

    public SessionLivenessVerifier(TimeSpan? grace = null)
    {
        _grace = grace ?? DefaultGrace;
    }

    public bool IsStillWaiting(SessionEntry entry)
    {
        if (string.IsNullOrEmpty(entry.TranscriptPath) || !File.Exists(entry.TranscriptPath))
        {
            return false;
        }

        var transcriptTouchedAt = File.GetLastWriteTimeUtc(entry.TranscriptPath);
        var threshold = entry.NotifiedAt.UtcDateTime + _grace;
        return transcriptTouchedAt <= threshold;
    }
}
