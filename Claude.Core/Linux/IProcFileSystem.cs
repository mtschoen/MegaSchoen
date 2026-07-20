namespace Claude.Core.Linux;

// Abstraction over the bits of /proc the locator needs, so the locator is
// unit-testable on a non-Linux CI host with a fake.
public interface IProcFileSystem
{
    long BootTimeEpochSeconds { get; }
    long ClockTicksPerSecond { get; }
    IEnumerable<int> EnumeratePids();
    string? ReadComm(int pid);          // /proc/<pid>/comm, trimmed (no trailing newline)
    string? ReadCwd(int pid);           // readlink /proc/<pid>/cwd
    long? ReadStartTicks(int pid);      // field 22 of /proc/<pid>/stat
    int? ReadParentPid(int pid);        // field 4 of /proc/<pid>/stat
    string? ReadEnviron(int pid);       // raw NUL-delimited /proc/<pid>/environ, or null
    string? ReadNetTcp();               // raw contents of /proc/net/tcp, or null
    string? ReadNetTcp6();              // raw contents of /proc/net/tcp6, or null
    int? FindPidOwningSocketInode(long inode); // scans /proc/<pid>/fd/* for socket:[inode]
    string? ReadCmdlineFirstLine(int pid);     // first NUL-delimited token of /proc/<pid>/cmdline
}
