using System.Runtime.InteropServices;

namespace ClaudeCycler.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct ProcessBasicInformation
{
    public int ExitStatus;
    public IntPtr PebBaseAddress;
    public IntPtr AffinityMask;
    public int BasePriority;
    public IntPtr UniqueProcessId;
    public IntPtr InheritedFromUniqueProcessId;
}

public static partial class NtDll
{
    public const int PROCESSBASICINFORMATION = 0;

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(IntPtr processHandle, int infoClass, ref ProcessBasicInformation info, int size, out int returnLength);
}
