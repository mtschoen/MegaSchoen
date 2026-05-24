# Remote Claude Session Detection Implementation Plan (scope A)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface Claude Code sessions running on a remote SSH host (llamabox) in the existing MegaSchoen Sessions dashboard with full live state (Working / Idle / PendingPermission / AwaitingInput), cwd, and last-activity — listing only; **Focus for remote sessions is out of scope** (deliberate fast-follow).

**Architecture:** A remote hook bridge captures attention-state into a remote `StateStore`; the existing `ActiveSessionEnumerator` runs *on* the remote (wired to a new `LinuxClaudeProcessLocator` that reads `/proc`) and streams `SessionSnapshot` NDJSON over a single persistent SSH connection. The Windows app spawns one `ssh … claude-sessions list --json-stream` child per configured host, reads the NDJSON, and merges remote sessions into the same live UI collection. Windows OpenSSH lacks `ControlMaster`, so a held-open streaming connection (not repeat-polling) is the transport.

**Tech Stack:** .NET 10 multi-targeted (`net10.0;net10.0-windows10.0.26100.0`), existing `Claude.Core` / `ClaudeHookBridge` / `ClaudeSessionsCLI`, system `ssh.exe` (OpenSSH), MAUI. No new NuGet dependencies.

**Spike grounding (already validated):**
- `~/.claude/notes/spike_claude-core-linux-build.md` — .NET 10 SDK present on llamabox; `Claude.Core` compiles clean for `net10.0`; Windows coupling isolated to 4 files; `ClaudeHookBridge` + `Paths.cs` portable with zero code change.
- `/proc` probe (2026-05-24): claude shows as `comm=claude`; match on `comm` exactly; start-time = `/proc/stat btime` + `/proc/<pid>/stat` field-22 / `CLK_TCK`(100).

---

## Phase 1: Multi-target the shared projects

Goal of phase: the three shared projects build for both `net10.0` and the Windows TFM; nothing in the Windows-only paths is compiled into the `net10.0` output. (Validated by the spike; this makes it real.)

### Task 1.1: Multi-target `Claude.Core`, exclude Windows-only files from `net10.0`

**Files:**
- Modify: `Claude.Core/Claude.Core.csproj`

- [x] **Step 1: Edit the csproj to multi-target and condition out Windows files**

Replace the contents of `Claude.Core/Claude.Core.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net10.0;net10.0-windows10.0.26100.0</TargetFrameworks>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>

  <!-- Windows-only: WMI process enumeration + Win32 P/Invoke + Win32 impls.
       Excluded from the net10.0 (Linux) target; the Linux locator replaces them. -->
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
    <Compile Remove="ProcessResolver.cs" />
    <Compile Remove="Interop/**/*.cs" />
    <Compile Remove="Windows/**/*.cs" />
  </ItemGroup>

  <!-- Linux-only: /proc-based locator. Excluded from the Windows target. -->
  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows10.0.26100.0'">
    <Compile Remove="Linux/**/*.cs" />
  </ItemGroup>
</Project>
```

- [x] **Step 2: Build the Linux target to verify it compiles**

Run: `dotnet build Claude.Core/Claude.Core.csproj -f net10.0 -v minimal`
Expected: `Build succeeded. 0 Error(s)` (the `Linux/` folder is empty until Phase 2 — that is fine, the Remove glob matches nothing).

- [x] **Step 3: Build the Windows target to verify no regression**

Run: `dotnet build Claude.Core/Claude.Core.csproj -f net10.0-windows10.0.26100.0 -v minimal`
Expected: `Build succeeded. 0 Error(s)`.

- [x] **Step 4: Commit**

```bash
git add Claude.Core/Claude.Core.csproj
git commit -m "build(core): multi-target Claude.Core for net10.0 (Linux), isolate Win32 files"
```

### Task 1.2: Multi-target `ClaudeHookBridge` and `ClaudeSessionsCLI`

**Files:**
- Modify: `ClaudeHookBridge/ClaudeHookBridge.csproj`
- Modify: `ClaudeSessionsCLI/ClaudeSessionsCLI.csproj`

- [x] **Step 1: Edit `ClaudeHookBridge.csproj` TargetFramework → TargetFrameworks**

Change the single line:
```xml
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
```
to:
```xml
<TargetFrameworks>net10.0;net10.0-windows10.0.26100.0</TargetFrameworks>
```
(Leave the rest of the file unchanged. The bridge references none of the Windows-only files.)

- [x] **Step 2: Edit `ClaudeSessionsCLI.csproj` the same way**

Change:
```xml
<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
```
to:
```xml
<TargetFrameworks>net10.0;net10.0-windows10.0.26100.0</TargetFrameworks>
```
(Spectre.Console 0.49.1 is cross-platform; no other change needed.)

- [x] **Step 3: Build both for the Linux target**

Run:
```bash
dotnet build ClaudeHookBridge/ClaudeHookBridge.csproj -f net10.0 -v minimal
dotnet build ClaudeSessionsCLI/ClaudeSessionsCLI.csproj -f net10.0 -v minimal
```
Expected: both `Build succeeded. 0 Error(s)`.

> Note: `MegaSchoen.csproj` references `Claude.Core` only under the Windows TFM (existing condition). Multi-targeting `Claude.Core` does not change that — the MAUI build resolves the `net10.0-windows…` asset automatically. No change to `MegaSchoen.csproj` is required; do not add one.

- [x] **Step 4: Commit**

```bash
git add ClaudeHookBridge/ClaudeHookBridge.csproj ClaudeSessionsCLI/ClaudeSessionsCLI.csproj
git commit -m "build: multi-target hook bridge + sessions CLI for net10.0"
```

---

## Phase 2: Linux process locator

Goal of phase: a `net10.0` `IClaudeProcessLocator` that finds live `claude` processes from `/proc`, unit-testable on the Windows CI host (no real `/proc`) via a small filesystem abstraction.

### Task 2.1: `IProcFileSystem` abstraction + real impl

**Files:**
- Create: `Claude.Core/Linux/IProcFileSystem.cs`
- Create: `Claude.Core/Linux/ProcFileSystem.cs`

- [x] **Step 1: Write the failing test for the real `/proc` reader shape**

Create `Claude.Core.Tests/Linux/ProcFileSystemTests.cs`:

```csharp
using Claude.Core.Linux;

namespace Claude.Core.Tests.Linux;

[TestClass]
public class ProcFileSystemTests
{
    [TestMethod]
    public void BootTimeEpochSeconds_ParsesBtimeLine()
    {
        var sut = new ProcFileSystem(statContents: "cpu  1 2 3\nbtime 1779252757\nprocesses 99\n");
        Assert.AreEqual(1779252757L, sut.BootTimeEpochSeconds);
    }
}
```

- [x] **Step 2: Run it — expect compile failure (types absent)**

Run: `dotnet test Claude.Core.Tests -f net10.0 --filter ProcFileSystemTests`
Expected: build error (`ProcFileSystem` not found).

- [x] **Step 3: Define the interface**

Create `Claude.Core/Linux/IProcFileSystem.cs`:

```csharp
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
```

- [x] **Step 4: Implement the real reader**

Create `Claude.Core/Linux/ProcFileSystem.cs`:

```csharp
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
        BootTimeEpochSeconds = ParseBtime(statContents ?? SafeReadAllText(Path.Combine(_root, "stat")));
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

    public string? ReadComm(int pid) => SafeReadAllText(Path.Combine(_root, pid.ToString(), "comm"))?.Trim();

    public string? ReadCwd(int pid)
    {
        try { return new FileInfo(Path.Combine(_root, pid.ToString(), "cwd")).LinkTarget; }
        catch { return null; }
    }

    public long? ReadStartTicks(int pid)
    {
        var stat = SafeReadAllText(Path.Combine(_root, pid.ToString(), "stat"));
        if (stat is null) return null;
        // comm (field 2) may contain spaces/parens; it is wrapped in (), so split after the last ')'.
        var close = stat.LastIndexOf(')');
        if (close < 0) return null;
        var rest = stat[(close + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // After comm, fields are 3..; field 22 is index (22 - 3) = 19 in this tail array.
        return rest.Length > 19 && long.TryParse(rest[19], out var ticks) ? ticks : null;
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
```

- [x] **Step 5: Run the test — expect PASS**

Run: `dotnet test Claude.Core.Tests -f net10.0 --filter ProcFileSystemTests`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add Claude.Core/Linux/IProcFileSystem.cs Claude.Core/Linux/ProcFileSystem.cs Claude.Core.Tests/Linux/ProcFileSystemTests.cs
git commit -m "feat(linux): /proc filesystem abstraction with btime + stat parsing"
```

### Task 2.2: `LinuxClaudeProcessLocator`

**Files:**
- Create: `Claude.Core/Linux/LinuxClaudeProcessLocator.cs`
- Create: `Claude.Core.Tests/Linux/LinuxClaudeProcessLocatorTests.cs`
- Reference: `Claude.Core/Models/ClaudeWindow.cs`, `Claude.Core/Windows/WindowsClaudeProcessLocator.cs` (mirror its output shape)

- [x] **Step 1: Write the failing test with a fake `/proc`**

Create `Claude.Core.Tests/Linux/LinuxClaudeProcessLocatorTests.cs`:

```csharp
using Claude.Core.Linux;
using Claude.Core.Models;

namespace Claude.Core.Tests.Linux;

[TestClass]
public class LinuxClaudeProcessLocatorTests
{
    sealed class FakeProc : IProcFileSystem
    {
        public long BootTimeEpochSeconds => 1_000_000;
        public long ClockTicksPerSecond => 100;
        public Dictionary<int, (string comm, string? cwd, long ticks)> Procs = new();
        public IEnumerable<int> EnumeratePids() => Procs.Keys;
        public string? ReadComm(int pid) => Procs.TryGetValue(pid, out var p) ? p.comm : null;
        public string? ReadCwd(int pid) => Procs.TryGetValue(pid, out var p) ? p.cwd : null;
        public long? ReadStartTicks(int pid) => Procs.TryGetValue(pid, out var p) ? p.ticks : null;
    }

    [TestMethod]
    public void EnumerateWindows_ReturnsOnlyCommClaude_WithCwdAndStartTime()
    {
        var fake = new FakeProc();
        fake.Procs[2572000] = ("claude", "/home/schoen/pr-crew", 200);          // start = 1_000_000 + 2s
        fake.Procs[2680058] = ("bash", "/home/schoen/git-wizard", 300);          // child shell — must be excluded
        fake.Procs[999]     = ("claude", null, 100);                             // no cwd — must be excluded

        var sut = new LinuxClaudeProcessLocator(fake);
        var windows = sut.EnumerateWindows();

        Assert.AreEqual(1, windows.Count);
        var w = windows[0];
        Assert.AreEqual("/home/schoen/pr-crew", w.WorkingDirectory);
        Assert.AreEqual(2572000, w.ProcessId);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1_000_002), w.StartTimeUtc);
        Assert.IsTrue(w.Window.IsNull);   // no window on the remote (scope A)
    }
}
```

- [x] **Step 2: Run it — expect failure (`LinuxClaudeProcessLocator` absent)**

Run: `dotnet test Claude.Core.Tests -f net10.0 --filter LinuxClaudeProcessLocatorTests`
Expected: build error.

- [x] **Step 3: Check `WindowToken` exposes a null sentinel; add if missing**

Open `Claude.Core/Models/WindowToken.cs`. It must support a null/empty token and an `IsNull` predicate. If absent, add:

```csharp
public static WindowToken Null { get; } = new(IntPtr.Zero);
public bool IsNull => Handle == IntPtr.Zero;
```
(Match the existing field/property name for the handle; `FromHandle` already exists per `WindowsClaudeProcessLocator`.)

- [x] **Step 4: Implement the locator**

Create `Claude.Core/Linux/LinuxClaudeProcessLocator.cs`:

```csharp
using Claude.Core.Models;

namespace Claude.Core.Linux;

public sealed class LinuxClaudeProcessLocator : IClaudeProcessLocator
{
    readonly IProcFileSystem _proc;

    public LinuxClaudeProcessLocator() : this(new ProcFileSystem()) { }
    public LinuxClaudeProcessLocator(IProcFileSystem proc) => _proc = proc;

    public IReadOnlyList<ClaudeWindow> EnumerateWindows()
    {
        var result = new List<ClaudeWindow>();
        foreach (var pid in _proc.EnumeratePids())
        {
            // Match on comm ONLY. The args of child shells (e.g. CI-poll one-liners)
            // can contain "claude" paths and would false-positive on an args match.
            if (!string.Equals(_proc.ReadComm(pid), "claude", StringComparison.Ordinal)) continue;

            var cwd = _proc.ReadCwd(pid);
            if (string.IsNullOrEmpty(cwd)) continue;             // need a cwd to map to a slug

            var ticks = _proc.ReadStartTicks(pid);
            if (ticks is null) continue;

            var startEpoch = _proc.BootTimeEpochSeconds + (ticks.Value / _proc.ClockTicksPerSecond);
            result.Add(new ClaudeWindow(
                ProcessId: pid,
                Window: WindowToken.Null,
                Title: null,
                WorkingDirectory: cwd,
                StartTimeUtc: DateTimeOffset.FromUnixTimeSeconds(startEpoch)));
        }
        return result;
    }
}
```

> If `ClaudeWindow.Title` is non-nullable, pass `string.Empty`. Verify against `Claude.Core/Models/ClaudeWindow.cs` and match its constructor exactly.

- [x] **Step 5: Run the test — expect PASS**

Run: `dotnet test Claude.Core.Tests -f net10.0 --filter LinuxClaudeProcessLocatorTests`
Expected: PASS.

- [x] **Step 6: Commit**

```bash
git add Claude.Core/Linux/LinuxClaudeProcessLocator.cs Claude.Core/Models/WindowToken.cs Claude.Core.Tests/Linux/LinuxClaudeProcessLocatorTests.cs
git commit -m "feat(linux): LinuxClaudeProcessLocator reading /proc (comm-match, windowless)"
```

### Task 2.3: Wire the locator into the CLI's enumerator selection

**Files:**
- Modify: `ClaudeSessionsCLI/Commands/ListCommand.cs` (locator construction)
- Reference: `Claude.Core/ActiveSessionEnumerator.cs` (unchanged)

- [x] **Step 1: Select the locator by OS at the CLI composition point**

In `ListCommand.cs`, where the `IClaudeProcessLocator` is constructed for the enumerator, replace the direct `new WindowsClaudeProcessLocator()` with an OS switch:

```csharp
IClaudeProcessLocator locator = OperatingSystem.IsWindows()
    ? new Claude.Core.Windows.WindowsClaudeProcessLocator()
    : new Claude.Core.Linux.LinuxClaudeProcessLocator();
```

> `WindowsClaudeProcessLocator` only exists in the Windows TFM. Because the CLI is multi-targeted, guard the Windows branch so the `net10.0` build compiles: wrap the Windows construction in `#if WINDOWS`-equivalent. .NET sets the `WINDOWS` symbol automatically for the `net10.0-windows` TFM. Use:
> ```csharp
> #if WINDOWS
>         IClaudeProcessLocator locator = new Claude.Core.Windows.WindowsClaudeProcessLocator();
> #else
>         IClaudeProcessLocator locator = new Claude.Core.Linux.LinuxClaudeProcessLocator();
> #endif
> ```

- [x] **Step 2: Build both targets**

Run:
```bash
dotnet build ClaudeSessionsCLI/ClaudeSessionsCLI.csproj -f net10.0 -v minimal
dotnet build ClaudeSessionsCLI/ClaudeSessionsCLI.csproj -f net10.0-windows10.0.26100.0 -v minimal
```
Expected: both succeed.

- [x] **Step 3: Commit**

```bash
git add ClaudeSessionsCLI/Commands/ListCommand.cs
git commit -m "feat(cli): select Linux vs Windows process locator by OS"
```

---

## Phase 3: Remote install + on-box verification

Goal of phase: a one-command remote setup that builds the Linux binaries, installs the hook set on the remote, and a verification that `claude-sessions list --json-stream` reports live remote sessions on llamabox. **These steps run against llamabox over SSH; they are deploy steps, not CI.**

### Task 3.1: Add an `install` verb to `ClaudeHookBridge`

**Files:**
- Modify: `ClaudeHookBridge/Program.cs` (add `"install"` case)
- Create: `ClaudeHookBridge/Commands/InstallCommand.cs`
- Reference: `Claude.Core/SettingsJsonInstaller.cs`

- [x] **Step 1: Write `InstallCommand`**

Create `ClaudeHookBridge/Commands/InstallCommand.cs`:

```csharp
using Claude.Core;

namespace ClaudeHookBridge.Commands;

static class InstallCommand
{
    public static int Run()
    {
        var selfPath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(selfPath))
        {
            Console.Error.WriteLine("install: could not resolve own executable path");
            return 1;
        }
        new SettingsJsonInstaller().Install(selfPath);
        Console.WriteLine($"install: hooks pointing at {selfPath} written to {Paths.ClaudeSettingsFile}");
        return 0;
    }
}
```

- [x] **Step 2: Register the verb in `Program.cs`**

In the `arguments[0] switch` block, add:
```csharp
"install" => Commands.InstallCommand.Run(),
```
and add `install` to the `PrintUnknownCommand` "Available:" list.

- [x] **Step 3: Build both targets, commit**

Run: `dotnet build ClaudeHookBridge/ClaudeHookBridge.csproj -f net10.0 -v minimal`
Expected: success.

```bash
git add ClaudeHookBridge/Commands/InstallCommand.cs ClaudeHookBridge/Program.cs
git commit -m "feat(bridge): add 'install' verb to self-register hooks via SettingsJsonInstaller"
```

### Task 3.2: Remote setup script

**Files:**
- Create: `scripts/setup-remote.sh`

- [x] **Step 1: Write the script**

Create `scripts/setup-remote.sh`:

```bash
#!/usr/bin/env bash
# Build the Linux session binaries on this (remote) host, install the Claude
# hook set, and expose `claude-sessions` on PATH. Run ON the remote host:
#   ssh <target> 'bash -s' < scripts/setup-remote.sh
# or from a checkout on the remote: ./scripts/setup-remote.sh
set -euo pipefail

REPO="${MEGASCHOEN_REPO:-$HOME/MegaSchoen}"
BIN="$HOME/.local/bin"
PUB="$HOME/.local/share/MegaSchoen/bin"
mkdir -p "$BIN" "$PUB"

cd "$REPO"
git pull --ff-only

dotnet publish ClaudeHookBridge/ClaudeHookBridge.csproj   -f net10.0 -c Release -r linux-x64 --self-contained false -o "$PUB/hookbridge"
dotnet publish ClaudeSessionsCLI/ClaudeSessionsCLI.csproj -f net10.0 -c Release -r linux-x64 --self-contained false -o "$PUB/sessions"

ln -sf "$PUB/sessions/ClaudeSessionsCLI" "$BIN/claude-sessions"

# Register the hook set (Notification/Stop/UserPromptSubmit/SessionEnd/PostToolUse)
"$PUB/hookbridge/ClaudeHookBridge" install

echo "Done. Ensure $BIN is on PATH for non-interactive SSH (e.g. add to ~/.bashrc / ~/.profile)."
echo "Verify: claude-sessions list --json"
```

- [x] **Step 2: Run the setup against llamabox**

Run: `ssh schoen@llamabox 'bash -s' < scripts/setup-remote.sh`
Expected: two `dotnet publish` successes, a symlink line, and `install: hooks … written to /home/schoen/.claude/settings.json`.

- [x] **Step 3: Verify enumeration on the remote (one-shot JSON)**

Run: `ssh schoen@llamabox 'claude-sessions list --json'`
Expected: a JSON array including the live sessions with Linux cwds (e.g. `/home/schoen/pr-crew`), correct `State`, and `SessionId` matching `~/.claude/projects/-home-schoen-*/*.jsonl`. If empty but sessions exist, check PATH for non-interactive SSH and that `comm == claude` (Task 2.2).

- [x] **Step 4: Verify the stream mode emits on change**

Run: `ssh schoen@llamabox 'claude-sessions list --json-stream --interval 2'` and observe one NDJSON object per snapshot; Ctrl-C to stop.
Expected: at least one NDJSON line; new lines as remote session state changes.

- [x] **Step 5: Commit the script**

```bash
git add scripts/setup-remote.sh
git commit -m "feat(deploy): scripts/setup-remote.sh to build + install remote session reporter"
```

---

## Phase 4: Windows-side host config + SSH stream reader

Goal of phase: the Windows app can read a configured host list and maintain a held-open NDJSON stream per host, surfacing snapshots as events with reconnect-on-drop — all unit-testable without a real SSH connection.

### Task 4.1: `RemoteHostConfig` model + loader

**Files:**
- Create: `Claude.Core/Remote/RemoteHostConfig.cs`
- Create: `Claude.Core.Tests/Remote/RemoteHostConfigTests.cs`

- [x] **Step 1: Failing test**

Create `Claude.Core.Tests/Remote/RemoteHostConfigTests.cs`:

```csharp
using Claude.Core.Remote;

namespace Claude.Core.Tests.Remote;

[TestClass]
public class RemoteHostConfigTests
{
    [TestMethod]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var hosts = RemoteHostConfig.Load(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.json"));
        Assert.AreEqual(0, hosts.Count);
    }

    [TestMethod]
    public void Load_ParsesHosts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hosts-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """[{ "name": "llamabox", "sshTarget": "schoen@llamabox" }]""");
        try
        {
            var hosts = RemoteHostConfig.Load(path);
            Assert.AreEqual(1, hosts.Count);
            Assert.AreEqual("llamabox", hosts[0].Name);
            Assert.AreEqual("schoen@llamabox", hosts[0].SshTarget);
            Assert.AreEqual("claude-sessions", hosts[0].RemoteCli);   // default
        }
        finally { File.Delete(path); }
    }
}
```

- [x] **Step 2: Run — expect failure.** `dotnet test Claude.Core.Tests -f net10.0 --filter RemoteHostConfigTests` → build error.

- [x] **Step 3: Implement**

Create `Claude.Core/Remote/RemoteHostConfig.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claude.Core.Remote;

public sealed class RemoteHostConfig
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("sshTarget")] public string SshTarget { get; set; } = "";
    [JsonPropertyName("remoteCli")] public string RemoteCli { get; set; } = "claude-sessions";

    public static string DefaultPath =>
        Path.Combine(Paths.AppDataDirectory, "remote-hosts.json");

    public static IReadOnlyList<RemoteHostConfig> Load() => Load(DefaultPath);

    public static IReadOnlyList<RemoteHostConfig> Load(string path)
    {
        if (!File.Exists(path)) return Array.Empty<RemoteHostConfig>();
        try
        {
            var hosts = JsonSerializer.Deserialize<List<RemoteHostConfig>>(File.ReadAllText(path));
            return hosts?.Where(h => !string.IsNullOrWhiteSpace(h.SshTarget)).ToList()
                   ?? (IReadOnlyList<RemoteHostConfig>)Array.Empty<RemoteHostConfig>();
        }
        catch { return Array.Empty<RemoteHostConfig>(); }
    }
}
```

- [x] **Step 4: Run — expect PASS.** Commit:
```bash
git add Claude.Core/Remote/RemoteHostConfig.cs Claude.Core.Tests/Remote/RemoteHostConfigTests.cs
git commit -m "feat(remote): RemoteHostConfig model + loader (absent file = dormant)"
```

### Task 4.2: `RemoteSessionStreamClient`

**Files:**
- Create: `Claude.Core/Remote/IStreamProcess.cs` (process seam)
- Create: `Claude.Core/Remote/SshStreamProcess.cs` (real `ssh.exe` impl)
- Create: `Claude.Core/Remote/RemoteSessionStreamClient.cs`
- Create: `Claude.Core.Tests/Remote/RemoteSessionStreamClientTests.cs`
- Reference: `Claude.Core/Models/SessionSnapshot.cs`

- [x] **Step 1: Failing test with a fake process emitting canned NDJSON**

Create `Claude.Core.Tests/Remote/RemoteSessionStreamClientTests.cs`:

```csharp
using System.Threading.Channels;
using Claude.Core.Models;
using Claude.Core.Remote;

namespace Claude.Core.Tests.Remote;

[TestClass]
public class RemoteSessionStreamClientTests
{
    sealed class FakeStreamProcess : IStreamProcess
    {
        readonly Channel<string> _lines = Channel.CreateUnbounded<string>();
        public bool Started;
        public void Emit(string line) => _lines.Writer.TryWrite(line);
        public void EndStream() => _lines.Writer.TryComplete();
        public void Start() => Started = true;
        public async IAsyncEnumerable<string> ReadLinesAsync(CancellationToken ct)
        {
            await foreach (var l in _lines.Reader.ReadAllAsync(ct)) yield return l;
        }
        public void Kill() => _lines.Writer.TryComplete();
        public void Dispose() { }
    }

    [TestMethod]
    public async Task EmitsSnapshots_TaggedWithHost()
    {
        var fake = new FakeStreamProcess();
        var received = new List<IReadOnlyList<SessionSnapshot>>();
        var client = new RemoteSessionStreamClient("llamabox", () => fake);
        client.SnapshotReceived += snaps => received.Add(snaps);

        var cts = new CancellationTokenSource();
        var run = client.RunAsync(cts.Token);
        fake.Emit("""[{"SessionId":"abc","Cwd":"/home/schoen/pr-crew","TranscriptPath":"/x","LastActivityUtc":"2026-05-24T03:00:00+00:00","State":"PendingPermission","RollupState":"PendingPermission","PendingMessage":null,"WindowTitle":"t","Subagents":[]}]""");
        await Task.Delay(50);

        Assert.AreEqual(1, received.Count);
        Assert.AreEqual("abc", received[0][0].SessionId);
        Assert.AreEqual("llamabox", received[0][0].Host);   // tagged by the client

        fake.EndStream();
        cts.Cancel();          // RunAsync catches OperationCanceledException and returns
        await run;
    }
}
```

- [x] **Step 2: Run — expect failure** (`IStreamProcess`, `RemoteSessionStreamClient`, `SessionSnapshot.Host` absent).

- [x] **Step 3: Add `Host` to `SessionSnapshot`**

In `Claude.Core/Models/SessionSnapshot.cs`, add a nullable `Host` property (default `null` = local) to the record. If it is a positional record, add `string? Host = null` as the last parameter (it has a default so existing local construction sites are unaffected). Confirm `RollupState` derivation is untouched.

- [x] **Step 4: Define the process seam**

Create `Claude.Core/Remote/IStreamProcess.cs`:

```csharp
namespace Claude.Core.Remote;

public interface IStreamProcess : IDisposable
{
    void Start();
    IAsyncEnumerable<string> ReadLinesAsync(CancellationToken cancellationToken);
    void Kill();
}
```

- [x] **Step 5: Implement the real ssh process**

Create `Claude.Core/Remote/SshStreamProcess.cs`:

```csharp
using System.Diagnostics;

namespace Claude.Core.Remote;

public sealed class SshStreamProcess : IStreamProcess
{
    readonly Process _process;

    public SshStreamProcess(string sshTarget, string remoteCli)
    {
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ssh",
                // -T no pty; BatchMode so a missing key fails fast instead of prompting.
                ArgumentList =
                {
                    "-T", "-o", "BatchMode=yes", "-o", "ServerAliveInterval=15",
                    sshTarget, remoteCli, "list", "--json-stream"
                },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };
    }

    public void Start() => _process.Start();

    public async IAsyncEnumerable<string> ReadLinesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? line;
        while ((line = await _process.StandardOutput.ReadLineAsync(cancellationToken)) is not null)
        {
            yield return line;
        }
    }

    public void Kill() { try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch { } }

    public void Dispose() { Kill(); _process.Dispose(); }
}
```

- [x] **Step 6: Implement the client with reconnect/backoff**

Create `Claude.Core/Remote/RemoteSessionStreamClient.cs`:

```csharp
using System.Text.Json;
using Claude.Core.Models;

namespace Claude.Core.Remote;

public enum RemoteConnectionState { Connecting, Connected, Disconnected }

public sealed class RemoteSessionStreamClient
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    readonly string _host;
    readonly Func<IStreamProcess> _processFactory;

    public RemoteSessionStreamClient(string host, Func<IStreamProcess> processFactory)
    {
        _host = host;
        _processFactory = processFactory;
    }

    public event Action<IReadOnlyList<SessionSnapshot>>? SnapshotReceived;
    public event Action<RemoteConnectionState>? ConnectionStateChanged;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!cancellationToken.IsCancellationRequested)
        {
            ConnectionStateChanged?.Invoke(RemoteConnectionState.Connecting);
            using var process = _processFactory();
            try
            {
                process.Start();
                ConnectionStateChanged?.Invoke(RemoteConnectionState.Connected);
                backoff = TimeSpan.FromSeconds(1);
                await foreach (var line in process.ReadLinesAsync(cancellationToken))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var snapshots = Parse(line);
                    if (snapshots is not null) SnapshotReceived?.Invoke(snapshots);
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* fall through to reconnect */ }

            ConnectionStateChanged?.Invoke(RemoteConnectionState.Disconnected);
            if (cancellationToken.IsCancellationRequested) break;
            try { await Task.Delay(backoff, cancellationToken); } catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
        }
    }

    IReadOnlyList<SessionSnapshot>? Parse(string ndjsonLine)
    {
        try
        {
            var snaps = JsonSerializer.Deserialize<List<SessionSnapshot>>(ndjsonLine, JsonOptions);
            if (snaps is null) return null;
            return snaps.Select(s => s with { Host = _host }).ToList();
        }
        catch { return null; }   // skip malformed line, keep stream alive
    }
}
```

> If `SessionSnapshot` is not a `record` (no `with`), add a method `WithHost(string host)` returning a copy, and use it here. Verify against the actual type.

- [x] **Step 7: Run the test — expect PASS.** Then commit:
```bash
git add Claude.Core/Remote/IStreamProcess.cs Claude.Core/Remote/SshStreamProcess.cs Claude.Core/Remote/RemoteSessionStreamClient.cs Claude.Core/Models/SessionSnapshot.cs Claude.Core.Tests/Remote/RemoteSessionStreamClientTests.cs
git commit -m "feat(remote): SSH NDJSON stream client with host tagging + reconnect backoff"
```

---

## Phase 5: UI integration

Goal of phase: remote sessions appear in the MAUI Sessions list, badged by host, Focus hidden for remote, with a per-host connection indicator; local behavior unchanged.

### Task 5.1: Merge remote snapshots into `SessionsPageViewModel`

**Files:**
- Modify: `MegaSchoen/ViewModels/SessionsPageViewModel.cs`
- Modify: `MegaSchoen/ViewModels/SessionCardViewModel.cs`
- Reference: `MegaSchoen/SessionsPage.xaml` (badge + Focus button binding)

- [x] **Step 1: Start stream clients for configured hosts in `Start()`**

In `SessionsPageViewModel.Start()` (idempotent), after the existing local watcher setup, add:

```csharp
foreach (var host in RemoteHostConfig.Load())
{
    var client = new RemoteSessionStreamClient(
        host.Name,
        () => new SshStreamProcess(host.SshTarget, host.RemoteCli));
    client.SnapshotReceived += snaps => _dispatcher.Dispatch(() => MergeRemote(host.Name, snaps));
    client.ConnectionStateChanged += state => _dispatcher.Dispatch(() => SetHostState(host.Name, state));
    _remoteClients.Add(client);
    _ = client.RunAsync(_cancellation.Token);
}
```

Add fields `_remoteClients` (`List<RemoteSessionStreamClient>`) and a `_remoteByHost` dictionary holding the last snapshot list per host. `MergeRemote` replaces that host's slice of the observable collection; `SetHostState` updates a per-host indicator and, on `Disconnected`, marks that host's cards stale (kept, not removed — honors the no-time-cutoff rule).

- [x] **Step 2: Dispose stream clients in `Dispose()`**

In `Dispose()`, cancel `_cancellation` (already present) and clear `_remoteClients` after the existing watcher teardown. `RunAsync` exits on cancellation; `SshStreamProcess.Dispose()` kills the ssh child.

- [x] **Step 3: Hide Focus for remote cards**

In `SessionCardViewModel`, add `public bool IsRemote => Snapshot.Host is not null;` and `public bool CanFocus => !IsRemote;`. Bind the Focus button's `IsVisible` to `CanFocus` in `SessionsPage.xaml`. Add a `HostBadge` (`Snapshot.Host ?? "local"`) shown on the card.

- [x] **Step 4: Build the MAUI app (Windows)**

Run: `MSBuild.exe MegaSchoen\MegaSchoen.csproj -p:Configuration=Debug`
Expected: build succeeds; output at the canonical `MegaSchoen\bin\x64\Debug\...\MegaSchoen.exe`.

- [x] **Step 5: Smoke test (use the smoke-test skill)**

Kill any running `MegaSchoen.exe` first (SingleInstance masks stale builds — see project memory). Launch the app with `remote-hosts.json` configured for llamabox; confirm a `llamabox` group appears with live remote sessions and correct state badges, no Focus button on remote cards, and a connection indicator that recovers after `ssh` is interrupted.

- [x] **Step 6: Commit**

```bash
git add MegaSchoen/ViewModels/SessionsPageViewModel.cs MegaSchoen/ViewModels/SessionCardViewModel.cs MegaSchoen/SessionsPage.xaml
git commit -m "feat(ui): merge remote sessions into Sessions page (host badge, no remote Focus)"
```

---

## Done / fast-follow (out of scope here)
- Focus + cycler for remote sessions (remote-session → ssh-tab → local-window correlation).
- Windows-driven one-click remote install (this plan ships the scripted `setup-remote.sh`).
- `remote-hosts.json` editing UI (hand-edited for v1).
