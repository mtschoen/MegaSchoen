namespace DisplayManager.Core;

// Lightweight diagnostic sink for non-fatal, swallowed-but-noteworthy failures
// in display/profile code (malformed native JSON, a corrupt profile/draft file,
// a file-access race). DisplayManager.Core intentionally takes no dependency on
// the host's logging stack, so the host installs Sink at startup - the MAUI app
// points it at Claude.Core.Logger, the CLI at stderr. Defaults to a no-op, so
// the library (and tests) stay silent unless a sink is installed.
public static class DiagnosticLog
{
    public static Action<string>? Sink { get; set; }

    public static void Log(string message) => Sink?.Invoke(message);
}
