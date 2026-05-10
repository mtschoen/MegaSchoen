# Active Claude Sessions Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a live dashboard of active Claude Code sessions, surfaced as a new MAUI Shell page in MegaSchoen alongside Display Manager, and as a new `ClaudeSessionsCLI` binary with `list` (one-shot or live) and `focus` verbs.

**Architecture:** Rename `ClaudeCycler.Core` → `Claude.Core` and grow it to own session enumeration + state classification. Pull `IClaudeProcessLocator` / `IClaudeWindowFocuser` interfaces with Windows-only impls so non-Windows ports drop in cleanly later. Active sessions = running cmd.exe windows joined to the most-recently-modified `~/.claude/projects/<slug>/*.jsonl` per cwd. State badges driven by StateStore presence (no time gates) — `Stop` hook upserts `AwaitingInput`, transcript tail-read distinguishes Working from Idle. MAUI restructures AppShell to a flyout with two siblings (Display Manager, Claude Sessions); SessionsPageViewModel uses two `FileSystemWatcher`s (one for `needy-sessions.json`, one recursive on `~/.claude/projects/`) funneling into a bounded channel with 250ms debounce — no polling. CLI uses Spectre.Console `AnsiConsole.Live()` for human mode and NDJSON for `--json-stream`, matching the conventions in `~/.claude/notes/idioms_cli_dashboard.md`.

**Tech Stack:** .NET 10, C# 13, MAUI Shell, Spectre.Console (NuGet), `System.Threading.Channels`, `FileSystemWatcher`, MSBuild (not `dotnet build` — native C++ dep). **MSTest 4.0.2** for tests — the existing `Claude.Core.Tests` project uses MSTest with `Microsoft.VisualStudio.TestTools.UnitTesting` as a global using. Plan task code below sometimes shows xUnit syntax; convert to MSTest when implementing: `[Fact]` → `[TestMethod]`, add `[TestClass]` on the class, `Assert.Equal(expected, actual)` → `Assert.AreEqual(expected, actual)`, `Assert.True/False` → `Assert.IsTrue/IsFalse`, `Assert.Single(x)` → `Assert.AreEqual(1, x.Count)`, `Assert.Empty(x)` → `Assert.AreEqual(0, x.Count)`, `Assert.NotEqual(a, b)` → `Assert.AreNotEqual(a, b)`, `IDisposable`/`Dispose` cleanup → `[TestCleanup]` method. Drop the `using Xunit;` line. Build with `MSBuild.exe MegaSchoen.sln -p:Configuration=Debug`.

---

## Phase 1: Rename `ClaudeCycler.Core` → `Claude.Core`

Foundation for the rest of the work. After this phase, the build is green with the new name and namespace; no behavior changes.

### Task 1.1: Move directories and rename csproj files

**Files:**
- Move: `ClaudeCycler.Core/` → `Claude.Core/`
- Move: `ClaudeCycler.Core.Tests/` → `Claude.Core.Tests/`
- Rename: `Claude.Core/ClaudeCycler.Core.csproj` → `Claude.Core/Claude.Core.csproj`
- Rename: `Claude.Core.Tests/ClaudeCycler.Core.Tests.csproj` → `Claude.Core.Tests/Claude.Core.Tests.csproj`

- [x] **Step 1: Stop any running MegaSchoen.exe and clean obj/bin**

```powershell
Get-Process MegaSchoen -ErrorAction SilentlyContinue | Stop-Process -Force
Get-ChildItem -Path . -Recurse -Directory -Include obj,bin | Remove-Item -Recurse -Force
```

- [x] **Step 2: Move the directories with `git mv` so history follows**

```bash
git mv ClaudeCycler.Core Claude.Core
git mv ClaudeCycler.Core.Tests Claude.Core.Tests
git mv Claude.Core/ClaudeCycler.Core.csproj Claude.Core/Claude.Core.csproj
git mv Claude.Core.Tests/ClaudeCycler.Core.Tests.csproj Claude.Core.Tests/Claude.Core.Tests.csproj
```

- [x] **Step 3: Update the test csproj's project reference to the renamed library**

Open `Claude.Core.Tests/Claude.Core.Tests.csproj` and change any `<ProjectReference Include="..\ClaudeCycler.Core\ClaudeCycler.Core.csproj" />` to `<ProjectReference Include="..\Claude.Core\Claude.Core.csproj" />`.

- [x] **Step 4: Verify the renames**

```bash
git status
ls Claude.Core
ls Claude.Core.Tests
```

Expected: shows the renames as moves, both directories present with their `.csproj` files.

### Task 1.2: Update namespaces in source files

**Files:**
- Modify: every `*.cs` under `Claude.Core/` and `Claude.Core.Tests/`
- Modify: every consumer file that has `using ClaudeCycler.Core` or `using ClaudeCycler.Core.Models`

- [x] **Step 1: Replace `namespace ClaudeCycler.Core` with `namespace Claude.Core` recursively**

```powershell
Get-ChildItem -Recurse -Include *.cs -Path Claude.Core,Claude.Core.Tests | ForEach-Object {
    (Get-Content $_.FullName -Raw) `
        -replace 'namespace ClaudeCycler\.Core', 'namespace Claude.Core' `
        | Set-Content -NoNewline -Encoding utf8 $_.FullName
}
```

- [x] **Step 2: Replace `using ClaudeCycler.Core` with `using Claude.Core` across the whole repo**

```powershell
Get-ChildItem -Recurse -Include *.cs -Path Claude.Core,Claude.Core.Tests,MegaSchoen,ClaudeHookBridge | ForEach-Object {
    (Get-Content $_.FullName -Raw) `
        -replace 'using ClaudeCycler\.Core', 'using Claude.Core' `
        | Set-Content -NoNewline -Encoding utf8 $_.FullName
}
```

- [x] **Step 3: Verify no `ClaudeCycler.Core` references remain in source**

Use Grep:
- pattern: `ClaudeCycler\.Core`
- glob: `*.cs`

Expected: no matches.

### Task 1.3: Update consumers and solution

**Files:**
- Modify: `MegaSchoen/MegaSchoen.csproj:75` (ProjectReference path)
- Modify: `ClaudeHookBridge/ClaudeHookBridge.csproj` (ProjectReference path)
- Modify: `MegaSchoen.sln` (project entries on lines 16-19)

- [x] **Step 1: Update `MegaSchoen/MegaSchoen.csproj`**

Open `MegaSchoen/MegaSchoen.csproj` and replace:

```xml
<ProjectReference Include="..\ClaudeCycler.Core\ClaudeCycler.Core.csproj" Condition="$(TargetFramework.Contains('windows'))" />
```

with:

```xml
<ProjectReference Include="..\Claude.Core\Claude.Core.csproj" Condition="$(TargetFramework.Contains('windows'))" />
```

- [x] **Step 2: Update `ClaudeHookBridge/ClaudeHookBridge.csproj`**

Find the `<ProjectReference Include="..\ClaudeCycler.Core\ClaudeCycler.Core.csproj" />` line and change the path to `..\Claude.Core\Claude.Core.csproj`.

- [x] **Step 3: Update `MegaSchoen.sln`**

Edit `MegaSchoen.sln`:

Replace:
```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ClaudeCycler.Core", "ClaudeCycler.Core\ClaudeCycler.Core.csproj", "{90BB3B8F-B229-436D-ABF4-92562A343292}"
```
with:
```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Claude.Core", "Claude.Core\Claude.Core.csproj", "{90BB3B8F-B229-436D-ABF4-92562A343292}"
```

Replace:
```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ClaudeCycler.Core.Tests", "ClaudeCycler.Core.Tests\ClaudeCycler.Core.Tests.csproj", "{702D4E9E-5C9A-4858-8C97-C9DB24796B88}"
```
with:
```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "Claude.Core.Tests", "Claude.Core.Tests\Claude.Core.Tests.csproj", "{702D4E9E-5C9A-4858-8C97-C9DB24796B88}"
```

(GUIDs preserved so VS doesn't lose tracking.)

### Task 1.4: Build verification + commit

- [x] **Step 1: Restore + build the solution**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false -restore
```

Expected: build succeeds with no errors. Warnings about LF/CRLF are fine.

- [x] **Step 2: Run the existing test suite to confirm renames didn't break behavior**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --no-build
```

Expected: all existing tests pass.

- [x] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor: rename ClaudeCycler.Core -> Claude.Core

Foundation for the active-sessions-dashboard work. The library is
about to grow beyond the cycler use case (session enumeration,
state classification) so the cycler-specific name no longer fits.

No behavior change.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Phase 2: Domain types in `Claude.Core`

New value types that the enumerator, classifier, CLI, and ViewModel all consume. Pure logic; full coverage achievable.

### Task 2.1: SessionState enum

**Files:**
- Create: `Claude.Core/Models/SessionState.cs`

- [x] **Step 1: Write the enum**

`Claude.Core/Models/SessionState.cs`:
```csharp
namespace Claude.Core.Models;

// Lower ordinal = more attention-needed. Used for sort order and rollup.
public enum SessionState
{
    PendingPermission = 0,
    AwaitingInput = 1,
    Working = 2,
    Idle = 3,
    Unknown = 4
}
```

- [x] **Step 2: Build and commit**

```bash
MSBuild.exe Claude.Core/Claude.Core.csproj -p:Configuration=Debug -nodeReuse:false
git add Claude.Core/Models/SessionState.cs
git commit -m "feat(core): add SessionState enum

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 2.2: WindowToken record

**Files:**
- Create: `Claude.Core/Models/WindowToken.cs`

- [x] **Step 1: Write the record**

`Claude.Core/Models/WindowToken.cs`:
```csharp
namespace Claude.Core.Models;

// Opaque wrapper around a Win32 HWND so SessionSnapshot doesn't leak IntPtr
// into consumer code. Internal fields are exposed only to platform impls
// inside Claude.Core.
public readonly record struct WindowToken
{
    internal IntPtr Handle { get; init; }

    public static WindowToken FromHandle(IntPtr handle) => new() { Handle = handle };

    public bool IsZero => Handle == IntPtr.Zero;
}
```

- [x] **Step 2: Build and commit**

```bash
MSBuild.exe Claude.Core/Claude.Core.csproj -p:Configuration=Debug -nodeReuse:false
git add Claude.Core/Models/WindowToken.cs
git commit -m "feat(core): add WindowToken value type

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 2.3: ClaudeWindow record

**Files:**
- Create: `Claude.Core/Models/ClaudeWindow.cs`

- [x] **Step 1: Write the record**

`Claude.Core/Models/ClaudeWindow.cs`:
```csharp
namespace Claude.Core.Models;

public readonly record struct ClaudeWindow(
    uint ProcessId,
    WindowToken Window,
    string Title,
    string? WorkingDirectory);
```

- [x] **Step 2: Build and commit**

```bash
MSBuild.exe Claude.Core/Claude.Core.csproj -p:Configuration=Debug -nodeReuse:false
git add Claude.Core/Models/ClaudeWindow.cs
git commit -m "feat(core): add ClaudeWindow record

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 2.4: SubagentSnapshot record

**Files:**
- Create: `Claude.Core/Models/SubagentSnapshot.cs`

- [x] **Step 1: Write the record**

`Claude.Core/Models/SubagentSnapshot.cs`:
```csharp
namespace Claude.Core.Models;

public sealed record SubagentSnapshot(
    string AgentId,
    string TranscriptPath,
    DateTimeOffset LastActivityUtc,
    SessionState State);
```

- [x] **Step 2: Build and commit**

```bash
MSBuild.exe Claude.Core/Claude.Core.csproj -p:Configuration=Debug -nodeReuse:false
git add Claude.Core/Models/SubagentSnapshot.cs
git commit -m "feat(core): add SubagentSnapshot record

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 2.5: SessionSnapshot with RollupState (TDD)

**Files:**
- Create: `Claude.Core/Models/SessionSnapshot.cs`
- Create: `Claude.Core.Tests/SessionSnapshotRollupTests.cs`

- [x] **Step 1: Write failing tests**

`Claude.Core.Tests/SessionSnapshotRollupTests.cs`:
```csharp
using Claude.Core.Models;
using Xunit;

namespace Claude.Core.Tests;

public class SessionSnapshotRollupTests
{
    static SessionSnapshot Make(SessionState parent, params SessionState[] subagents) =>
        new(
            SessionId: "abc",
            Cwd: @"C:\foo",
            TranscriptPath: @"C:\foo.jsonl",
            LastActivityUtc: DateTimeOffset.UtcNow,
            State: parent,
            PendingMessage: null,
            Window: WindowToken.FromHandle(IntPtr.Zero),
            WindowTitle: null,
            Subagents: subagents
                .Select((s, i) => new SubagentSnapshot($"a{i}", $"p{i}", DateTimeOffset.UtcNow, s))
                .ToArray());

    [Fact]
    public void RollupState_NoSubagents_EqualsParent()
    {
        Assert.Equal(SessionState.Idle, Make(SessionState.Idle).RollupState);
    }

    [Fact]
    public void RollupState_PicksMinOrdinalAcrossParentAndSubagents()
    {
        // Idle parent, one Working subagent -> Working (lower ordinal wins).
        Assert.Equal(SessionState.Working, Make(SessionState.Idle, SessionState.Working).RollupState);
    }

    [Fact]
    public void RollupState_PendingPermissionSubagentBeatsAwaitingParent()
    {
        Assert.Equal(
            SessionState.PendingPermission,
            Make(SessionState.AwaitingInput, SessionState.PendingPermission, SessionState.Idle).RollupState);
    }

    [Fact]
    public void RollupState_AllResolvedAcrossParentAndSubagents()
    {
        Assert.Equal(SessionState.Idle, Make(SessionState.Idle, SessionState.Idle, SessionState.Unknown).RollupState);
    }
}
```

- [x] **Step 2: Run tests to confirm they fail (SessionSnapshot doesn't exist yet)**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "FullyQualifiedName~SessionSnapshotRollupTests" --no-restore
```

Expected: build error on `SessionSnapshot`.

- [x] **Step 3: Write SessionSnapshot**

`Claude.Core/Models/SessionSnapshot.cs`:
```csharp
namespace Claude.Core.Models;

public sealed record SessionSnapshot(
    string SessionId,
    string Cwd,
    string TranscriptPath,
    DateTimeOffset LastActivityUtc,
    SessionState State,
    string? PendingMessage,
    WindowToken Window,
    string? WindowTitle,
    IReadOnlyList<SubagentSnapshot> Subagents)
{
    public SessionState RollupState
    {
        get
        {
            if (Subagents.Count == 0) return State;
            var minSubagent = Subagents.Min(s => (int)s.State);
            return (SessionState)Math.Min((int)State, minSubagent);
        }
    }
}
```

- [x] **Step 4: Run tests to confirm they pass**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "FullyQualifiedName~SessionSnapshotRollupTests"
```

Expected: 4 passed.

- [x] **Step 5: Commit**

```bash
git add Claude.Core/Models/SessionSnapshot.cs Claude.Core.Tests/SessionSnapshotRollupTests.cs
git commit -m "feat(core): add SessionSnapshot record with rollup state

RollupState = min ordinal across parent + subagents, so a card shows
the most-attention-needed state in its hierarchy.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Phase 3: Platform interfaces and Windows impls

Pull the seams the spec mandates. Move `Win32ForegroundHelper` from MegaSchoen into `Claude.Core` so the CLI can use it.

### Task 3.1: IClaudeProcessLocator interface

**Files:**
- Create: `Claude.Core/IClaudeProcessLocator.cs`

- [x] **Step 1: Write the interface**

`Claude.Core/IClaudeProcessLocator.cs`:
```csharp
using Claude.Core.Models;

namespace Claude.Core;

public interface IClaudeProcessLocator
{
    IReadOnlyList<ClaudeWindow> EnumerateWindows();
}
```

- [x] **Step 2: Build and commit**

```bash
MSBuild.exe Claude.Core/Claude.Core.csproj -p:Configuration=Debug -nodeReuse:false
git add Claude.Core/IClaudeProcessLocator.cs
git commit -m "feat(core): add IClaudeProcessLocator interface

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 3.2: IClaudeWindowFocuser interface

**Files:**
- Create: `Claude.Core/IClaudeWindowFocuser.cs`

- [x] **Step 1: Write the interface**

`Claude.Core/IClaudeWindowFocuser.cs`:
```csharp
using Claude.Core.Models;

namespace Claude.Core;

public interface IClaudeWindowFocuser
{
    bool BringToFront(WindowToken window);
}
```

- [x] **Step 2: Build and commit**

```bash
MSBuild.exe Claude.Core/Claude.Core.csproj -p:Configuration=Debug -nodeReuse:false
git add Claude.Core/IClaudeWindowFocuser.cs
git commit -m "feat(core): add IClaudeWindowFocuser interface

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 3.3: Move Win32ForegroundHelper into Claude.Core

**Files:**
- Move: `MegaSchoen/Platforms/Windows/Services/Win32ForegroundHelper.cs` → `Claude.Core/Interop/Win32ForegroundHelper.cs`
- Modify: `Claude.Core/Interop/User32.cs` — add the missing P/Invokes that Win32ForegroundHelper needs

- [x] **Step 1: Identify what User32/Kernel32 P/Invokes Win32ForegroundHelper needs**

Read `MegaSchoen/Platforms/Windows/Services/Win32Interop.cs` to find: `IsIconic`, `ShowWindow`, `SW_RESTORE`, `GetCurrentThreadId`, `GetForegroundWindow`, `AttachThreadInput`, `BringWindowToTop`, `SetForegroundWindow`, `SetFocus`, plus the existing `GetWindowThreadProcessId`.

- [x] **Step 2: Add the missing P/Invokes to `Claude.Core/Interop/User32.cs`**

Open `Claude.Core/Interop/User32.cs` and add (preserving any existing imports):

```csharp
using System.Runtime.InteropServices;

namespace Claude.Core.Interop;

static partial class User32
{
    public const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);
}

static class Kernel32Threading
{
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
}
```

(If `User32.cs` is not declared `partial`, declare both blocks in the same file directly — adjust to fit the existing declaration.)

- [x] **Step 3: Move and rewrite Win32ForegroundHelper**

Create `Claude.Core/Interop/Win32ForegroundHelper.cs`:

```csharp
using Claude.Core.Models;
using static Claude.Core.Interop.User32;

namespace Claude.Core.Interop;

static class Win32ForegroundHelper
{
    public static bool BringToFront(WindowToken token)
    {
        var targetHwnd = token.Handle;
        if (targetHwnd == IntPtr.Zero) return false;

        if (IsIconic(targetHwnd))
        {
            ShowWindow(targetHwnd, SW_RESTORE);
        }

        // Three-way AttachThreadInput so SetForegroundWindow is allowed to
        // hand focus to the target. Standard workaround for the
        // foreground-lock restriction on modern Windows.
        var currentThread = Kernel32Threading.GetCurrentThreadId();
        var foregroundHwnd = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foregroundHwnd, out _);
        var targetThread = GetWindowThreadProcessId(targetHwnd, out _);

        var attachedCurrent = false;
        var attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedCurrent = AttachThreadInput(currentThread, foregroundThread, true);
            }
            if (foregroundThread != 0 && foregroundThread != targetThread)
            {
                attachedTarget = AttachThreadInput(targetThread, foregroundThread, true);
            }

            BringWindowToTop(targetHwnd);
            var result = SetForegroundWindow(targetHwnd);
            SetFocus(targetHwnd);
            return result;
        }
        finally
        {
            if (attachedTarget) AttachThreadInput(targetThread, foregroundThread, false);
            if (attachedCurrent) AttachThreadInput(currentThread, foregroundThread, false);
        }
    }
}
```

- [x] **Step 4: Delete the old file from MegaSchoen and update its consumer (`ClaudeWindowService`)**

```bash
git rm MegaSchoen/Platforms/Windows/Services/Win32ForegroundHelper.cs
```

In `MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs`, the call

```csharp
Win32ForegroundHelper.BringToFront(next.Window.WindowHandle);
```

stays — but we'll route it through the new Focuser interface in Task 3.5 / Phase 7. For now, do a temporary fix: replace the call with

```csharp
Claude.Core.Interop.Win32ForegroundHelper.BringToFront(WindowToken.FromHandle(next.Window.WindowHandle));
```

and add `using Claude.Core.Models;` to the `using` block. Also: change `Win32ForegroundHelper`'s accessibility from internal to public for now (we'll narrow it again in Task 3.5 when the focuser wraps it). Edit `Claude.Core/Interop/Win32ForegroundHelper.cs` and change `static class Win32ForegroundHelper` to `public static class Win32ForegroundHelper`.

- [x] **Step 5: Build the whole solution and commit**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false
git add -A
git commit -m "refactor: move Win32ForegroundHelper into Claude.Core

ClaudeSessionsCLI will need to focus windows without depending on
the MAUI host. WindowToken wraps the HWND so consumers don't have
to touch IntPtr.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 3.4: WindowsClaudeProcessLocator (TDD where practical)

**Files:**
- Create: `Claude.Core/Windows/WindowsClaudeProcessLocator.cs`

The Win32 API call itself isn't unit-testable; we cover it indirectly via the enumerator integration tests in Phase 5 (with a fake). Step here is just the wrap.

- [x] **Step 1: Implement the locator**

`Claude.Core/Windows/WindowsClaudeProcessLocator.cs`:
```csharp
using Claude.Core.Models;

namespace Claude.Core.Windows;

public sealed class WindowsClaudeProcessLocator : IClaudeProcessLocator
{
    public IReadOnlyList<ClaudeWindow> EnumerateWindows()
    {
        var raw = ProcessResolver.EnumerateCmdExeWindows();
        var result = new List<ClaudeWindow>(raw.Count);
        foreach (var window in raw)
        {
            result.Add(new ClaudeWindow(
                ProcessId: window.ProcessId,
                Window: WindowToken.FromHandle(window.WindowHandle),
                Title: window.WindowTitle,
                WorkingDirectory: window.WorkingDirectory));
        }
        return result;
    }
}
```

- [x] **Step 2: Build and commit**

```bash
MSBuild.exe Claude.Core/Claude.Core.csproj -p:Configuration=Debug -nodeReuse:false
git add Claude.Core/Windows/WindowsClaudeProcessLocator.cs
git commit -m "feat(core): add WindowsClaudeProcessLocator

Wraps the existing ProcessResolver.EnumerateCmdExeWindows so callers
program against an interface instead of a static.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 3.5: WindowsClaudeWindowFocuser

**Files:**
- Create: `Claude.Core/Windows/WindowsClaudeWindowFocuser.cs`
- Modify: `MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs` (use the new interface; remove the temporary Win32ForegroundHelper direct-call)

- [x] **Step 1: Implement the focuser**

`Claude.Core/Windows/WindowsClaudeWindowFocuser.cs`:
```csharp
using Claude.Core.Interop;
using Claude.Core.Models;

namespace Claude.Core.Windows;

public sealed class WindowsClaudeWindowFocuser : IClaudeWindowFocuser
{
    public bool BringToFront(WindowToken window) => Win32ForegroundHelper.BringToFront(window);
}
```

- [x] **Step 2: Make ClaudeWindowService take an `IClaudeWindowFocuser` via constructor**

Modify `MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs`:

Change the constructor and field:
```csharp
sealed class ClaudeWindowService
{
    readonly TrayIconService _tray;
    readonly IClaudeWindowFocuser _focuser;
    readonly StateStore _store = new();
    readonly SessionLivenessVerifier _verifier = new();
    IntPtr _lastFocused = IntPtr.Zero;

    public ClaudeWindowService(TrayIconService tray, IClaudeWindowFocuser focuser)
    {
        _tray = tray;
        _focuser = focuser;
    }
```

Change the focus call (currently `Win32ForegroundHelper.BringToFront(next.Window.WindowHandle);`) to:
```csharp
_focuser.BringToFront(WindowToken.FromHandle(next.Window.WindowHandle));
```

Add `using Claude.Core;` and `using Claude.Core.Models;` if not already present.

- [x] **Step 3: Register WindowsClaudeWindowFocuser in MauiProgram.cs**

Open `MegaSchoen/MauiProgram.cs` and inside the `#if WINDOWS` block, add:
```csharp
builder.Services.AddSingleton<IClaudeWindowFocuser, Claude.Core.Windows.WindowsClaudeWindowFocuser>();
```

- [x] **Step 4: Narrow Win32ForegroundHelper back to internal**

Edit `Claude.Core/Interop/Win32ForegroundHelper.cs` and change `public static class Win32ForegroundHelper` back to `static class Win32ForegroundHelper`. Now only `WindowsClaudeWindowFocuser` (in the same assembly) calls into it.

- [x] **Step 5: Build the whole solution and commit**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false
git add -A
git commit -m "feat(core): add WindowsClaudeWindowFocuser; route ClaudeWindowService through it

Win32ForegroundHelper goes back to internal — only the focuser
implementation in the same assembly calls it now.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Phase 4: Slug encoder + state classifier

Pure logic; full TDD coverage.

### Task 4.1: SlugEncoder (TDD)

**Files:**
- Create: `Claude.Core/SlugEncoder.cs`
- Create: `Claude.Core.Tests/SlugEncoderTests.cs`

- [x] **Step 1: Write failing tests**

`Claude.Core.Tests/SlugEncoderTests.cs`:
```csharp
using Claude.Core;
using Xunit;

namespace Claude.Core.Tests;

public class SlugEncoderTests
{
    [Fact]
    public void Encode_WindowsPathWithDriveLetter_ProducesDoubleHyphen()
    {
        Assert.Equal("C--Users-mtsch-source-repos-MegaSchoen",
            SlugEncoder.Encode(@"C:\Users\mtsch\source\repos\MegaSchoen"));
    }

    [Fact]
    public void Encode_PathWithForwardSlashes_HyphenatesEach()
    {
        Assert.Equal("-Users-sam-Projects-dev-journal",
            SlugEncoder.Encode("/Users/sam/Projects/dev-journal"));
    }

    [Fact]
    public void Encode_PathWithMixedSeparators_HyphenatesAll()
    {
        Assert.Equal("C--Users-mtsch-mix",
            SlugEncoder.Encode(@"C:/Users\mtsch/mix"));
    }

    [Fact]
    public void Encode_TrailingSeparator_DoesNotProduceTrailingHyphen()
    {
        // Trailing separator is not part of the canonical cwd we get from
        // Windows; trim before encoding to be safe.
        Assert.Equal("C--Users-mtsch", SlugEncoder.Encode(@"C:\Users\mtsch\"));
    }
}
```

- [x] **Step 2: Run tests to confirm they fail**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "FullyQualifiedName~SlugEncoderTests"
```

Expected: build error on `SlugEncoder`.

- [x] **Step 3: Implement SlugEncoder**

`Claude.Core/SlugEncoder.cs`:
```csharp
namespace Claude.Core;

public static class SlugEncoder
{
    // Claude Code encodes a cwd into a directory name under ~/.claude/projects/
    // by replacing path separator characters (':', '\\', '/') with '-'.
    // Adjacent separators produce adjacent hyphens (e.g. "C:\foo" -> "C--foo").
    // Trailing separators are trimmed before encoding.
    public static string Encode(string cwd)
    {
        var trimmed = cwd.TrimEnd('\\', '/');
        var chars = new char[trimmed.Length];
        for (var i = 0; i < trimmed.Length; i++)
        {
            var c = trimmed[i];
            chars[i] = c is ':' or '\\' or '/' ? '-' : c;
        }
        return new string(chars);
    }
}
```

- [x] **Step 4: Run tests to confirm they pass**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "FullyQualifiedName~SlugEncoderTests"
```

Expected: 4 passed.

- [x] **Step 5: Commit**

```bash
git add Claude.Core/SlugEncoder.cs Claude.Core.Tests/SlugEncoderTests.cs
git commit -m "feat(core): SlugEncoder for ~/.claude/projects/<slug> directory naming

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 4.2: Expose SessionLivenessVerifier last-entry classification

**Files:**
- Modify: `Claude.Core/SessionLivenessVerifier.cs` — promote `ClassifyLastEntry` and `LastEntryClass` from `private` to `internal`, OR add a new public method `GetLastEntryClass(string transcriptPath)` and a public `LastEntryClass` enum

- [x] **Step 1: Promote `LastEntryClass` enum and `ClassifyLastEntry` static method to public**

Edit `Claude.Core/SessionLivenessVerifier.cs`. Move `LastEntryClass` out as a public enum at namespace level:

```csharp
public enum LastEntryClass
{
    SessionPending,
    Resolved
}
```

Change `static LastEntryClass ClassifyLastEntry(string transcriptPath)` to `public static LastEntryClass ClassifyLastEntry(string transcriptPath)`.

- [x] **Step 2: Build (existing tests should still pass)**

```bash
MSBuild.exe Claude.Core/Claude.Core.csproj -p:Configuration=Debug -nodeReuse:false
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --no-build
```

Expected: all green.

- [x] **Step 3: Commit**

```bash
git add Claude.Core/SessionLivenessVerifier.cs
git commit -m "refactor(core): expose LastEntryClass + ClassifyLastEntry as public

SessionStateClassifier (next commit) needs the tail-read result
without going through the IsStillWaiting wrapper.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 4.3: SessionStateClassifier (TDD)

**Files:**
- Create: `Claude.Core/SessionStateClassifier.cs`
- Create: `Claude.Core.Tests/SessionStateClassifierTests.cs`

- [x] **Step 1: Write failing tests with a temp-file fixture**

`Claude.Core.Tests/SessionStateClassifierTests.cs`:
```csharp
using Claude.Core;
using Claude.Core.Models;
using Xunit;

namespace Claude.Core.Tests;

public class SessionStateClassifierTests : IDisposable
{
    readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"sscl-{Guid.NewGuid():N}.jsonl");

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    [Fact]
    public void Classify_StateStoreHitWithPermissionReason_ReturnsPendingPermission()
    {
        var entry = new SessionEntry { Reason = WaitingReason.Permission, TranscriptPath = _tempFile };
        Assert.Equal(SessionState.PendingPermission,
            SessionStateClassifier.Classify(entry, _tempFile));
    }

    [Fact]
    public void Classify_StateStoreHitWithAwaitingInputReason_ReturnsAwaitingInput()
    {
        var entry = new SessionEntry { Reason = WaitingReason.AwaitingInput, TranscriptPath = _tempFile };
        Assert.Equal(SessionState.AwaitingInput,
            SessionStateClassifier.Classify(entry, _tempFile));
    }

    [Fact]
    public void Classify_NoStateEntryAndAssistantLast_ReturnsWorking()
    {
        File.WriteAllText(_tempFile, """{"type":"assistant","message":{}}""" + "\n");
        Assert.Equal(SessionState.Working, SessionStateClassifier.Classify(stateEntry: null, _tempFile));
    }

    [Fact]
    public void Classify_NoStateEntryAndUserLast_ReturnsIdle()
    {
        File.WriteAllText(_tempFile, """{"type":"user","message":{}}""" + "\n");
        Assert.Equal(SessionState.Idle, SessionStateClassifier.Classify(stateEntry: null, _tempFile));
    }

    [Fact]
    public void Classify_TranscriptMissing_ReturnsUnknown()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jsonl");
        Assert.Equal(SessionState.Unknown, SessionStateClassifier.Classify(stateEntry: null, missingPath));
    }
}
```

- [x] **Step 2: Run tests to confirm they fail (SessionStateClassifier doesn't exist)**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "FullyQualifiedName~SessionStateClassifierTests"
```

Expected: build error on `SessionStateClassifier`.

- [x] **Step 3: Implement SessionStateClassifier**

`Claude.Core/SessionStateClassifier.cs`:
```csharp
using Claude.Core.Models;

namespace Claude.Core;

public static class SessionStateClassifier
{
    public static SessionState Classify(SessionEntry? stateEntry, string transcriptPath)
    {
        if (stateEntry is not null)
        {
            return stateEntry.Reason switch
            {
                WaitingReason.Permission => SessionState.PendingPermission,
                WaitingReason.AwaitingInput => SessionState.AwaitingInput,
                _ => SessionState.Unknown
            };
        }

        if (string.IsNullOrEmpty(transcriptPath) || !File.Exists(transcriptPath))
        {
            return SessionState.Unknown;
        }

        try
        {
            return SessionLivenessVerifier.ClassifyLastEntry(transcriptPath) switch
            {
                LastEntryClass.SessionPending => SessionState.Working,
                LastEntryClass.Resolved => SessionState.Idle,
                _ => SessionState.Unknown
            };
        }
        catch (Exception exception)
        {
            Logger.Log($"SessionStateClassifier: ClassifyLastEntry threw for {transcriptPath}: {exception.Message}");
            return SessionState.Unknown;
        }
    }
}
```

- [x] **Step 4: Run tests to confirm they pass**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "FullyQualifiedName~SessionStateClassifierTests"
```

Expected: 5 passed.

- [x] **Step 5: Commit**

```bash
git add Claude.Core/SessionStateClassifier.cs Claude.Core.Tests/SessionStateClassifierTests.cs
git commit -m "feat(core): SessionStateClassifier — pure-logic state mapping

StateStore presence is the discriminator between PendingPermission/
AwaitingInput and Working/Idle. No time gates.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Phase 5: ActiveSessionEnumerator

Joins window-list ⨝ slug-derived JSONL globs ⨝ StateStore. Integration-tested with a fake locator + temp-dir Claude projects tree.

### Task 5.1: Test fakes and harness

**Files:**
- Create: `Claude.Core.Tests/Fakes/FakeProcessLocator.cs`
- Create: `Claude.Core.Tests/Fakes/ClaudeProjectsFixture.cs`

- [x] **Step 1: Write FakeProcessLocator**

`Claude.Core.Tests/Fakes/FakeProcessLocator.cs`:
```csharp
using Claude.Core;
using Claude.Core.Models;

namespace Claude.Core.Tests.Fakes;

internal sealed class FakeProcessLocator : IClaudeProcessLocator
{
    public List<ClaudeWindow> Windows { get; } = new();
    public IReadOnlyList<ClaudeWindow> EnumerateWindows() => Windows;
}
```

- [x] **Step 2: Write the projects-tree fixture**

`Claude.Core.Tests/Fakes/ClaudeProjectsFixture.cs`:
```csharp
namespace Claude.Core.Tests.Fakes;

internal sealed class ClaudeProjectsFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"claude-projects-{Guid.NewGuid():N}");

    public ClaudeProjectsFixture()
    {
        Directory.CreateDirectory(Root);
    }

    public string AddSession(string slug, string sessionId, string lastLineJson, DateTime mtimeUtc)
    {
        var dir = Path.Combine(Root, slug);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{sessionId}.jsonl");
        File.WriteAllText(path, lastLineJson + "\n");
        File.SetLastWriteTimeUtc(path, mtimeUtc);
        return path;
    }

    public string AddSubagent(string slug, string sessionId, string agentId, string lastLineJson, DateTime mtimeUtc)
    {
        var dir = Path.Combine(Root, slug, sessionId, "subagents");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"agent-{agentId}.jsonl");
        File.WriteAllText(path, lastLineJson + "\n");
        File.SetLastWriteTimeUtc(path, mtimeUtc);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
```

- [x] **Step 3: Build, then commit**

```bash
MSBuild.exe Claude.Core.Tests/Claude.Core.Tests.csproj -p:Configuration=Debug -nodeReuse:false
git add Claude.Core.Tests/Fakes/
git commit -m "test(core): add FakeProcessLocator and ClaudeProjectsFixture

Test scaffolding for ActiveSessionEnumerator integration tests.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 5.2: ActiveSessionEnumerator skeleton + first test (no windows → empty)

**Files:**
- Create: `Claude.Core/ActiveSessionEnumerator.cs`
- Create: `Claude.Core.Tests/ActiveSessionEnumeratorTests.cs`

The enumerator needs to know where `~/.claude/projects/` is. We add a constructor parameter for the projects root so tests can point it at the fixture; production code will resolve `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` + `.claude/projects`.

- [x] **Step 1: Write the failing test**

`Claude.Core.Tests/ActiveSessionEnumeratorTests.cs`:
```csharp
using Claude.Core;
using Claude.Core.Models;
using Claude.Core.Tests.Fakes;
using Xunit;

namespace Claude.Core.Tests;

public class ActiveSessionEnumeratorTests
{
    [Fact]
    public void Enumerate_NoWindows_ReturnsEmpty()
    {
        using var fixture = new ClaudeProjectsFixture();
        var locator = new FakeProcessLocator();
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));

        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);

        var result = enumerator.Enumerate();
        Assert.Empty(result);
    }
}
```

- [x] **Step 2: Run test to confirm it fails (ActiveSessionEnumerator doesn't exist)**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "Enumerate_NoWindows_ReturnsEmpty"
```

Expected: build error on `ActiveSessionEnumerator`.

- [x] **Step 3: Write the skeleton**

`Claude.Core/ActiveSessionEnumerator.cs`:
```csharp
using Claude.Core.Models;

namespace Claude.Core;

public sealed class ActiveSessionEnumerator
{
    static readonly StringComparer SessionStateOrder = StringComparer.Ordinal;

    readonly IClaudeProcessLocator _locator;
    readonly StateStore _store;
    readonly string _projectsRoot;

    public ActiveSessionEnumerator(IClaudeProcessLocator locator, StateStore store, string projectsRoot)
    {
        _locator = locator;
        _store = store;
        _projectsRoot = projectsRoot;
    }

    public ActiveSessionEnumerator(IClaudeProcessLocator locator, StateStore store)
        : this(locator, store, DefaultProjectsRoot()) { }

    static string DefaultProjectsRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

    public IReadOnlyList<SessionSnapshot> Enumerate()
    {
        var windows = _locator.EnumerateWindows();
        if (windows.Count == 0) return Array.Empty<SessionSnapshot>();

        // Implementation grows over the next tasks.
        return Array.Empty<SessionSnapshot>();
    }
}
```

- [x] **Step 4: Run test to confirm it passes**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "Enumerate_NoWindows_ReturnsEmpty"
```

Expected: 1 passed.

- [x] **Step 5: Commit**

```bash
git add Claude.Core/ActiveSessionEnumerator.cs Claude.Core.Tests/ActiveSessionEnumeratorTests.cs
git commit -m "feat(core): ActiveSessionEnumerator skeleton

Empty path covered. Joining + classification land in subsequent
tasks under TDD.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 5.3: Window with no slug-dir → skipped

- [x] **Step 1: Add the failing test**

Append to `ActiveSessionEnumeratorTests.cs`:
```csharp
    [Fact]
    public void Enumerate_WindowCwdHasNoProjectsDir_ProducesNoSessions()
    {
        using var fixture = new ClaudeProjectsFixture();
        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(
            ProcessId: 100,
            Window: WindowToken.FromHandle(new IntPtr(1)),
            Title: "cmd",
            WorkingDirectory: @"C:\nowhere\that\matches"));
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));

        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);
        Assert.Empty(enumerator.Enumerate());
    }
```

- [x] **Step 2: Run to confirm it passes already (no slug-dir means nothing to glob)**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "Enumerate_WindowCwdHasNoProjectsDir"
```

Expected: PASS (the loop doesn't yet do anything for a window). Even after we add slug-glob in the next task, this should still pass since the dir won't exist.

- [x] **Step 3: Commit (regression coverage of an early-exit path)**

```bash
git add Claude.Core.Tests/ActiveSessionEnumeratorTests.cs
git commit -m "test(core): cover window-cwd-with-no-slug-dir produces no sessions

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 5.4: Most-recent JSONL match (Working state)

- [x] **Step 1: Add the failing test**

Append to `ActiveSessionEnumeratorTests.cs`:
```csharp
    [Fact]
    public void Enumerate_OneWindowOneTranscriptAssistantLast_ReturnsWorking()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\proj";
        var slug = SlugEncoder.Encode(cwd);
        fixture.AddSession(slug, "abc-123",
            """{"type":"assistant","message":{}}""",
            DateTime.UtcNow);

        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(
            ProcessId: 100,
            Window: WindowToken.FromHandle(new IntPtr(1)),
            Title: "cmd",
            WorkingDirectory: cwd));
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));

        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);
        var result = enumerator.Enumerate();

        Assert.Single(result);
        Assert.Equal("abc-123", result[0].SessionId);
        Assert.Equal(cwd, result[0].Cwd);
        Assert.Equal(SessionState.Working, result[0].State);
        Assert.Empty(result[0].Subagents);
    }
```

- [x] **Step 2: Run to confirm it fails (no glob logic yet)**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "Enumerate_OneWindowOneTranscriptAssistantLast"
```

Expected: assertion failure (result is empty).

- [x] **Step 3: Implement the join**

Replace the `Enumerate()` body in `Claude.Core/ActiveSessionEnumerator.cs` with:

```csharp
public IReadOnlyList<SessionSnapshot> Enumerate()
{
    var windows = _locator.EnumerateWindows();
    if (windows.Count == 0) return Array.Empty<SessionSnapshot>();

    var stateFile = _store.Read();
    var stateBySessionId = stateFile.Sessions;

    var snapshots = new List<SessionSnapshot>(capacity: windows.Count);

    foreach (var window in windows)
    {
        if (string.IsNullOrEmpty(window.WorkingDirectory)) continue;

        var slug = SlugEncoder.Encode(window.WorkingDirectory);
        var slugDir = Path.Combine(_projectsRoot, slug);
        if (!Directory.Exists(slugDir)) continue;

        // Top-level *.jsonl, freshest wins per cwd.
        var transcripts = Directory.GetFiles(slugDir, "*.jsonl", SearchOption.TopDirectoryOnly);
        if (transcripts.Length == 0) continue;

        var freshest = transcripts
            .Select(p => new { Path = p, Mtime = File.GetLastWriteTimeUtc(p) })
            .OrderByDescending(p => p.Mtime)
            .First();

        if (!VerifyCwdMatch(freshest.Path, window.WorkingDirectory))
        {
            Logger.Log($"ActiveSessionEnumerator: cwd mismatch on {freshest.Path}; skipping (slug collision)");
            continue;
        }

        var sessionId = Path.GetFileNameWithoutExtension(freshest.Path);

        SessionEntry? stateEntry = stateBySessionId.TryGetValue(sessionId, out var entry) ? entry : null;
        var state = SessionStateClassifier.Classify(stateEntry, freshest.Path);
        var subagents = EnumerateSubagents(slugDir, sessionId);

        snapshots.Add(new SessionSnapshot(
            SessionId: sessionId,
            Cwd: window.WorkingDirectory,
            TranscriptPath: freshest.Path,
            LastActivityUtc: new DateTimeOffset(freshest.Mtime, TimeSpan.Zero),
            State: state,
            PendingMessage: stateEntry?.Message,
            Window: window.Window,
            WindowTitle: window.Title,
            Subagents: subagents));
    }

    snapshots.Sort(CompareForDisplay);
    return snapshots;
}

static int CompareForDisplay(SessionSnapshot a, SessionSnapshot b)
{
    var byState = ((int)a.RollupState).CompareTo((int)b.RollupState);
    if (byState != 0) return byState;
    return b.LastActivityUtc.CompareTo(a.LastActivityUtc);
}

static bool VerifyCwdMatch(string transcriptPath, string expectedCwd)
{
    try
    {
        using var stream = new FileStream(transcriptPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var firstLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(firstLine)) return true; // empty -> trust slug

        using var doc = System.Text.Json.JsonDocument.Parse(firstLine);
        if (!doc.RootElement.TryGetProperty("cwd", out var cwdElement)) return true; // first line lacks cwd -> trust slug
        var actualCwd = cwdElement.GetString();
        return string.Equals(actualCwd, expectedCwd, StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
        return true; // unreadable first line -> trust slug rather than skip
    }
}

static IReadOnlyList<SubagentSnapshot> EnumerateSubagents(string slugDir, string sessionId)
{
    var subagentsDir = Path.Combine(slugDir, sessionId, "subagents");
    if (!Directory.Exists(subagentsDir)) return Array.Empty<SubagentSnapshot>();

    var files = Directory.GetFiles(subagentsDir, "agent-*.jsonl", SearchOption.TopDirectoryOnly);
    if (files.Length == 0) return Array.Empty<SubagentSnapshot>();

    var result = new List<SubagentSnapshot>(files.Length);
    foreach (var file in files)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var agentId = name.StartsWith("agent-", StringComparison.Ordinal) ? name["agent-".Length..] : name;
        var mtime = File.GetLastWriteTimeUtc(file);
        var state = SessionStateClassifier.Classify(stateEntry: null, file);
        result.Add(new SubagentSnapshot(agentId, file, new DateTimeOffset(mtime, TimeSpan.Zero), state));
    }
    return result;
}
```

- [x] **Step 4: Run all enumerator tests**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "FullyQualifiedName~ActiveSessionEnumeratorTests"
```

Expected: 3 passed (no-windows, no-slug-dir, one-window-working).

- [x] **Step 5: Commit**

```bash
git add Claude.Core/ActiveSessionEnumerator.cs Claude.Core.Tests/ActiveSessionEnumeratorTests.cs
git commit -m "feat(core): join windows to project-slug transcripts

Most-recently-modified JSONL per cwd. Verifies cwd via the first
line's cwd field; if absent or unreadable, trusts the slug.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 5.5: Multiple JSONLs in same slug → freshest wins

- [x] **Step 1: Add the failing test**

Append to `ActiveSessionEnumeratorTests.cs`:
```csharp
    [Fact]
    public void Enumerate_MultipleTranscriptsSameSlug_PicksFreshest()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\proj";
        var slug = SlugEncoder.Encode(cwd);

        var older = DateTime.UtcNow.AddMinutes(-30);
        var newer = DateTime.UtcNow;
        fixture.AddSession(slug, "old-id",
            """{"type":"user","message":{}}""", older);
        fixture.AddSession(slug, "new-id",
            """{"type":"assistant","message":{}}""", newer);

        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(100, WindowToken.FromHandle(new IntPtr(1)), "cmd", cwd));
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));
        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);

        var result = enumerator.Enumerate();
        Assert.Single(result);
        Assert.Equal("new-id", result[0].SessionId);
    }
```

- [x] **Step 2: Run; this should pass already because of the OrderByDescending in Step 3 of Task 5.4**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "Enumerate_MultipleTranscriptsSameSlug_PicksFreshest"
```

Expected: PASS.

- [x] **Step 3: Commit (regression coverage)**

```bash
git add Claude.Core.Tests/ActiveSessionEnumeratorTests.cs
git commit -m "test(core): cover freshest-JSONL-wins selection

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 5.6: cwd-mismatch sanity check skips collisions

- [x] **Step 1: Add the failing test**

Append to `ActiveSessionEnumeratorTests.cs`:
```csharp
    [Fact]
    public void Enumerate_FirstLineCwdMismatch_SkipsTranscript()
    {
        using var fixture = new ClaudeProjectsFixture();
        // Two cwds that collide to the same slug "C--foo-bar":
        var actualCwd = @"C:\foo\bar";
        var slug = SlugEncoder.Encode(actualCwd);

        // The window says C:\foo\bar, but the transcript was written with cwd=C:\foo-bar
        // (the colliding partner). The classifier should detect mismatch and skip.
        fixture.AddSession(slug, "wrong-cwd-id",
            """{"type":"assistant","message":{},"cwd":"C:\\foo-bar"}""",
            DateTime.UtcNow);

        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(100, WindowToken.FromHandle(new IntPtr(1)), "cmd", actualCwd));
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));
        var enumerator = new ActiveSessionEnumerator(locator, store, fixture.Root);

        Assert.Empty(enumerator.Enumerate());
    }
```

- [x] **Step 2: Run; this should pass already because of VerifyCwdMatch**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "Enumerate_FirstLineCwdMismatch_SkipsTranscript"
```

Expected: PASS. If FAIL, debug — possible escaping issue in the JSON test string.

- [x] **Step 3: Commit**

```bash
git add Claude.Core.Tests/ActiveSessionEnumeratorTests.cs
git commit -m "test(core): cover slug-collision sanity check

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 5.7: Subagent rollup

- [x] **Step 1: Add the failing test**

Append to `ActiveSessionEnumeratorTests.cs`:
```csharp
    [Fact]
    public void Enumerate_SessionWithSubagents_RollsUpAndExposesIndividualStates()
    {
        using var fixture = new ClaudeProjectsFixture();
        var cwd = @"C:\repo\proj";
        var slug = SlugEncoder.Encode(cwd);

        // Parent: Idle (last user line).
        fixture.AddSession(slug, "parent-1",
            """{"type":"user","message":{}}""", DateTime.UtcNow.AddSeconds(-10));
        // Two subagents: one Working, one Idle.
        fixture.AddSubagent(slug, "parent-1", "abc",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);
        fixture.AddSubagent(slug, "parent-1", "def",
            """{"type":"user","message":{}}""", DateTime.UtcNow.AddSeconds(-5));

        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(100, WindowToken.FromHandle(new IntPtr(1)), "cmd", cwd));
        var store = new StateStore(Path.Combine(fixture.Root, "state.json"));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.Single(result);
        Assert.Equal(SessionState.Idle, result[0].State);
        Assert.Equal(2, result[0].Subagents.Count);
        Assert.Equal(SessionState.Working, result[0].RollupState); // Working subagent dominates
    }
```

- [x] **Step 2: Run to confirm pass**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "Enumerate_SessionWithSubagents"
```

Expected: PASS.

- [x] **Step 3: Commit**

```bash
git add Claude.Core.Tests/ActiveSessionEnumeratorTests.cs
git commit -m "test(core): cover subagent rollup with worst-state rule

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 5.8: StateStore join + sort order

- [x] **Step 1: Add the failing test**

Append to `ActiveSessionEnumeratorTests.cs`:
```csharp
    [Fact]
    public void Enumerate_StateStoreUpgradesIdleToAwaitingInput_AndSortsToTop()
    {
        using var fixture = new ClaudeProjectsFixture();

        var cwdA = @"C:\repo\a";
        var cwdB = @"C:\repo\b";
        var slugA = SlugEncoder.Encode(cwdA);
        var slugB = SlugEncoder.Encode(cwdB);

        // A: assistant-last (would be Working without state)
        // B: user-last (Idle)
        fixture.AddSession(slugA, "session-a",
            """{"type":"assistant","message":{}}""", DateTime.UtcNow);
        fixture.AddSession(slugB, "session-b",
            """{"type":"user","message":{}}""", DateTime.UtcNow);

        // Add session-b as AwaitingInput in StateStore -> should sort BEFORE Working A.
        var statePath = Path.Combine(fixture.Root, "state.json");
        var store = new StateStore(statePath);
        store.Upsert("session-b", new SessionEntry
        {
            Cwd = cwdB,
            TranscriptPath = Path.Combine(fixture.Root, slugB, "session-b.jsonl"),
            NotifiedAt = DateTimeOffset.UtcNow,
            Reason = WaitingReason.AwaitingInput
        });

        var locator = new FakeProcessLocator();
        locator.Windows.Add(new ClaudeWindow(100, WindowToken.FromHandle(new IntPtr(1)), "cmd-a", cwdA));
        locator.Windows.Add(new ClaudeWindow(101, WindowToken.FromHandle(new IntPtr(2)), "cmd-b", cwdB));

        var result = new ActiveSessionEnumerator(locator, store, fixture.Root).Enumerate();

        Assert.Equal(2, result.Count);
        Assert.Equal("session-b", result[0].SessionId);
        Assert.Equal(SessionState.AwaitingInput, result[0].State);
        Assert.Equal("session-a", result[1].SessionId);
        Assert.Equal(SessionState.Working, result[1].State);
    }
```

- [x] **Step 2: Run to confirm pass**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "Enumerate_StateStoreUpgradesIdleToAwaitingInput"
```

Expected: PASS.

- [x] **Step 3: Commit**

```bash
git add Claude.Core.Tests/ActiveSessionEnumeratorTests.cs
git commit -m "test(core): cover StateStore join + state-priority sort

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Phase 6: ClaudeSessionsCLI

### Task 6.1: Project skeleton + dispatcher

**Files:**
- Create: `ClaudeSessionsCLI/ClaudeSessionsCLI.csproj`
- Create: `ClaudeSessionsCLI/Program.cs`
- Modify: `MegaSchoen.sln` (add the new project entry + config mappings)

- [x] **Step 1: Create the csproj**

`ClaudeSessionsCLI/ClaudeSessionsCLI.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>ClaudeSessionsCLI</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Spectre.Console" Version="0.49.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Claude.Core\Claude.Core.csproj" />
  </ItemGroup>
</Project>
```

(If `0.49.1` is not the latest at implementation time, take the latest stable.)

- [x] **Step 2: Create the dispatcher Program.cs**

`ClaudeSessionsCLI/Program.cs`:
```csharp
using ClaudeSessionsCLI;

if (arguments.Length == 0)
{
    PrintUsage();
    return 0;
}

return arguments[0].ToLowerInvariant() switch
{
    "list" => await Commands.ListCommand.Run(arguments[1..]),
    "focus" => await Commands.FocusCommand.Run(arguments[1..]),
    _ => PrintUnknown(arguments[0])
};

static int PrintUsage()
{
    Console.WriteLine("Usage: ClaudeSessionsCLI <command> [arguments]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  list  [--json] [--json-stream] [--interval 1.5]   List active Claude sessions");
    Console.WriteLine("  focus <session-id-prefix>                          Bring matching window to foreground");
    return 0;
}

static int PrintUnknown(string verb)
{
    Console.Error.WriteLine($"Unknown command: {verb}");
    Console.Error.WriteLine("Run with no arguments to see usage.");
    return 1;
}

partial class Program
{
    // Top-level statements expose `arguments` as args; alias for clarity below.
}
```

Note: top-level statements provide `args`; the snippet uses `arguments` for readability. Adjust to match `args` if the compiler complains, OR rewrite as `static int Main(string[] arguments) { ... }`.

- [x] **Step 3: Add ClaudeSessionsCLI to MegaSchoen.sln**

Open `MegaSchoen.sln` and add a new Project line at the bottom of the existing `Project(...) ... EndProject` block (line ~21 area):

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "ClaudeSessionsCLI", "ClaudeSessionsCLI\ClaudeSessionsCLI.csproj", "{C5D8E001-0001-0001-0001-000000000001}"
EndProject
```

In the `GlobalSection(ProjectConfigurationPlatforms)` block, add config mappings (matching `DisplayManagerCLI`'s style):

```
{C5D8E001-0001-0001-0001-000000000001}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
{C5D8E001-0001-0001-0001-000000000001}.Debug|Any CPU.Build.0 = Debug|Any CPU
{C5D8E001-0001-0001-0001-000000000001}.Debug|x64.ActiveCfg = Debug|Any CPU
{C5D8E001-0001-0001-0001-000000000001}.Debug|x64.Build.0 = Debug|Any CPU
{C5D8E001-0001-0001-0001-000000000001}.Debug|x86.ActiveCfg = Debug|Any CPU
{C5D8E001-0001-0001-0001-000000000001}.Debug|x86.Build.0 = Debug|Any CPU
{C5D8E001-0001-0001-0001-000000000001}.Release|Any CPU.ActiveCfg = Release|Any CPU
{C5D8E001-0001-0001-0001-000000000001}.Release|Any CPU.Build.0 = Release|Any CPU
{C5D8E001-0001-0001-0001-000000000001}.Release|x64.ActiveCfg = Release|Any CPU
{C5D8E001-0001-0001-0001-000000000001}.Release|x64.Build.0 = Release|Any CPU
{C5D8E001-0001-0001-0001-000000000001}.Release|x86.ActiveCfg = Release|Any CPU
{C5D8E001-0001-0001-0001-000000000001}.Release|x86.Build.0 = Release|Any CPU
```

- [x] **Step 4: Add stub Commands files so the project compiles**

`ClaudeSessionsCLI/Commands/ListCommand.cs`:
```csharp
namespace ClaudeSessionsCLI.Commands;

static class ListCommand
{
    public static Task<int> Run(string[] arguments) => Task.FromResult(0);
}
```

`ClaudeSessionsCLI/Commands/FocusCommand.cs`:
```csharp
namespace ClaudeSessionsCLI.Commands;

static class FocusCommand
{
    public static Task<int> Run(string[] arguments) => Task.FromResult(0);
}
```

- [x] **Step 5: Build, restore, and commit**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false -restore
git add -A
git commit -m "feat(cli): scaffold ClaudeSessionsCLI project + dispatcher

Spectre.Console dep added. list/focus stubs in place.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 6.2: ListCommand JSON one-shot mode

**Files:**
- Modify: `ClaudeSessionsCLI/Commands/ListCommand.cs`
- Create: `ClaudeSessionsCLI/CliOptions.cs`

- [x] **Step 1: Write CliOptions parser**

`ClaudeSessionsCLI/CliOptions.cs`:
```csharp
namespace ClaudeSessionsCLI;

sealed class CliOptions
{
    public bool Json { get; init; }
    public bool JsonStream { get; init; }
    public double IntervalSeconds { get; init; } = 1.5;

    public static CliOptions Parse(string[] arguments)
    {
        var json = false;
        var jsonStream = false;
        var interval = 1.5;
        for (var i = 0; i < arguments.Length; i++)
        {
            switch (arguments[i])
            {
                case "--json": json = true; break;
                case "--json-stream": jsonStream = true; break;
                case "--interval" when i + 1 < arguments.Length:
                    if (double.TryParse(arguments[++i], System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                    {
                        interval = Math.Max(0.1, parsed);
                    }
                    break;
            }
        }
        return new CliOptions { Json = json, JsonStream = jsonStream, IntervalSeconds = interval };
    }
}
```

- [x] **Step 2: Implement ListCommand JSON one-shot path**

Replace `ClaudeSessionsCLI/Commands/ListCommand.cs`:
```csharp
using System.Text.Json;
using Claude.Core;
using Claude.Core.Models;
using Claude.Core.Windows;

namespace ClaudeSessionsCLI.Commands;

static class ListCommand
{
    public static async Task<int> Run(string[] arguments)
    {
        var options = CliOptions.Parse(arguments);
        var enumerator = BuildEnumerator();

        if (options.Json)
        {
            return EmitJsonOnce(enumerator);
        }
        if (options.JsonStream)
        {
            return await EmitJsonStream(enumerator, options).ConfigureAwait(false);
        }
        return await RunHumanMode(enumerator, options).ConfigureAwait(false);
    }

    static ActiveSessionEnumerator BuildEnumerator()
    {
        var locator = new WindowsClaudeProcessLocator();
        var store = new StateStore();
        return new ActiveSessionEnumerator(locator, store);
    }

    static int EmitJsonOnce(ActiveSessionEnumerator enumerator)
    {
        var snapshots = enumerator.Enumerate();
        var json = JsonSerializer.Serialize(
            snapshots.Select(SnapshotDto.From).ToArray(),
            new JsonSerializerOptions { WriteIndented = true });
        Console.Out.Write(json);
        Console.Out.Write('\n');
        return 0;
    }

    static Task<int> EmitJsonStream(ActiveSessionEnumerator enumerator, CliOptions options) =>
        Task.FromResult(0); // implemented in next task

    static Task<int> RunHumanMode(ActiveSessionEnumerator enumerator, CliOptions options) =>
        Task.FromResult(0); // implemented in later task
}

sealed record SnapshotDto(
    string SessionId,
    string Cwd,
    string TranscriptPath,
    DateTimeOffset LastActivityUtc,
    string State,
    string RollupState,
    string? PendingMessage,
    string? WindowTitle,
    SubagentDto[] Subagents)
{
    public static SnapshotDto From(SessionSnapshot snapshot) => new(
        snapshot.SessionId,
        snapshot.Cwd,
        snapshot.TranscriptPath,
        snapshot.LastActivityUtc,
        snapshot.State.ToString(),
        snapshot.RollupState.ToString(),
        snapshot.PendingMessage,
        snapshot.WindowTitle,
        snapshot.Subagents.Select(s => new SubagentDto(s.AgentId, s.LastActivityUtc, s.State.ToString())).ToArray());
}

sealed record SubagentDto(string AgentId, DateTimeOffset LastActivityUtc, string State);
```

(`SnapshotDto` is the wire format — strings instead of enums for friendlier JSON.)

- [x] **Step 3: Build**

```bash
MSBuild.exe ClaudeSessionsCLI/ClaudeSessionsCLI.csproj -p:Configuration=Debug -nodeReuse:false
```

Expected: success.

- [x] **Step 4: Smoke test (manual)**

```bash
".\ClaudeSessionsCLI\bin\Debug\net10.0-windows10.0.26100.0\ClaudeSessionsCLI.exe" list --json
```

Expected: JSON array (possibly empty `[]`) printed; exit 0. If you have an active Claude session in another window, it should appear.

- [x] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(cli): list --json one-shot mode

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 6.3: ListCommand --json-stream

- [x] **Step 1: Implement EmitJsonStream**

Replace the placeholder `EmitJsonStream` in `ListCommand.cs`:
```csharp
static async Task<int> EmitJsonStream(ActiveSessionEnumerator enumerator, CliOptions options)
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    var json = new JsonSerializerOptions { WriteIndented = false };
    var ct = cts.Token;

    while (!ct.IsCancellationRequested)
    {
        var snapshots = enumerator.Enumerate();
        var serialized = JsonSerializer.Serialize(
            snapshots.Select(SnapshotDto.From).ToArray(),
            json);
        await Console.Out.WriteLineAsync(serialized.AsMemory(), ct).ConfigureAwait(false);
        await Console.Out.FlushAsync(ct).ConfigureAwait(false);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
    return 0;
}
```

- [x] **Step 2: Build**

```bash
MSBuild.exe ClaudeSessionsCLI/ClaudeSessionsCLI.csproj -p:Configuration=Debug -nodeReuse:false
```

- [x] **Step 3: Smoke test (manual)**

```bash
".\ClaudeSessionsCLI\bin\Debug\net10.0-windows10.0.26100.0\ClaudeSessionsCLI.exe" list --json-stream --interval 1
```

Expected: NDJSON line every ~1 second. Ctrl-C exits cleanly.

- [x] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(cli): list --json-stream mode (NDJSON, ctrl-c exits)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 6.4: ListCommand human mode with Spectre.Console.Live

- [x] **Step 1: Implement RunHumanMode**

Replace the placeholder `RunHumanMode` in `ListCommand.cs`:
```csharp
static async Task<int> RunHumanMode(ActiveSessionEnumerator enumerator, CliOptions options)
{
    // Pipe-aware: if stdout is redirected, render once and exit (NOT ANSI live).
    if (Console.IsOutputRedirected)
    {
        var snapshots = enumerator.Enumerate();
        var json = JsonSerializer.Serialize(
            snapshots.Select(SnapshotDto.From).ToArray(),
            new JsonSerializerOptions { WriteIndented = true });
        Console.Out.Write(json);
        Console.Out.Write('\n');
        return 0;
    }

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    var ct = cts.Token;

    var table = BuildTable(Array.Empty<SessionSnapshot>());

    await Spectre.Console.AnsiConsole.Live(table)
        .StartAsync(async live =>
        {
            while (!ct.IsCancellationRequested)
            {
                var snapshots = enumerator.Enumerate();
                Repopulate(table, snapshots);
                live.Refresh();
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }).ConfigureAwait(false);

    Spectre.Console.AnsiConsole.WriteLine("Stopped.");
    return 0;
}

static Spectre.Console.Table BuildTable(IReadOnlyList<SessionSnapshot> snapshots)
{
    var table = new Spectre.Console.Table()
        .AddColumn("State")
        .AddColumn("Cwd")
        .AddColumn("Session")
        .AddColumn("Last activity");
    Repopulate(table, snapshots);
    return table;
}

static void Repopulate(Spectre.Console.Table table, IReadOnlyList<SessionSnapshot> snapshots)
{
    table.Rows.Clear();
    if (snapshots.Count == 0)
    {
        table.AddRow("[grey](no active sessions)[/]", "", "", "");
        return;
    }
    foreach (var s in snapshots)
    {
        table.AddRow(
            FormatState(s.RollupState),
            Spectre.Console.Markup.Escape(TruncateMiddle(s.Cwd, 50)),
            s.SessionId.Length >= 8 ? s.SessionId[..8] : s.SessionId,
            $"{s.LastActivityUtc.ToLocalTime():HH:mm:ss}");
    }
}

static string FormatState(SessionState state) => state switch
{
    SessionState.PendingPermission => "[red]PERM[/]",
    SessionState.AwaitingInput => "[yellow]INPUT[/]",
    SessionState.Working => "[green]WORK[/]",
    SessionState.Idle => "[grey]idle[/]",
    _ => "[red]?[/]"
};

static string TruncateMiddle(string s, int max)
{
    if (s.Length <= max) return s;
    var keep = (max - 3) / 2;
    return s[..keep] + "..." + s[^keep..];
}
```

- [x] **Step 2: Build**

```bash
MSBuild.exe ClaudeSessionsCLI/ClaudeSessionsCLI.csproj -p:Configuration=Debug -nodeReuse:false
```

- [x] **Step 3: Smoke test (manual)**

In a real terminal (not piped):
```bash
".\ClaudeSessionsCLI\bin\Debug\net10.0-windows10.0.26100.0\ClaudeSessionsCLI.exe" list
```

Expected: a Spectre.Console table that refreshes every 1.5s. Ctrl-C exits cleanly with "Stopped." printed.

- [x] **Step 4: Smoke test pipe-detection**

```bash
".\ClaudeSessionsCLI\bin\Debug\net10.0-windows10.0.26100.0\ClaudeSessionsCLI.exe" list | clip
```

Expected: clipboard receives a JSON document; the CLI exits immediately (no infinite loop).

- [x] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(cli): list human mode with Spectre.Console.Live

Pipe-aware: emits one-shot JSON when stdout is redirected.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 6.5: FocusCommand

- [x] **Step 1: Implement FocusCommand**

Replace `ClaudeSessionsCLI/Commands/FocusCommand.cs`:
```csharp
using Claude.Core;
using Claude.Core.Windows;

namespace ClaudeSessionsCLI.Commands;

static class FocusCommand
{
    public static Task<int> Run(string[] arguments)
    {
        if (arguments.Length == 0)
        {
            Console.Error.WriteLine("focus: missing <session-id-prefix>");
            return Task.FromResult(1);
        }

        var prefix = arguments[0];
        var locator = new WindowsClaudeProcessLocator();
        var store = new StateStore();
        var enumerator = new ActiveSessionEnumerator(locator, store);
        var snapshots = enumerator.Enumerate();

        var matches = snapshots.Where(s => s.SessionId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"focus: no session matches prefix '{prefix}'");
            return Task.FromResult(1);
        }
        if (matches.Count > 1)
        {
            Console.Error.WriteLine($"focus: ambiguous prefix '{prefix}' matches {matches.Count} sessions");
            foreach (var m in matches) Console.Error.WriteLine($"  {m.SessionId}  {m.Cwd}");
            return Task.FromResult(1);
        }

        var focuser = new WindowsClaudeWindowFocuser();
        var ok = focuser.BringToFront(matches[0].Window);
        return Task.FromResult(ok ? 0 : 2);
    }
}
```

- [x] **Step 2: Build**

```bash
MSBuild.exe ClaudeSessionsCLI/ClaudeSessionsCLI.csproj -p:Configuration=Debug -nodeReuse:false
```

- [x] **Step 3: Smoke test (manual)**

Open a Claude Code session in another terminal. Then:
```bash
".\ClaudeSessionsCLI\bin\Debug\net10.0-windows10.0.26100.0\ClaudeSessionsCLI.exe" list --json
```

Copy the first 8 chars of a `SessionId` from the output. Then:
```bash
".\ClaudeSessionsCLI\bin\Debug\net10.0-windows10.0.26100.0\ClaudeSessionsCLI.exe" focus <prefix>
```

Expected: the matching window comes to the foreground; CLI exits 0.

- [x] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(cli): focus command with unique-prefix matching

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 6.6: CLI smoke tests

**Files:**
- Create: `Claude.Core.Tests/CliSmokeTests.cs`

- [x] **Step 1: Write a smoke test that spawns the built CLI**

`Claude.Core.Tests/CliSmokeTests.cs`:
```csharp
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Claude.Core.Tests;

public class CliSmokeTests
{
    static string CliPath()
    {
        // Walk up from test bin to find the CLI's published exe.
        var current = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && current is not null; i++)
        {
            var probe = Path.Combine(current, "ClaudeSessionsCLI", "bin", "Debug", "net10.0-windows10.0.26100.0", "ClaudeSessionsCLI.exe");
            if (File.Exists(probe)) return probe;
            current = Directory.GetParent(current)?.FullName;
        }
        throw new FileNotFoundException("ClaudeSessionsCLI.exe not found — build the solution first.");
    }

    [Fact]
    public void ListJson_ProducesParseableJsonAndExitsZero()
    {
        var psi = new ProcessStartInfo(CliPath(), "list --json")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(timeoutMilliseconds: 30_000);
        Assert.Equal(0, process.ExitCode);
        // Should be parseable as a JSON array.
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [Fact]
    public void FocusWithoutArguments_ExitsWithFailureCode()
    {
        var psi = new ProcessStartInfo(CliPath(), "focus")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit(timeoutMilliseconds: 5_000);
        Assert.NotEqual(0, process.ExitCode);
    }
}
```

- [x] **Step 2: Run**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj --filter "FullyQualifiedName~CliSmokeTests"
```

Expected: 2 passed.

- [x] **Step 3: Commit**

```bash
git add Claude.Core.Tests/CliSmokeTests.cs
git commit -m "test(cli): smoke tests for list --json and focus argument-validation

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Phase 7: MAUI Shell restructure

### Task 7.1: Rename MainPage → DisplayManagerPage

**Files:**
- Move: `MegaSchoen/MainPage.xaml` → `MegaSchoen/DisplayManagerPage.xaml`
- Move: `MegaSchoen/MainPage.xaml.cs` → `MegaSchoen/DisplayManagerPage.xaml.cs`
- Move: `MegaSchoen/ViewModels/MainPageViewModel.cs` → `MegaSchoen/ViewModels/DisplayManagerPageViewModel.cs`

- [x] **Step 1: Move with git mv**

```bash
git mv MegaSchoen/MainPage.xaml MegaSchoen/DisplayManagerPage.xaml
git mv MegaSchoen/MainPage.xaml.cs MegaSchoen/DisplayManagerPage.xaml.cs
git mv MegaSchoen/ViewModels/MainPageViewModel.cs MegaSchoen/ViewModels/DisplayManagerPageViewModel.cs
```

- [x] **Step 2: Rename class names inside the moved files**

In `DisplayManagerPage.xaml`:
- Replace `x:Class="MegaSchoen.MainPage"` → `x:Class="MegaSchoen.DisplayManagerPage"`
- Replace any `MainPageViewModel` references → `DisplayManagerPageViewModel`

In `DisplayManagerPage.xaml.cs`:
- Replace `class MainPage` → `class DisplayManagerPage`

In `DisplayManagerPageViewModel.cs`:
- Replace `class MainPageViewModel` → `class DisplayManagerPageViewModel`

- [x] **Step 3: Update references in AppShell.xaml**

Open `MegaSchoen/AppShell.xaml` and change:
```xml
<ShellContent
    Title="Home"
    ContentTemplate="{DataTemplate local:MainPage}"
    Route="MainPage" />
```
to (placeholder until Task 7.3):
```xml
<ShellContent
    Title="Display Manager"
    ContentTemplate="{DataTemplate local:DisplayManagerPage}"
    Route="DisplayManagerPage" />
```

- [x] **Step 4: Build**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false
```

Expected: success. If errors mention `MainPage` somewhere we missed, fix and rebuild.

- [x] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: rename MainPage -> DisplayManagerPage

Prep for adding SessionsPage as a sibling flyout entry.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 7.2: AppShell flyout restructure with two siblings

**Files:**
- Modify: `MegaSchoen/AppShell.xaml`
- Create: `MegaSchoen/SessionsPage.xaml` (placeholder, full content in Phase 8)
- Create: `MegaSchoen/SessionsPage.xaml.cs`

- [x] **Step 1: Create a placeholder SessionsPage**

`MegaSchoen/SessionsPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             x:Class="MegaSchoen.SessionsPage"
             Title="Claude Sessions">

    <VerticalStackLayout Padding="20" Spacing="20">
        <Label Text="Sessions page coming soon" FontSize="20" FontAttributes="Bold"/>
    </VerticalStackLayout>

</ContentPage>
```

`MegaSchoen/SessionsPage.xaml.cs`:
```csharp
namespace MegaSchoen;

public partial class SessionsPage : ContentPage
{
    public SessionsPage()
    {
        InitializeComponent();
    }
}
```

- [x] **Step 2: Restructure AppShell.xaml to a flyout with two entries**

Replace `MegaSchoen/AppShell.xaml` body:
```xml
<?xml version="1.0" encoding="UTF-8" ?>
<Shell
    x:Class="MegaSchoen.AppShell"
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:local="clr-namespace:MegaSchoen"
    Title="MegaSchoen">

    <FlyoutItem Title="Display Manager">
        <ShellContent ContentTemplate="{DataTemplate local:DisplayManagerPage}" Route="displays" />
    </FlyoutItem>
    <FlyoutItem Title="Claude Sessions">
        <ShellContent ContentTemplate="{DataTemplate local:SessionsPage}" Route="sessions" />
    </FlyoutItem>

</Shell>
```

- [x] **Step 3: Build, run, and visually confirm both flyout entries appear**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false
```

Then run `MegaSchoen.exe` and confirm: hamburger menu shows "Display Manager" and "Claude Sessions"; clicking each switches pages.

- [x] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(ui): restructure AppShell to flyout with Display Manager + Claude Sessions

SessionsPage is a placeholder for now; ViewModel + cards land in
the next phase.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Phase 8: SessionsPage UI

### Task 8.1: SessionCardViewModel

**Files:**
- Create: `MegaSchoen/ViewModels/SessionCardViewModel.cs`

- [ ] **Step 1: Write the card view model**

`MegaSchoen/ViewModels/SessionCardViewModel.cs`:
```csharp
#if WINDOWS
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Claude.Core.Models;

namespace MegaSchoen.ViewModels;

public sealed class SessionCardViewModel : INotifyPropertyChanged
{
    SessionSnapshot _snapshot;
    bool _isExpanded;

    public SessionCardViewModel(SessionSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public SessionSnapshot Snapshot
    {
        get => _snapshot;
        set
        {
            _snapshot = value;
            OnPropertyChanged(nameof(Snapshot));
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(StateColor));
            OnPropertyChanged(nameof(CwdShort));
            OnPropertyChanged(nameof(SessionIdStem));
            OnPropertyChanged(nameof(LastActivityRelative));
            OnPropertyChanged(nameof(SubagentSummary));
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            OnPropertyChanged(nameof(IsExpanded));
        }
    }

    public string StateText => _snapshot.RollupState.ToString();

    public string StateColor => _snapshot.RollupState switch
    {
        SessionState.PendingPermission => "#D9534F",
        SessionState.AwaitingInput => "#F0AD4E",
        SessionState.Working => "#5CB85C",
        SessionState.Idle => "#777777",
        _ => "#999999"
    };

    public string CwdShort
    {
        get
        {
            const int max = 60;
            if (_snapshot.Cwd.Length <= max) return _snapshot.Cwd;
            var keep = (max - 3) / 2;
            return _snapshot.Cwd[..keep] + "..." + _snapshot.Cwd[^keep..];
        }
    }

    public string SessionIdStem => _snapshot.SessionId.Length >= 8 ? _snapshot.SessionId[..8] : _snapshot.SessionId;

    public string LastActivityRelative
    {
        get
        {
            var delta = DateTimeOffset.UtcNow - _snapshot.LastActivityUtc;
            return delta.TotalSeconds < 60
                ? $"{(int)delta.TotalSeconds}s ago"
                : delta.TotalMinutes < 60
                    ? $"{(int)delta.TotalMinutes}m ago"
                    : $"{(int)delta.TotalHours}h ago";
        }
    }

    public string SubagentSummary => _snapshot.Subagents.Count == 0
        ? ""
        : $"{_snapshot.Subagents.Count} subagent{(_snapshot.Subagents.Count == 1 ? "" : "s")}";

    public IReadOnlyList<SubagentSnapshot> Subagents => _snapshot.Subagents;

    public event PropertyChangedEventHandler? PropertyChanged;

    void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
#endif
```

- [ ] **Step 2: Build**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false
```

- [ ] **Step 3: Commit**

```bash
git add MegaSchoen/ViewModels/SessionCardViewModel.cs
git commit -m "feat(ui): SessionCardViewModel for SessionsPage cards

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 8.2: SessionsPageViewModel skeleton + DI

**Files:**
- Create: `MegaSchoen/ViewModels/SessionsPageViewModel.cs`
- Modify: `MegaSchoen/MauiProgram.cs`

- [ ] **Step 1: Write the view model skeleton (watcher logic comes in 8.3)**

`MegaSchoen/ViewModels/SessionsPageViewModel.cs`:
```csharp
#if WINDOWS
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Claude.Core;
using Claude.Core.Models;

namespace MegaSchoen.ViewModels;

public sealed class SessionsPageViewModel : INotifyPropertyChanged, IDisposable
{
    readonly ActiveSessionEnumerator _enumerator;
    readonly IClaudeWindowFocuser _focuser;
    readonly IDispatcher _dispatcher;

    public ObservableCollection<SessionCardViewModel> Sessions { get; } = new();
    public ICommand FocusCommand { get; }
    public ICommand ToggleExpandCommand { get; }

    public SessionsPageViewModel(
        ActiveSessionEnumerator enumerator,
        IClaudeWindowFocuser focuser,
        IDispatcher dispatcher)
    {
        _enumerator = enumerator;
        _focuser = focuser;
        _dispatcher = dispatcher;

        FocusCommand = new Command<SessionCardViewModel>(card =>
            _focuser.BringToFront(card.Snapshot.Window));
        ToggleExpandCommand = new Command<SessionCardViewModel>(card =>
            card.IsExpanded = !card.IsExpanded);
    }

    public void RefreshNow()
    {
        var snapshots = _enumerator.Enumerate();
        UpdateUi(snapshots);
    }

    void UpdateUi(IReadOnlyList<SessionSnapshot> snapshots)
    {
        // Match by session-id; replace snapshot in-place when present, otherwise add/remove.
        var keepIds = new HashSet<string>(snapshots.Select(s => s.SessionId));
        for (var i = Sessions.Count - 1; i >= 0; i--)
        {
            if (!keepIds.Contains(Sessions[i].Snapshot.SessionId))
            {
                Sessions.RemoveAt(i);
            }
        }

        for (var i = 0; i < snapshots.Count; i++)
        {
            var existing = Sessions.FirstOrDefault(c => c.Snapshot.SessionId == snapshots[i].SessionId);
            if (existing is null)
            {
                Sessions.Insert(i, new SessionCardViewModel(snapshots[i]));
            }
            else
            {
                existing.Snapshot = snapshots[i];
                var currentIndex = Sessions.IndexOf(existing);
                if (currentIndex != i)
                {
                    Sessions.Move(currentIndex, i);
                }
            }
        }
    }

    public void Dispose()
    {
        // Watchers added in next task; cancellation belongs there.
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
#endif
```

- [ ] **Step 2: Register the view model + ActiveSessionEnumerator in MauiProgram.cs**

Open `MegaSchoen/MauiProgram.cs` and inside the `#if WINDOWS` block, add:
```csharp
builder.Services.AddSingleton<IClaudeProcessLocator, Claude.Core.Windows.WindowsClaudeProcessLocator>();
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<ActiveSessionEnumerator>();
builder.Services.AddTransient<MegaSchoen.ViewModels.SessionsPageViewModel>();
builder.Services.AddTransient<SessionsPage>();
builder.Services.AddTransient<DisplayManagerPage>();
```

(`IClaudeWindowFocuser` was registered in Task 3.5 already.)

Add `using Claude.Core;` and `using Claude.Core.Models;` to the top.

- [ ] **Step 3: Build**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false
```

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(ui): SessionsPageViewModel skeleton + DI registrations

Watcher refresh wires up in next task.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 8.3: FileSystemWatcher debounced refresh

**Files:**
- Modify: `MegaSchoen/ViewModels/SessionsPageViewModel.cs`

- [ ] **Step 1: Add the watcher infrastructure**

In `SessionsPageViewModel.cs`, add fields and modify the constructor:
```csharp
    readonly System.Threading.Channels.Channel<byte> _refreshSignal =
        System.Threading.Channels.Channel.CreateBounded<byte>(
            new System.Threading.Channels.BoundedChannelOptions(1)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite
            });
    readonly CancellationTokenSource _cts = new();
    FileSystemWatcher? _stateWatcher;
    FileSystemWatcher? _transcriptsWatcher;
    Task? _consumerTask;
```

Add a `Start()` method:
```csharp
    public void Start()
    {
        var stateFile = Paths.NeedySessionsFile;
        var stateDir = Path.GetDirectoryName(stateFile);
        if (!string.IsNullOrEmpty(stateDir))
        {
            Directory.CreateDirectory(stateDir);
            _stateWatcher = new FileSystemWatcher(stateDir, Path.GetFileName(stateFile))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _stateWatcher.Changed += OnAnyEvent;
            _stateWatcher.Created += OnAnyEvent;
        }

        var projectsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        if (Directory.Exists(projectsRoot))
        {
            _transcriptsWatcher = new FileSystemWatcher(projectsRoot, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _transcriptsWatcher.Changed += OnAnyEvent;
            _transcriptsWatcher.Created += OnAnyEvent;
        }

        _consumerTask = Task.Run(() => ConsumeAsync(_cts.Token));
        RefreshNow(); // initial load
    }

    void OnAnyEvent(object? sender, FileSystemEventArgs e) => _refreshSignal.Writer.TryWrite(0);

    async Task ConsumeAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _refreshSignal.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
                while (_refreshSignal.Reader.TryRead(out _)) { }
                var snapshots = _enumerator.Enumerate();
                await _dispatcher.DispatchAsync(() => UpdateUi(snapshots)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception exception)
        {
            Logger.Log($"SessionsPageViewModel.ConsumeAsync threw: {exception}");
        }
    }
```

Update `Dispose`:
```csharp
    public void Dispose()
    {
        _cts.Cancel();
        _stateWatcher?.Dispose();
        _transcriptsWatcher?.Dispose();
        _consumerTask?.Wait(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }
```

Add `using Claude.Core;` so `Logger` and `Paths` resolve.

- [ ] **Step 2: Wire Start/Dispose into the page lifecycle**

`MegaSchoen/SessionsPage.xaml.cs`:
```csharp
namespace MegaSchoen;

public partial class SessionsPage : ContentPage
{
#if WINDOWS
    readonly MegaSchoen.ViewModels.SessionsPageViewModel _viewModel;

    public SessionsPage(MegaSchoen.ViewModels.SessionsPageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Dispose();
    }
#else
    public SessionsPage()
    {
        InitializeComponent();
    }
#endif
}
```

- [ ] **Step 3: Build**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false
```

- [ ] **Step 4: Run + manual smoke test**

Run MegaSchoen.exe, switch to the Claude Sessions flyout entry. With a Claude Code session running in another terminal:
- The card should appear within ~250ms.
- When you submit a prompt, the badge should flip Working → Idle within ~1s of the Stop hook firing.
- Cards remove themselves when the cmd.exe window closes.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(ui): event-driven SessionsPage refresh via FileSystemWatcher + bounded channel

Two watchers (needy-sessions.json and ~/.claude/projects/) funnel
into one debounce-and-drain loop with 250ms coalescing. No polling.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 8.4: SessionsPage card layout

**Files:**
- Modify: `MegaSchoen/SessionsPage.xaml`

- [ ] **Step 1: Replace the placeholder XAML with the card layout**

`MegaSchoen/SessionsPage.xaml`:
```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:MegaSchoen.ViewModels"
             x:Class="MegaSchoen.SessionsPage"
             Title="Claude Sessions">

    <ScrollView>
        <VerticalStackLayout Padding="20" Spacing="12">

            <Label Text="Active Claude sessions"
                   FontSize="22"
                   FontAttributes="Bold"/>

            <Label Text="No active Claude sessions"
                   IsVisible="{Binding Sessions.Count, Converter={StaticResource IsZeroConverter}}"
                   TextColor="{StaticResource Gray500}"
                   HorizontalOptions="Center"
                   Margin="20"/>

            <CollectionView ItemsSource="{Binding Sessions}">
                <CollectionView.ItemTemplate>
                    <DataTemplate x:DataType="vm:SessionCardViewModel">
                        <Frame Margin="0,4" Padding="12" BorderColor="{StaticResource Gray300}">
                            <VerticalStackLayout Spacing="6">
                                <Grid ColumnDefinitions="80,*,Auto,Auto" ColumnSpacing="10">
                                    <Label Grid.Column="0"
                                           Text="{Binding StateText}"
                                           TextColor="{Binding StateColor}"
                                           FontAttributes="Bold"
                                           VerticalOptions="Center"/>
                                    <Label Grid.Column="1"
                                           Text="{Binding CwdShort}"
                                           VerticalOptions="Center"
                                           FontSize="13"/>
                                    <Label Grid.Column="2"
                                           Text="{Binding LastActivityRelative}"
                                           TextColor="{StaticResource Gray500}"
                                           FontSize="11"
                                           VerticalOptions="Center"/>
                                    <Button Grid.Column="3"
                                            Text="Focus"
                                            Command="{Binding Source={RelativeSource AncestorType={x:Type vm:SessionsPageViewModel}}, Path=FocusCommand}"
                                            CommandParameter="{Binding .}"
                                            HeightRequest="36"
                                            WidthRequest="80"
                                            VerticalOptions="Center"/>
                                </Grid>
                                <Label Text="{Binding SessionIdStem}"
                                       FontSize="11"
                                       TextColor="{StaticResource Gray500}"/>
                                <Label Text="{Binding SubagentSummary}"
                                       IsVisible="{Binding SubagentSummary, Converter={StaticResource IsNonEmptyStringConverter}}"
                                       FontSize="11"
                                       TextColor="{StaticResource Gray500}"/>
                            </VerticalStackLayout>
                        </Frame>
                    </DataTemplate>
                </CollectionView.ItemTemplate>
            </CollectionView>

        </VerticalStackLayout>
    </ScrollView>

</ContentPage>
```

- [ ] **Step 2: Add the `IsNonEmptyStringConverter` if it doesn't exist**

Look in `MegaSchoen/Converters/` for an existing string-emptiness converter. If absent, create `MegaSchoen/Converters/IsNonEmptyStringConverter.cs`:
```csharp
using System.Globalization;

namespace MegaSchoen.Converters;

public sealed class IsNonEmptyStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

Register it in `MegaSchoen/App.xaml`:
```xml
<converters:IsNonEmptyStringConverter x:Key="IsNonEmptyStringConverter" />
```

(Inside the existing `<Application.Resources>` `<ResourceDictionary>` block. Match the pattern of `IsZeroConverter`.)

- [ ] **Step 3: Build, run, and visually verify**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false
```

Run MegaSchoen.exe → Claude Sessions tab. Cards should render with state badge, cwd, last-activity, Focus button. With no sessions: "No active Claude sessions" centered.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(ui): SessionsPage card layout

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 8.5: Move cycler debug section to SessionsPage

**Files:**
- Modify: `MegaSchoen/DisplayManagerPage.xaml` (remove cycler section)
- Modify: `MegaSchoen/DisplayManagerPage.xaml.cs` (remove cycler handlers and helper)
- Modify: `MegaSchoen/SessionsPage.xaml` (add cycler section at bottom)
- Modify: `MegaSchoen/SessionsPage.xaml.cs` (add cycler handlers)

- [ ] **Step 1: Cut the cycler `Frame` block out of DisplayManagerPage.xaml**

In `MegaSchoen/DisplayManagerPage.xaml`, remove the entire `Frame` element titled "🤖 Claude Cycler (debug)" (currently at lines 217-238).

- [ ] **Step 2: Cut the cycler handlers out of DisplayManagerPage.xaml.cs**

Remove the entire `OnCyclePermsClicked`, `OnCycleAnyWaitingClicked`, and `CycleClaude` methods (and any `#if WINDOWS` / `#endif` wrappers around them) from `MegaSchoen/DisplayManagerPage.xaml.cs`.

- [ ] **Step 3: Append the cycler `Frame` block to SessionsPage.xaml**

In `MegaSchoen/SessionsPage.xaml`, before the closing `</VerticalStackLayout>`, paste the same cycler block:

```xml
<Frame BorderColor="{StaticResource Primary}"
       BackgroundColor="{AppThemeBinding Light={StaticResource Gray100}, Dark={StaticResource Gray900}}"
       Padding="15">
    <VerticalStackLayout Spacing="10">
        <Label Text="🤖 Claude Cycler (debug)"
               FontSize="20"
               FontAttributes="Bold"/>
        <Button x:Name="CyclePermsButton"
                Text="Cycle Pending Permissions"
                Clicked="OnCyclePermsClicked"
                BackgroundColor="{StaticResource Primary}"/>
        <Button x:Name="CycleAnyWaitingButton"
                Text="Cycle Any Waiting"
                Clicked="OnCycleAnyWaitingClicked"
                BackgroundColor="{StaticResource Primary}"/>
        <Label x:Name="CycleClaudeStatusLabel"
               Text=""
               FontSize="12"
               TextColor="{StaticResource Gray600}"/>
    </VerticalStackLayout>
</Frame>
```

- [ ] **Step 4: Add the cycler handlers to SessionsPage.xaml.cs**

Append to `MegaSchoen/SessionsPage.xaml.cs` (inside the class):
```csharp
#if WINDOWS
    void OnCyclePermsClicked(object? sender, EventArgs eventArguments) =>
        CycleClaude(filter: Claude.Core.Models.WaitingReason.Permission);

    void OnCycleAnyWaitingClicked(object? sender, EventArgs eventArguments) =>
        CycleClaude(filter: null);

    void CycleClaude(Claude.Core.Models.WaitingReason? filter)
    {
        try
        {
            var services = Microsoft.UI.Xaml.Application.Current is MegaSchoen.WinUI.App
                ? MauiWinUIApplication.Current.Services
                : null;
            if (services is null)
            {
                CycleClaudeStatusLabel.Text = "DI container not available";
                return;
            }
            var cycler = services.GetService(typeof(MegaSchoen.Platforms.Windows.Services.ClaudeWindowService))
                as MegaSchoen.Platforms.Windows.Services.ClaudeWindowService;
            if (cycler is null)
            {
                CycleClaudeStatusLabel.Text = "ClaudeWindowService not resolved";
                return;
            }
            cycler.CycleToNext(filter);
            var label = filter is null ? "any-waiting" : filter.ToString();
            CycleClaudeStatusLabel.Text = $"CycleToNext({label}) returned at {DateTimeOffset.UtcNow:O}";
        }
        catch (Exception exception)
        {
            CycleClaudeStatusLabel.Text = $"Threw: {exception.GetType().Name}: {exception.Message}";
            Claude.Core.Logger.Log($"CycleClaude({filter}) threw: {exception}");
        }
    }
#else
    void OnCyclePermsClicked(object? sender, EventArgs eventArguments) =>
        CycleClaudeStatusLabel.Text = "Windows only";

    void OnCycleAnyWaitingClicked(object? sender, EventArgs eventArguments) =>
        CycleClaudeStatusLabel.Text = "Windows only";
#endif
```

- [ ] **Step 5: Build, run, and confirm the buttons still work**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false
```

Run MegaSchoen.exe → Claude Sessions tab. Confirm: the "Cycle Pending Permissions" / "Cycle Any Waiting" buttons appear at the bottom and behave the same as before.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(ui): move cycler debug section from DisplayManager to SessionsPage

Conceptually it belongs with the sessions list, not displays.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

### Task 8.6: SessionsPage screenshot test

**Files:**
- Modify: `MegaSchoen.UITests/ScreenshotTests.cs`

- [ ] **Step 1: Read the existing screenshot test to learn the harness**

Use Read on `MegaSchoen.UITests/ScreenshotTests.cs` to understand how the existing test launches and captures.

- [ ] **Step 2: Add a SessionsPage screenshot test**

Append a new `[Fact]` to `ScreenshotTests.cs` that navigates to the Claude Sessions flyout entry and captures a screenshot. Match the existing test pattern; if the existing test does e.g.

```csharp
[Fact]
public void DisplayManagerPage_RendersWithoutErrors() { ... }
```

mirror it as:
```csharp
[Fact]
public void SessionsPage_RendersWithoutErrors()
{
    // ...launch app, navigate to Claude Sessions tab, capture screenshot, assert no exceptions.
    // Mirror the existing DisplayManagerPage test exactly except for the navigation step.
}
```

- [ ] **Step 3: Run the UI test**

```bash
dotnet test MegaSchoen.UITests/MegaSchoen.UITests.csproj
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add MegaSchoen.UITests/ScreenshotTests.cs
git commit -m "test(ui): SessionsPage rendering smoke test

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Done — final verification

- [ ] **Step 1: Build the whole solution clean**

```bash
Get-ChildItem -Recurse -Directory -Include obj,bin | Remove-Item -Recurse -Force
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -nodeReuse:false -restore
```

Expected: clean build, no errors.

- [ ] **Step 2: Run all tests**

```bash
dotnet test Claude.Core.Tests/Claude.Core.Tests.csproj
```

Expected: all green, including new `SessionStateClassifierTests`, `SlugEncoderTests`, `ActiveSessionEnumeratorTests`, `SessionSnapshotRollupTests`, `CliSmokeTests`.

- [ ] **Step 3: Walk through the acceptance criteria from the spec**

In a real environment with at least one Claude Code session running in another terminal:

1. `ClaudeSessionsCLI list --json` prints a JSON array including the session.
2. `ClaudeSessionsCLI list` shows a refreshing table; pasting a new prompt in the other terminal flips the badge.
3. `ClaudeSessionsCLI focus <prefix>` brings the matching window forward.
4. MegaSchoen has two flyout entries; the Claude Sessions tab shows cards sorted by attention-need; Focus works.
5. Cycler hotkeys / tray / debug buttons still work.

- [ ] **Step 4: When everything passes, the plan can be deleted as part of the wrap commit (per the plan-lifecycle rules in writing-plans).**
