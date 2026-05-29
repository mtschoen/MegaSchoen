namespace Claude.Core.Models;

// The last hook-derived state for a session. Despite the name, this is now the
// authoritative current state (not only "waiting" reasons): every relevant hook
// event upserts the session's state file, so Working is a first-class value.
// Kept as WaitingReason to avoid churn across the cycler/UI; IsNeedy() draws the
// line between "blocked on the user" (Permission/AwaitingInput) and Working.
public enum WaitingReason
{
    Permission,
    AwaitingInput,
    Working
}

public static class WaitingReasonExtensions
{
    // "Needy" = the session is blocked on the user and belongs in attention
    // cycling / notifications. Working is live-but-not-blocked and is excluded.
    public static bool IsNeedy(this WaitingReason reason) =>
        reason is WaitingReason.Permission or WaitingReason.AwaitingInput;
}
