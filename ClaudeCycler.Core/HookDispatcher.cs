using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core;

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
                    _store.Upsert(payload.SessionId, new SessionEntry
                    {
                        Cwd = payload.Cwd ?? "",
                        NotifiedAt = DateTimeOffset.UtcNow,
                        Message = payload.Message
                    });
                    break;

                case "UserPromptSubmit":
                case "Stop":
                    _store.Delete(payload.SessionId);
                    break;

                default:
                    Logger.Log($"HookDispatcher: ignoring event {payload.HookEventName} / type {payload.NotificationType}");
                    break;
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"HookDispatcher.Dispatch failed: {exception.Message}");
        }
    }
}
