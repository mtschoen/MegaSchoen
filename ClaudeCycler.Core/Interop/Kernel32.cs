using System.Runtime.InteropServices;

namespace ClaudeCycler.Core.Interop;

public static partial class Kernel32
{
    public const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const int PROCESS_VM_READ = 0x0010;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(int access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool ReadProcessMemory(IntPtr hProcess, IntPtr baseAddress, void* buffer, nuint size, out nuint bytesRead);
}
