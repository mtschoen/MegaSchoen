using System.Globalization;
using Claude.Core.Models;

namespace Claude.Core.Linux;

// Production ISshSessionWindowResolver for Linux: resolves a remote session's
// reported ssh client port to the local ssh process hosting it, then hands
// back a PID-carrying WindowToken so LinuxClaudeWindowFocuser's ancestor walk
// (ssh -> shell -> terminal emulator) can activate the hosting window
// unchanged - the same mechanism used for local claude sessions.
//
// Resolution path mirrors the Windows analog (SshSessionWindowResolver /
// WindowsSshSessionWindowResolver): local port -> owning pid -> verify the
// owner is "ssh" (a stale or reused port must never point Focus at the wrong
// window) -> window. On Linux "owning pid" has no TCP table API, so it goes
// through the socket inode: /proc/net/tcp(6) maps the local port of an
// ESTABLISHED connection to a socket inode, then every process's open file
// descriptors are scanned for a symlink to that inode.
public sealed class LinuxSshSessionWindowResolver : ISshSessionWindowResolver
{
    const string EstablishedState = "01";

    readonly IProcFileSystem _proc;

    public LinuxSshSessionWindowResolver() : this(new ProcFileSystem()) { }

    // Test seam: the /proc abstraction is injected.
    public LinuxSshSessionWindowResolver(IProcFileSystem proc)
    {
        _proc = proc;
    }

    public (WindowToken Window, string Title)? ResolveWindow(int sshClientPort)
    {
        if (sshClientPort <= 0) return null;

        var inode = FindEstablishedInode(_proc.ReadNetTcp(), sshClientPort)
            ?? FindEstablishedInode(_proc.ReadNetTcp6(), sshClientPort);
        if (inode is not { } socketInode) return null;

        if (_proc.FindPidOwningSocketInode(socketInode) is not { } pid) return null;

        // Reject anything that is not ssh (stale port reuse, wrong match).
        var comm = _proc.ReadComm(pid);
        if (!string.Equals(comm, "ssh", StringComparison.OrdinalIgnoreCase)) return null;

        var title = _proc.ReadCmdlineFirstLine(pid) ?? "";
        return (WindowToken.FromHandle(pid), title);
    }

    // Pure: parses /proc/net/tcp or /proc/net/tcp6 lines (both share this
    // column layout) for an ESTABLISHED row whose local port matches, and
    // returns the socket inode. Returns null on no match or malformed input.
    public static long? FindEstablishedInode(string? procNetTcpContents, int port)
    {
        if (string.IsNullOrEmpty(procNetTcpContents)) return null;

        var targetPortHex = ((uint)port).ToString("X4", CultureInfo.InvariantCulture);

        foreach (var line in procNetTcpContents.Split('\n').Skip(1))
        {
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // sl local_address rem_address st tx:rx tr:tm retrnsmt uid timeout inode ...
            if (fields.Length < 10) continue;

            var localAddress = fields[1];
            var colon = localAddress.LastIndexOf(':');
            if (colon < 0) continue;

            var localPortHex = localAddress[(colon + 1)..];
            if (!string.Equals(localPortHex, targetPortHex, StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.Equals(fields[3], EstablishedState, StringComparison.OrdinalIgnoreCase)) continue;

            return long.TryParse(fields[9], out var inode) ? inode : null;
        }
        return null;
    }
}
