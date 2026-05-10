namespace Claude.Core.Models;

// Lower ordinal = more attention-needed. Used for sort order and rollup.
public enum SessionState
{
    PendingPermission = 0,
    AwaitingInput = 1,
    Working = 2,
    Idle = 3,
    Unknown = 4
}
