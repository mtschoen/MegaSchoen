using System.Diagnostics;
using System.Globalization;
using System.Text;
using Claude.Core.Models;

namespace Claude.Core.Linux;

// Focuses the terminal window hosting a claude process on KDE Plasma via
// KWin's DBus scripting interface (org.kde.KWin /Scripting): Wayland has no
// cross-client "activate that window" protocol, but a loaded KWin script may
// assign workspace.activeWindow. On Linux the WindowToken carries the claude
// PID (stamped by LinuxClaudeProcessLocator), not a native window handle; the
// claude process itself never owns a window, so the focuser climbs the /proc
// ancestor chain (claude -> shell -> terminal emulator) and the generated
// script activates the first window owned by the nearest ancestor.
// Rig-verified on SteamOS Plasma 6 (2026-07-19).
public sealed class LinuxClaudeWindowFocuser : IClaudeWindowFocuser
{
    const int MaximumAncestorDepth = 32;

    readonly IProcFileSystem _proc;
    readonly Func<string[], string?> _runQdbus;

    public LinuxClaudeWindowFocuser() : this(new ProcFileSystem(), RunQdbusProcess) { }

    // Test seam: qdbus invocations are injected. The delegate receives the
    // qdbus arguments and returns trimmed stdout, or null on failure.
    public LinuxClaudeWindowFocuser(IProcFileSystem proc, Func<string[], string?> runQdbus)
    {
        _proc = proc;
        _runQdbus = runQdbus;
    }

    public bool BringToFront(WindowToken window)
    {
        var pid = (int)window.Handle;
        if (pid <= 0) return false;

        var script = BuildActivationScript(AncestorChain(pid, _proc.ReadParentPid, MaximumAncestorDepth));
        var scriptPath = Path.Combine(
            Path.GetTempPath(), $"megaschoen-focus-{Environment.ProcessId}-{pid}.js");
        try
        {
            File.WriteAllText(scriptPath, script);
            return RunKwinScript(scriptPath, pluginName: $"megaschoen-focus-{pid}");
        }
        catch (Exception exception)
        {
            Logger.Log($"LinuxClaudeWindowFocuser.BringToFront({pid}) failed: {exception}");
            return false;
        }
        finally
        {
            try
            {
                File.Delete(scriptPath);
            }
            catch (IOException exception)
            {
                // Best-effort temp cleanup; a leaked script file is harmless.
                Logger.Log($"LinuxClaudeWindowFocuser: temp script cleanup failed: {exception.Message}");
            }
        }
    }

    // Pure: nearest ancestor first, so the generated script prefers the window
    // closest to the claude process (its own terminal over an outer IDE).
    public static IReadOnlyList<int> AncestorChain(int pid, Func<int, int?> parentOf, int maximumDepth)
    {
        var chain = new List<int>();
        var seen = new HashSet<int>();
        var current = pid;
        while (current > 1 && seen.Add(current) && chain.Count < maximumDepth)
        {
            chain.Add(current);
            if (parentOf(current) is not { } parent) break;
            current = parent;
        }
        return chain;
    }

    // Pure: KWin script that activates the first window owned by the earliest
    // pid in the list (list order wins over window order).
    public static string BuildActivationScript(IReadOnlyList<int> pids)
    {
        var list = string.Join(", ", pids.Select(p => p.ToString(CultureInfo.InvariantCulture)));
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"var pids = [{list}];");
        builder.AppendLine("var windows = workspace.windowList();");
        builder.AppendLine("var found = false;");
        builder.AppendLine("for (var p = 0; p < pids.length && !found; p++) {");
        builder.AppendLine("    for (var i = 0; i < windows.length && !found; i++) {");
        builder.AppendLine("        if (windows[i].pid === pids[p]) {");
        builder.AppendLine("            workspace.activeWindow = windows[i];");
        builder.AppendLine("            found = true;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    bool RunKwinScript(string scriptPath, string pluginName)
    {
        // A stale plugin with this name (e.g. a previous crashed run) makes
        // loadScript return -1, so clear it first; failure here is fine.
        _runQdbus(["org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.unloadScript", pluginName]);

        var loadOutput = _runQdbus(
            ["org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.loadScript", scriptPath, pluginName]);
        if (loadOutput is null || !int.TryParse(loadOutput, out var scriptId) || scriptId < 0)
        {
            Logger.Log($"LinuxClaudeWindowFocuser: loadScript failed (output: '{loadOutput}')");
            return false;
        }

        try
        {
            return _runQdbus(["org.kde.KWin", $"/Scripting/Script{scriptId}", "org.kde.kwin.Script.run"]) is not null;
        }
        finally
        {
            _runQdbus(["org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.unloadScript", pluginName]);
        }
    }

    static string? RunQdbusProcess(string[] arguments)
    {
        // Plasma 6 ships qdbus6; older stacks ship qdbus. Both speak the same CLI.
        foreach (var executable in (string[])["qdbus6", "qdbus"])
        {
            try
            {
                var startInfo = new ProcessStartInfo(executable)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
                using var process = Process.Start(startInfo);
                if (process is null) continue;
                var output = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
                {
                    process.Kill(entireProcessTree: true);
                    return null;
                }
                return process.ExitCode == 0 ? output.Trim() : null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Executable not on PATH; try the next candidate.
            }
        }
        return null;
    }
}
