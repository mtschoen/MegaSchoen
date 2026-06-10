using System.Diagnostics;
using System.Runtime.InteropServices;
using Claude.Core.Interop;

namespace Claude.Core;

public readonly record struct CmdWindow(uint ProcessId, IntPtr WindowHandle, string WindowTitle, string? WorkingDirectory);

public readonly record struct ClaudeCliProcess(
    uint Pid,
    uint ParentPid,
    string? WorkingDirectory,
    DateTimeOffset StartTimeUtc);

public static class ProcessResolver
{
    public static List<CmdWindow> EnumerateCmdExeWindows()
    {
        var cmdPids = GetCmdExePids();
        var results = new List<CmdWindow>();

        // Modern cmd.exe instances run under ConPTY, so their HWNDs are
        // invisible PseudoConsoleWindow helpers (1x1, not user-facing).
        // The real visible terminal window is the ConPTY's ancestor — an
        // OpenConsole/conhost/WindowsTerminal window. Walk up to it.
        var seenRoots = new HashSet<IntPtr>();
        User32.EnumWindowsProc callback = (hwnd, _) =>
        {
            User32.GetWindowThreadProcessId(hwnd, out var pid);
            if (!cmdPids.Contains(pid)) return true;

            // GA_ROOTOWNER follows the owner chain (what GetParent returns for
            // top-level owned windows), so a ConPTY PseudoConsoleWindow walks
            // up to its user-facing host (WindowsTerminal / conhost / OpenConsole).
            var root = User32.GetAncestor(hwnd, User32.GA_ROOTOWNER);
            if (root == IntPtr.Zero) root = hwnd;
            if (!seenRoots.Add(root)) return true;
            if (!User32.IsWindowVisible(root)) return true;

            var title = GetWindowTitle(root);
            var cwd = TryGetProcessCwd(pid);

            results.Add(new CmdWindow(pid, root, title, cwd));
            return true;
        };

        User32.EnumWindows(callback, IntPtr.Zero);
        return results;
    }

    // Each interactive Claude CLI session is a "claude" process whose parent is a
    // shell — cmd.exe, powershell.exe, or pwsh.exe. This filter naturally
    // excludes the Claude desktop app's helper subprocesses (parented by the
    // desktop app's main process) and subagent invocations (parented by another
    // claude.exe).
    public static List<ClaudeCliProcess> EnumerateClaudeCliProcesses()
    {
        var shellPids = GetShellPids();
        var results = new List<ClaudeCliProcess>();
        foreach (var process in Process.GetProcessesByName("claude"))
        {
            using (process)
            {
                try
                {
                    var pid = (uint)process.Id;
                    var parentPid = TryGetParentPid(pid);
                    if (parentPid is null || !shellPids.Contains(parentPid.Value)) continue;
                    var cwd = TryGetProcessCwd(pid);
                    var startTime = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
                    results.Add(new ClaudeCliProcess(pid, parentPid.Value, cwd, startTime));
                }
                catch (Exception exception)
                {
                    Logger.Log($"EnumerateClaudeCliProcesses skipping pid {process.Id}: {exception.Message}");
                }
            }
        }
        return results;
    }

    // Background ("claude agents" / /bg) sessions: claude.exe workers that carry
    // --session-id on the command line, parented by a --bg-pty-host under the
    // daemon. They fail the shell-parent filter in EnumerateClaudeCliProcesses,
    // so enumerate them separately and carry the authoritative session id out.
    public static List<ClaudeCliProcess> EnumerateBackgroundClaudeSessions(out Dictionary<uint, string> sessionIdByPid)
    {
        sessionIdByPid = new Dictionary<uint, string>();
        var results = new List<ClaudeCliProcess>();
        foreach (var process in Process.GetProcessesByName("claude"))
        {
            using (process)
            {
                try
                {
                    var pid = (uint)process.Id;
                    var commandLine = TryGetProcessCommandLine(pid);
                    if (!BackgroundSessionParser.TryParseWorkerSessionId(commandLine, out var sessionId)) continue;
                    var parentPid = TryGetParentPid(pid) ?? 0;
                    var cwd = TryGetProcessCwd(pid);
                    var startTime = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
                    results.Add(new ClaudeCliProcess(pid, parentPid, cwd, startTime));
                    sessionIdByPid[pid] = sessionId;
                }
                catch (Exception exception)
                {
                    Logger.Log($"EnumerateBackgroundClaudeSessions skipping pid {process.Id}: {exception.Message}");
                }
            }
        }
        return results;
    }

    // shell pid -> visible terminal window (root owner). Lets the locator
    // attach each claude CLI process's parent shell to the user-facing terminal.
    public static Dictionary<uint, (IntPtr WindowHandle, string WindowTitle)> GetTerminalWindowsByCmdPid()
    {
        var shellPids = GetShellPids();
        var result = new Dictionary<uint, (IntPtr, string)>();
        var seenRoots = new HashSet<IntPtr>();
        User32.EnumWindowsProc callback = (hwnd, _) =>
        {
            User32.GetWindowThreadProcessId(hwnd, out var pid);
            if (!shellPids.Contains(pid)) return true;
            var root = User32.GetAncestor(hwnd, User32.GA_ROOTOWNER);
            if (root == IntPtr.Zero) root = hwnd;
            if (!seenRoots.Add(root)) return true;
            if (!User32.IsWindowVisible(root)) return true;
            result[pid] = (root, GetWindowTitle(root));
            return true;
        };
        User32.EnumWindows(callback, IntPtr.Zero);
        return result;
    }

    // Desktop-shell window classes that must never be treated as a focusable
    // host (Progman/WorkerW = desktop, Shell_TrayWnd = taskbar, etc.).
    static readonly HashSet<string> DesktopShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Button", "DV2ControlHost", "NotifyIconOverflowWindow",
    };

    // Every visible top-level (root-owner) window, keyed by owning PID. Unlike
    // GetTerminalWindowsByCmdPid this is NOT restricted to shell PIDs - it is
    // the map the ancestor resolver climbs into to find an IDE/host window.
    public static Dictionary<uint, (IntPtr Hwnd, string Title)> GetVisibleTopLevelWindowsByPid()
    {
        var result = new Dictionary<uint, (IntPtr, string)>();
        var seenRoots = new HashSet<IntPtr>();
        User32.EnumWindowsProc callback = (hwnd, _) =>
        {
            var root = User32.GetAncestor(hwnd, User32.GA_ROOTOWNER);
            if (root == IntPtr.Zero) root = hwnd;
            if (!seenRoots.Add(root)) return true;
            if (!User32.IsWindowVisible(root)) return true;
            if (DesktopShellClasses.Contains(GetWindowClass(root))) return true;
            User32.GetWindowThreadProcessId(root, out var pid);
            // First visible window per PID wins; good enough for an IDE main window.
            if (!result.ContainsKey(pid)) result[pid] = (root, GetWindowTitle(root));
            return true;
        };
        User32.EnumWindows(callback, IntPtr.Zero);
        return result;
    }

    // explorer.exe is the launcher of standalone terminals; treat it as a STOP
    // boundary for the ancestor walk so a chain that reaches it never resolves
    // to a File Explorer or desktop window.
    public static HashSet<uint> GetExplorerPids()
    {
        var pids = new HashSet<uint>();
        try
        {
            foreach (var process in Process.GetProcessesByName("explorer"))
            {
                using (process) pids.Add((uint)process.Id);
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"GetExplorerPids failed: {exception.Message}");
        }
        return pids;
    }

    static string GetWindowClass(IntPtr hwnd)
    {
        var buffer = new char[256];
        var copied = User32.GetClassName(hwnd, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : "";
    }

    public static uint? TryGetParentPid(uint pid)
    {
        var handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var pbi = default(ProcessBasicInformation);
            var status = NtDll.NtQueryInformationProcess(handle, NtDll.PROCESSBASICINFORMATION, ref pbi, Marshal.SizeOf<ProcessBasicInformation>(), out _);
            if (status != 0) return null;
            return (uint)pbi.InheritedFromUniqueProcessId.ToInt64();
        }
        catch (Exception exception)
        {
            Logger.Log($"TryGetParentPid({pid}) failed: {exception.Message}");
            return null;
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    static HashSet<uint> GetCmdExePids()
    {
        var pids = new HashSet<uint>();
        try
        {
            foreach (var process in Process.GetProcessesByName("cmd"))
            {
                using (process)
                {
                    pids.Add((uint)process.Id);
                }
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"GetCmdExePids failed: {exception.Message}");
        }
        return pids;
    }

    static readonly string[] ShellProcessNames = { "cmd", "powershell", "pwsh" };

    static HashSet<uint> GetShellPids()
    {
        var pids = new HashSet<uint>();
        foreach (var name in ShellProcessNames)
        {
            try
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        pids.Add((uint)process.Id);
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.Log($"GetShellPids({name}) failed: {exception.Message}");
            }
        }
        return pids;
    }

    static string GetWindowTitle(IntPtr hwnd)
    {
        var length = User32.GetWindowTextLengthW(hwnd);
        if (length <= 0) return "";
        var buffer = new char[length + 1];
        var copied = User32.GetWindowText(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, copied);
    }

    static unsafe string? TryGetProcessCwd(uint pid)
    {
        var handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION | Kernel32.PROCESS_VM_READ, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var pbi = default(ProcessBasicInformation);
            var status = NtDll.NtQueryInformationProcess(handle, NtDll.PROCESSBASICINFORMATION, ref pbi, Marshal.SizeOf<ProcessBasicInformation>(), out _);
            if (status != 0 || pbi.PebBaseAddress == IntPtr.Zero) return null;

            // Offsets for 64-bit PEB:
            //   PEB + 0x20  â†’ ProcessParameters (PTR)
            //   ProcessParameters + 0x38 â†’ CurrentDirectory.DosPath (UNICODE_STRING)
            //   UNICODE_STRING: USHORT Length; USHORT MaximumLength; PWSTR Buffer;
            //     Length at +0x00, Buffer at +0x08 (64-bit).

            IntPtr processParameters;
            if (!Kernel32.ReadProcessMemory(handle, pbi.PebBaseAddress + 0x20, &processParameters, (nuint)IntPtr.Size, out _))
                return null;

            ushort length;
            if (!Kernel32.ReadProcessMemory(handle, processParameters + 0x38, &length, sizeof(ushort), out _))
                return null;
            if (length == 0 || length > 4096) return null;

            IntPtr bufferPtr;
            if (!Kernel32.ReadProcessMemory(handle, processParameters + 0x38 + 8, &bufferPtr, (nuint)IntPtr.Size, out _))
                return null;
            if (bufferPtr == IntPtr.Zero) return null;

            var chars = new char[length / 2];
            fixed (char* dest = chars)
            {
                if (!Kernel32.ReadProcessMemory(handle, bufferPtr, dest, length, out _))
                    return null;
            }
            return new string(chars).TrimEnd('\\');
        }
        catch (Exception exception)
        {
            Logger.Log($"TryGetProcessCwd({pid}) failed: {exception.Message}");
            return null;
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }

    // Reads a process's command line from its PEB. Mirrors TryGetProcessCwd but
    // reads RTL_USER_PROCESS_PARAMETERS.CommandLine (UNICODE_STRING at
    // ProcessParameters + 0x70 on x64: Length at +0x00, Buffer at +0x08).
    // Best-effort: returns null on any failure.
    static unsafe string? TryGetProcessCommandLine(uint pid)
    {
        var handle = Kernel32.OpenProcess(Kernel32.PROCESS_QUERY_LIMITED_INFORMATION | Kernel32.PROCESS_VM_READ, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var pbi = default(ProcessBasicInformation);
            var status = NtDll.NtQueryInformationProcess(handle, NtDll.PROCESSBASICINFORMATION, ref pbi, Marshal.SizeOf<ProcessBasicInformation>(), out _);
            if (status != 0 || pbi.PebBaseAddress == IntPtr.Zero) return null;

            IntPtr processParameters;
            if (!Kernel32.ReadProcessMemory(handle, pbi.PebBaseAddress + 0x20, &processParameters, (nuint)IntPtr.Size, out _))
                return null;

            ushort length;
            if (!Kernel32.ReadProcessMemory(handle, processParameters + 0x70, &length, sizeof(ushort), out _))
                return null;
            if (length == 0 || length > 8192) return null;

            IntPtr bufferPtr;
            if (!Kernel32.ReadProcessMemory(handle, processParameters + 0x70 + 8, &bufferPtr, (nuint)IntPtr.Size, out _))
                return null;
            if (bufferPtr == IntPtr.Zero) return null;

            var chars = new char[length / 2];
            fixed (char* dest = chars)
            {
                if (!Kernel32.ReadProcessMemory(handle, bufferPtr, dest, length, out _))
                    return null;
            }
            return new string(chars);
        }
        catch (Exception exception)
        {
            Logger.Log($"TryGetProcessCommandLine({pid}) failed: {exception.Message}");
            return null;
        }
        finally
        {
            Kernel32.CloseHandle(handle);
        }
    }
}
