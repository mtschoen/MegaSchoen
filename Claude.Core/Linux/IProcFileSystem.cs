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
}
