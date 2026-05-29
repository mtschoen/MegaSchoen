using Claude.Core.Models;

namespace Claude.Core.Linux;

public sealed class LinuxClaudeProcessLocator : IClaudeProcessLocator
{
    readonly IProcFileSystem _proc;

    public LinuxClaudeProcessLocator() : this(new ProcFileSystem()) { }
    public LinuxClaudeProcessLocator(IProcFileSystem proc) => _proc = proc;

    public IReadOnlyList<ClaudeWindow> EnumerateLiveSessions()
    {
        var result = new List<ClaudeWindow>();
        foreach (var pid in _proc.EnumeratePids())
        {
            // Match on comm ONLY. The args of child shells (e.g. CI-poll one-liners)
            // can contain "claude" paths and would false-positive on an args match.
            if (!string.Equals(_proc.ReadComm(pid), "claude", StringComparison.Ordinal)) continue;

            var cwd = _proc.ReadCwd(pid);
            if (string.IsNullOrEmpty(cwd)) continue;             // need a cwd to map to a slug

            var ticks = _proc.ReadStartTicks(pid);
            if (ticks is null) continue;

            var startEpoch = _proc.BootTimeEpochSeconds + (ticks.Value / _proc.ClockTicksPerSecond);
            result.Add(new ClaudeWindow(
                ProcessId: (uint)pid,
                Window: WindowToken.Null,
                Title: string.Empty,
                WorkingDirectory: cwd,
                StartTimeUtc: DateTimeOffset.FromUnixTimeSeconds(startEpoch)));
        }
        return result;
    }
}
