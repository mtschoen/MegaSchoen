using System.Globalization;

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

    public string? ReadComm(int pid) => SafeReadAllText(Path.Combine(_root, pid.ToString(CultureInfo.InvariantCulture), "comm"))?.Trim();

    public string? ReadCwd(int pid)
    {
        try { return new FileInfo(Path.Combine(_root, pid.ToString(CultureInfo.InvariantCulture), "cwd")).LinkTarget; }
        catch { return null; }
    }

    public string? ReadEnviron(int pid) =>
        SafeReadAllText(Path.Combine(_root, pid.ToString(CultureInfo.InvariantCulture), "environ"));

    public string? ReadNetTcp() => SafeReadAllText(Path.Combine(_root, "net", "tcp"));

    public string? ReadNetTcp6() => SafeReadAllText(Path.Combine(_root, "net", "tcp6"));

    // Scans every process's open file descriptors for one whose symlink target
    // is "socket:[<inode>]" - the only way to map a socket inode back to its
    // owning pid on Linux. Best-effort: a pid whose /fd directory disappears or
    // denies access (permissions, exited between enumerate and read) is skipped.
    public int? FindPidOwningSocketInode(long inode)
    {
        var target = $"socket:[{inode}]";
        foreach (var pid in EnumeratePids())
        {
            string[] fileDescriptorPaths;
            try
            {
                fileDescriptorPaths = Directory.GetFileSystemEntries(
                    Path.Combine(_root, pid.ToString(CultureInfo.InvariantCulture), "fd"));
            }
            catch
            {
                continue;
            }

            foreach (var fileDescriptorPath in fileDescriptorPaths)
            {
                string? linkTarget;
                try { linkTarget = new FileInfo(fileDescriptorPath).LinkTarget; }
                catch { continue; }

                if (string.Equals(linkTarget, target, StringComparison.Ordinal)) return pid;
            }
        }
        return null;
    }

    public string? ReadCmdlineFirstLine(int pid)
    {
        var raw = SafeReadAllText(Path.Combine(_root, pid.ToString(CultureInfo.InvariantCulture), "cmdline"));
        return string.IsNullOrEmpty(raw)
            ? null
            : raw.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    public long? ReadStartTicks(int pid)
    {
        // Field 22; index (22 - 3) = 19 in the after-comm tail array.
        var tail = ReadStatTail(pid);
        return tail is not null && tail.Length > 19 && long.TryParse(tail[19], out var ticks) ? ticks : null;
    }

    public int? ReadParentPid(int pid)
    {
        // Field 4 (ppid); index (4 - 3) = 1 in the after-comm tail array.
        var tail = ReadStatTail(pid);
        return tail is not null && tail.Length > 1 && int.TryParse(tail[1], out var parent) ? parent : null;
    }

    string[]? ReadStatTail(int pid)
    {
        var stat = SafeReadAllText(Path.Combine(_root, pid.ToString(CultureInfo.InvariantCulture), "stat"));
        if (stat is null) return null;
        // comm (field 2) may contain spaces/parens; it is wrapped in (), so split after the last ')'.
        var close = stat.LastIndexOf(')');
        return close < 0 ? null : stat[(close + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
