using Claude.Core.Models;

namespace Claude.Core;

public static class SessionModeMapper
{
    public static SessionMode FromPermissionMode(string? permissionMode) => permissionMode switch
    {
        "plan" => SessionMode.Plan,
        "auto" => SessionMode.Auto,
        "default" or "acceptEdits" or "dontAsk" or "bypassPermissions" => SessionMode.Build,
        _ => SessionMode.Unknown
    };
}
