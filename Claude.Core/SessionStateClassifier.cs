using Claude.Core.Models;

namespace Claude.Core;

public static class SessionStateClassifier
{
    public static SessionState Classify(SessionEntry? stateEntry, string transcriptPath)
    {
        if (stateEntry is not null)
        {
            return stateEntry.Reason switch
            {
                WaitingReason.Permission => SessionState.PendingPermission,
                WaitingReason.AwaitingInput => SessionState.AwaitingInput,
                WaitingReason.Working => SessionState.Working,
                _ => SessionState.Unknown
            };
        }

        if (string.IsNullOrEmpty(transcriptPath) || !File.Exists(transcriptPath))
        {
            return SessionState.Unknown;
        }

        try
        {
            return SessionLivenessVerifier.ClassifyLastEntry(transcriptPath) switch
            {
                LastEntryClass.SessionPending => SessionState.Working,
                LastEntryClass.Resolved => SessionState.Idle,
                _ => SessionState.Unknown
            };
        }
        catch (Exception exception)
        {
            Logger.Log($"SessionStateClassifier: ClassifyLastEntry threw for {transcriptPath}: {exception.Message}");
            return SessionState.Unknown;
        }
    }
}
