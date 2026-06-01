using Claude.Core.Models;

namespace Claude.Core;

// Maps Claude Code hook events to the authoritative per-session state file.
// Every state-relevant event upserts the session's state (or deletes it on
// SessionEnd), so the dashboard can read state directly instead of guessing
// from a transcript tail-read. The key transitions this fixes:
//   - PostToolUse (fires after every tool, including the one a permission
//     prompt was approved for) flips a stale Permission back to Working.
//   - idle_prompt notifications surface AwaitingInput immediately.
public sealed class HookDispatcher
{
    readonly StateStore _store;

    public HookDispatcher(StateStore store)
    {
        _store = store;
    }

    public void Dispatch(HookPayload payload)
    {
        var sessionId = payload.SessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            Logger.Log($"HookDispatcher: missing session_id for event {payload.HookEventName}");
            return;
        }

        try
        {
            switch (payload.HookEventName)
            {
                case "Notification" when payload.NotificationType == "permission_prompt":
                    SetState(sessionId, payload, WaitingReason.Permission, payload.Message);
                    break;

                case "Notification" when payload.NotificationType == "idle_prompt":
                    SetState(sessionId, payload, WaitingReason.AwaitingInput, message: null);
                    break;

                // A mid-task question dialog blocks on the user like a permission prompt.
                case "Notification" when payload.NotificationType == "elicitation_dialog":
                    SetState(sessionId, payload, WaitingReason.AwaitingInput, payload.Message);
                    break;

                // The user answered the question; the turn resumes.
                case "Notification" when payload.NotificationType is "elicitation_complete" or "elicitation_response":
                    SetState(sessionId, payload, WaitingReason.Working, message: null);
                    break;

                case "Notification":
                    // auth_success, etc. carry no state meaning.
                    Logger.Log($"HookDispatcher: ignoring Notification type {payload.NotificationType}");
                    break;

                case "Stop":
                    SetState(sessionId, payload, WaitingReason.AwaitingInput, message: null);
                    break;

                // The session is actively doing work. PostToolUse in particular
                // is the signal that a granted permission has resolved.
                case "UserPromptSubmit":
                case "PreToolUse":
                case "PostToolUse":
                    SetState(sessionId, payload, WaitingReason.Working, message: null);
                    break;

                case "SessionEnd":
                    _store.Delete(sessionId);
                    break;

                default:
                    Logger.Log($"HookDispatcher: ignoring event {payload.HookEventName}");
                    break;
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"HookDispatcher.Dispatch failed: {exception.Message}");
        }
    }

    // Upserts the session's state, skipping the write when nothing observable
    // changed. PostToolUse/PreToolUse fire after/before every single tool, so an
    // unconditional write would rewrite the file (and wake the dashboard's
    // FileSystemWatcher) dozens of times per turn for no state change.
    void SetState(string sessionId, HookPayload payload, WaitingReason reason, string? message)
    {
        var existing = _store.ReadSession(sessionId);
        if (existing is not null
            && existing.Reason == reason
            && existing.Message == message
            && string.Equals(existing.TranscriptPath, payload.TranscriptPath, StringComparison.Ordinal))
        {
            return;
        }

        _store.Upsert(sessionId, new SessionEntry
        {
            Cwd = payload.Cwd ?? "",
            TranscriptPath = payload.TranscriptPath,
            NotifiedAt = DateTimeOffset.UtcNow,
            Message = message,
            Reason = reason
        });
    }
}
