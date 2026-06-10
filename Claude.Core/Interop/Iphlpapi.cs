// Claude.Core/Interop/Iphlpapi.cs
using System.Runtime.InteropServices;

namespace Claude.Core.Interop;

static partial class Iphlpapi
{
    public const int AF_INET = 2;
    public const int AF_INET6 = 23;
    public const int TCP_TABLE_OWNER_PID_ALL = 5;
    public const uint MIB_TCP_STATE_ESTAB = 5;

    // Rows (MIB_TCPROW_OWNER_PID / MIB_TCP6ROW_OWNER_PID) are read by byte offset
    // in TcpConnectionTable rather than marshalled as structs, so the IPv6 row's
    // 16-byte address fields need no array-marshalling here.

    [LibraryImport("iphlpapi.dll")]
    public static partial uint GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int pdwSize,
        [MarshalAs(UnmanagedType.Bool)] bool bOrder,
        int ulAf,
        int tableClass,
        int reserved);
}
