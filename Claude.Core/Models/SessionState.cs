namespace Claude.Core.Models;

// Lower ordinal = more attention-needed. Used for sort order and rollup.
public enum SessionState
{
    PendingPermission = 0,
    AwaitingInput = 1,
    Working = 2,
    Idle = 3,
    Wrapped = 4,
    Unknown = 5
}
