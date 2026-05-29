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
        if (string.IsNullOrEmpty(payload.SessionId))
        {
            Logger.Log($"HookDispatcher: missing session_id for event {payload.HookEventName}");
            return;
        }

        try
        {
            switch (payload.HookEventName)
            {
                case "Notification" when payload.NotificationType == "permission_prompt":
                    SetState(payload, WaitingReason.Permission, payload.Message);
                    break;

                case "Notification" when payload.NotificationType == "idle_prompt":
                    SetState(payload, WaitingReason.AwaitingInput, message: null);
                    break;

                case "Notification":
                    // auth_success, elicitation_*, etc. carry no state meaning.
                    Logger.Log($"HookDispatcher: ignoring Notification type {payload.NotificationType}");
                    break;

                case "Stop":
                    SetState(payload, WaitingReason.AwaitingInput, message: null);
                    break;

                // The session is actively doing work. PostToolUse in particular
                // is the signal that a granted permission has resolved.
                case "UserPromptSubmit":
                case "PreToolUse":
                case "PostToolUse":
                    SetState(payload, WaitingReason.Working, message: null);
                    break;

                case "SessionEnd":
                    _store.Delete(payload.SessionId);
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
    void SetState(HookPayload payload, WaitingReason reason, string? message)
    {
        var existing = _store.ReadSession(payload.SessionId!);
        if (existing is not null
            && existing.Reason == reason
            && existing.Message == message
            && string.Equals(existing.TranscriptPath, payload.TranscriptPath, StringComparison.Ordinal))
        {
            return;
        }

        _store.Upsert(payload.SessionId!, new SessionEntry
        {
            Cwd = payload.Cwd ?? "",
            TranscriptPath = payload.TranscriptPath,
            NotifiedAt = DateTimeOffset.UtcNow,
            Message = message,
            Reason = reason
        });
    }
}
