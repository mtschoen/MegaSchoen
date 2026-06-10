// Claude.Core/Windows/TcpConnectionTable.cs
using System.Runtime.InteropServices;
using Claude.Core.Interop;

namespace Claude.Core.Windows;

public static class TcpConnectionTable
{
    // Returns the PID owning the ESTABLISHED TCP connection whose LOCAL (source)
    // port equals localPort, or null. The local source port of an outbound
    // ssh.exe connection is exactly the SSH_CONNECTION client port the remote
    // reported, so this maps a remote session to its local ssh.exe. BOTH the
    // IPv4 and IPv6 owner-pid tables are checked: ssh to a link-local host
    // (fe80::) runs over IPv6, so an IPv4-only query would miss it.
    public static uint? TryGetOwningPidForLocalPort(int localPort)
    {
        // Row layouts, byte offsets within each MIB_*ROW_OWNER_PID:
        //   IPv4 (24 bytes): State @0, LocalAddr @4, LocalPort @8, RemoteAddr @12,
        //                    RemotePort @16, OwningPid @20
        //   IPv6 (56 bytes): LocalAddr[16] @0, LocalScopeId @16, LocalPort @20,
        //                    RemoteAddr[16] @24, RemoteScopeId @40, RemotePort @44,
        //                    State @48, OwningPid @52
        return QueryTable(Iphlpapi.AF_INET, localPort,
                   rowSize: 24, stateOffset: 0, localPortOffset: 8, owningPidOffset: 20)
            ?? QueryTable(Iphlpapi.AF_INET6, localPort,
                   rowSize: 56, stateOffset: 48, localPortOffset: 20, owningPidOffset: 52);
    }

    static uint? QueryTable(
        int addressFamily, int localPort,
        int rowSize, int stateOffset, int localPortOffset, int owningPidOffset)
    {
        var size = 0;
        // Size probe: returns ERROR_INSUFFICIENT_BUFFER by design; size is
        // populated even on that failure, so the return value is irrelevant.
        // TCP_TABLE_OWNER_PID_ALL (5) has the same value in both the
        // TCP_TABLE_CLASS (IPv4) and TCP6_TABLE_CLASS (IPv6) SDK enums.
        Iphlpapi.GetExtendedTcpTable(IntPtr.Zero, ref size, false,
            addressFamily, Iphlpapi.TCP_TABLE_OWNER_PID_ALL, 0);
        if (size <= 0) return null;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (Iphlpapi.GetExtendedTcpTable(buffer, ref size, false,
                    addressFamily, Iphlpapi.TCP_TABLE_OWNER_PID_ALL, 0) != 0)
            {
                return null;
            }

            var count = Marshal.ReadInt32(buffer);   // dwNumEntries; rows follow it
            var rowsBase = buffer + 4;
            for (var i = 0; i < count; i++)
            {
                var row = rowsBase + i * rowSize;
                if ((uint)Marshal.ReadInt32(row + stateOffset) != Iphlpapi.MIB_TCP_STATE_ESTAB) continue;
                // LocalPort is network byte order in the low two bytes.
                var raw = (uint)Marshal.ReadInt32(row + localPortOffset);
                var port = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
                if (port == localPort) return (uint)Marshal.ReadInt32(row + owningPidOffset);
            }
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
