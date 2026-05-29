namespace Claude.Core.Linux;

public sealed class ProcFileSystem : IProcFileSystem
{
    readonly string _root;

    public ProcFileSystem() : this("/proc", statContents: null) { }

    // Test seam: pass /proc/stat contents directly to exercise btime parsing.
    public ProcFileSystem(string statContents) : this("/proc", statContents) { }

    ProcFileSystem(string root, string? statContents)
    {
        _root = root;
        BootTimeEpochSeconds = ParseBtime(statContents ?? SafeReadAllText(Path.Combine(_root, "stat")) ?? "");
        // USER_HZ is 100 on all mainstream x86_64/arm64 Linux (confirmed on llamabox).
        // The enumerator only needs start-time within a 30s tolerance, so this is safe.
        ClockTicksPerSecond = 100;
    }

    public long BootTimeEpochSeconds { get; }
    public long ClockTicksPerSecond { get; }

    public IEnumerable<int> EnumeratePids()
    {
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            var name = Path.GetFileName(dir);
            if (int.TryParse(name, out var pid)) yield return pid;
        }
    }

    public string? ReadComm(int pid) => SafeReadAllText(Path.Combine(_root, pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "comm"))?.Trim();

    public string? ReadCwd(int pid)
    {
        try { return new FileInfo(Path.Combine(_root, pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "cwd")).LinkTarget; }
        catch { return null; }
    }

    public long? ReadStartTicks(int pid)
    {
        var stat = SafeReadAllText(Path.Combine(_root, pid.ToString(System.Globalization.CultureInfo.InvariantCulture), "stat"));
        if (stat is null) return null;
        // comm (field 2) may contain spaces/parens; it is wrapped in (), so split after the last ')'.
        var close = stat.LastIndexOf(')');
        if (close < 0) return null;
        var rest = stat[(close + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // After comm, fields are 3..; field 22 is index (22 - 3) = 19 in this tail array.
        return rest.Length > 19 && long.TryParse(rest[19], out var ticks) ? ticks : null;
    }

    static long ParseBtime(string statContents)
    {
        foreach (var line in statContents.Split('\n'))
        {
            if (line.StartsWith("btime ", StringComparison.Ordinal)
                && long.TryParse(line.AsSpan(6).Trim(), out var btime))
            {
                return btime;
            }
        }
        return 0;
    }

    static string? SafeReadAllText(string path)
    {
        try { return File.ReadAllText(path); } catch { return null; }
    }
}
