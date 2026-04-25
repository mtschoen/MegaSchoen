# Claude Window Cycler — Needy-Entry Verification & Hotkey Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Claude window cycler surface only sessions that are actually waiting on a permission prompt, with no time-based expiry — broaden hook coverage to catch resolution events, verify each entry against its transcript file at cycle time, and switch the hotkey to Ctrl+Alt+Tab.

**Architecture:** Two-layer fix. Layer 1: `HookDispatcher` learns to clear entries on `PostToolUse` and `SessionEnd` (in addition to existing `UserPromptSubmit` and `Stop`); `SettingsJsonInstaller` is extended to register the new events. Layer 2: a new `SessionLivenessVerifier` re-checks each entry at cycle time by comparing the session transcript file's `LastWriteTimeUtc` against the entry's `NotifiedAt` — if the transcript has been touched since the notification, the prompt is treated as resolved and the entry is removed. The transcript path is already on the hook payload (`HookPayload.TranscriptPath`); we store it on `SessionEntry` so the verifier doesn't have to derive it.

**Tech Stack:** .NET 10, MSTest, MAUI (Windows-only services), JSON via `System.Text.Json` and `System.Text.Json.Nodes`, no new NuGet dependencies.

**Spec:** [docs/superpowers/specs/2026-04-24-claude-window-cycler-needy-verification-design.md](../specs/2026-04-24-claude-window-cycler-needy-verification-design.md)

---

## File map

**Create:**
- `ClaudeCycler.Core/SessionLivenessVerifier.cs` — verifies an entry's transcript modtime vs notifiedAt
- `ClaudeCycler.Core.Tests/SessionLivenessVerifierTests.cs` — unit tests for the verifier

**Modify (small):**
- `ClaudeCycler.Core/Models/SessionEntry.cs` — add `TranscriptPath` property
- `ClaudeCycler.Core/HookDispatcher.cs` — capture `TranscriptPath`; add `PostToolUse` + `SessionEnd` cases
- `ClaudeCycler.Core/SettingsJsonInstaller.cs` — extend `EventNames`; extend `EventInstallStatus` with `PostToolUse` + `SessionEnd` fields
- `ClaudeHookBridge/Commands/CheckCommand.cs` — print all five events; update `allInstalled` check
- `ClaudeCycler.Core.Tests/HookDispatcherTests.cs` — add tests for `PostToolUse`, `SessionEnd`, and `TranscriptPath` capture
- `ClaudeCycler.Core.Tests/SettingsJsonInstallerTests.cs` — assert 5 hooks installed and reported
- `MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs` — instantiate verifier; call before `candidates.Add`
- `MegaSchoen/Platforms/Windows/Services/TrayIconService.cs` — add "Clear Needy Sessions" menu item + event
- `MegaSchoen/Platforms/Windows/App.xaml.cs` — wire `ClearNeedyRequested` tray event; change hotkey from `"9"` to `"Tab"`

**No changes needed:**
- `ClaudeCycler.Core/Models/HookPayload.cs` — `TranscriptPath` is already there.
- `MegaSchoen/Platforms/Windows/Services/Win32Interop.cs` — `KeyToVirtualKey("Tab")` already returns `0x09`.
- `MegaSchoen/Platforms/Windows/Services/GlobalHotkeyService.cs` — already supports arbitrary key/modifier combos.
- `ClaudeHookBridge/Commands/StatusCommand.cs` and `ResolveCommand.cs` — operate on the state file generically; no schema-aware code to update beyond what `JsonSerializer` already handles.

---

## Task 1: Spike — confirm `Ctrl+Alt+Tab` registers and transcripts get touched per turn

Before any code changes, we resolve the two remaining open questions from the spec. Each is a one-shot manual probe; no test code lands.

**Files:** none modified.

- [ ] **Step 1: Probe `RegisterHotKey` for Ctrl+Alt+Tab**

Open a fresh PowerShell and run this one-liner. It registers the chord against a hidden message-only window, sleeps long enough for you to press the chord, and prints whether `RegisterHotKey` succeeded.

```powershell
Add-Type -Namespace W -Name H -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mod, uint vk);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
[System.Runtime.InteropServices.DllImport("kernel32.dll")] public static extern int GetLastError();
'@
$ok = [W.H]::RegisterHotKey([IntPtr]::Zero, 1, 0x0002 -bor 0x0001, 0x09)  # MOD_CONTROL | MOD_ALT, VK_TAB
"RegisterHotKey returned: $ok ; GetLastError = $([W.H]::GetLastError())"
[void][W.H]::UnregisterHotKey([IntPtr]::Zero, 1)
```

Expected: `RegisterHotKey returned: True ; GetLastError = 0`. If `False` with error 1409 (ERROR_HOTKEY_ALREADY_REGISTERED) or any other non-zero error, **stop** and report the failure — pick a fallback chord (suggest `Ctrl+Shift+Tab` or `Ctrl+Alt+Backquote`) and update Task 9 accordingly.

- [ ] **Step 2: Probe transcript-file mtime updates**

Find the JSONL transcript file for the current Claude session. The path lives on every hook payload as `transcript_path`. Quick way to grab one: trigger a permission prompt now, then check `%LOCALAPPDATA%\MegaSchoen\needy-sessions.json` — but that file currently does not store the transcript path (Task 2 adds it). For this probe, instead enumerate `~/.claude/projects/` for the most recently modified `.jsonl`:

```bash
ls -lt ~/.claude/projects/*/*.jsonl | head -3
```

Pick the freshest one. In a separate terminal, run a `stat`-style watch on it:

```bash
while true; do stat -c '%y %n' "$path" 2>/dev/null || ls -la --time-style=full-iso "$path"; sleep 1; done
```

Approve a permission prompt in the live session and verify the modtime jumps within ~2 seconds of approval. Also verify it jumps when you send a new user message.

Expected: modtime advances on both events. If the file appears to update on a buffered/delayed schedule (e.g. only every 10s), **stop** and report — we'd need to switch the verifier to JSONL last-line parsing (the documented fallback in the spec) before proceeding to Task 6.

- [ ] **Step 3: Record findings**

Add a paragraph to the spec under "Open implementation questions" marking spikes 1, 2, and 4 resolved (or noting fallback decisions). No commit yet — bundle this with Task 11.

---

## Task 2: Add `TranscriptPath` to `SessionEntry` and have the dispatcher capture it

Storing the transcript path on the entry at notification time eliminates any need for path-encoding logic in the verifier — Claude Code already tells us where the transcript lives.

**Files:**
- Modify: `ClaudeCycler.Core/Models/SessionEntry.cs`
- Modify: `ClaudeCycler.Core/HookDispatcher.cs:27-32`
- Modify: `ClaudeCycler.Core.Tests/HookDispatcherTests.cs`

- [ ] **Step 1: Write a failing test asserting the dispatcher captures `transcript_path`**

Add this test method to `ClaudeCycler.Core.Tests/HookDispatcherTests.cs`, after the existing `Notification_PermissionPrompt_UpsertsSession` test:

```csharp
[TestMethod]
public void Notification_PermissionPrompt_CapturesTranscriptPath()
{
    _dispatcher.Dispatch(new HookPayload
    {
        HookEventName = "Notification",
        NotificationType = "permission_prompt",
        SessionId = "s1",
        Cwd = "C:\\foo",
        TranscriptPath = "C:\\Users\\me\\.claude\\projects\\C--foo\\s1.jsonl",
        Message = "Claude needs your permission"
    });

    var file = _store.Read();
    Assert.AreEqual(
        "C:\\Users\\me\\.claude\\projects\\C--foo\\s1.jsonl",
        file.Sessions["s1"].TranscriptPath);
}
```

- [ ] **Step 2: Run the new test and confirm it fails**

```bash
MSBuild.exe ClaudeCycler.Core.Tests\ClaudeCycler.Core.Tests.csproj -p:Configuration=Debug
dotnet test ClaudeCycler.Core.Tests --filter "Notification_PermissionPrompt_CapturesTranscriptPath" --no-build
```

Expected: build error like `'SessionEntry' does not contain a definition for 'TranscriptPath'`. That's the failure signal.

- [ ] **Step 3: Add `TranscriptPath` to `SessionEntry`**

Replace the entire contents of `ClaudeCycler.Core/Models/SessionEntry.cs` with:

```csharp
using System.Text.Json.Serialization;

namespace ClaudeCycler.Core.Models;

public sealed class SessionEntry
{
    [JsonPropertyName("cwd")]
    public string Cwd { get; set; } = "";

    [JsonPropertyName("transcriptPath")]
    public string? TranscriptPath { get; set; }

    [JsonPropertyName("notifiedAt")]
    public DateTimeOffset NotifiedAt { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
```

- [ ] **Step 4: Have the dispatcher pass the payload's transcript path through**

In `ClaudeCycler.Core/HookDispatcher.cs`, the `Notification` case currently constructs a `SessionEntry` from `payload.Cwd` and `payload.Message`. Add `TranscriptPath`:

```csharp
case "Notification" when payload.NotificationType == "permission_prompt":
    _store.Upsert(payload.SessionId, new SessionEntry
    {
        Cwd = payload.Cwd ?? "",
        TranscriptPath = payload.TranscriptPath,
        NotifiedAt = DateTimeOffset.UtcNow,
        Message = payload.Message
    });
    break;
```

- [ ] **Step 5: Run test to verify it passes**

```bash
dotnet test ClaudeCycler.Core.Tests --filter "Notification_PermissionPrompt_CapturesTranscriptPath"
```

Expected: PASS. Also run all `HookDispatcherTests` to make sure the existing tests still pass:

```bash
dotnet test ClaudeCycler.Core.Tests --filter "HookDispatcherTests"
```

Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add ClaudeCycler.Core/Models/SessionEntry.cs \
        ClaudeCycler.Core/HookDispatcher.cs \
        ClaudeCycler.Core.Tests/HookDispatcherTests.cs
git commit -m "feat(cycler): capture transcript path on needy-session entries

Stores HookPayload.TranscriptPath on the SessionEntry so cycle-time
liveness verification can read the transcript file directly without
having to derive its path from the cwd."
```

---

## Task 3: Dispatch on `PostToolUse` (delete entry)

`PostToolUse` fires after a tool runs successfully — which means any permission prompt that gated it has been resolved.

**Files:**
- Modify: `ClaudeCycler.Core/HookDispatcher.cs:35-37`
- Modify: `ClaudeCycler.Core.Tests/HookDispatcherTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `HookDispatcherTests.cs`, after the existing `Stop_DeletesSession` test:

```csharp
[TestMethod]
public void PostToolUse_DeletesSession()
{
    _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
    _dispatcher.Dispatch(new HookPayload { HookEventName = "PostToolUse", SessionId = "s1" });

    Assert.IsEmpty(_store.Read().Sessions);
}

[TestMethod]
public void PostToolUse_NoMatchingEntry_IsNoop()
{
    _dispatcher.Dispatch(new HookPayload { HookEventName = "PostToolUse", SessionId = "unrelated" });
    Assert.IsEmpty(_store.Read().Sessions);
}
```

- [ ] **Step 2: Run tests; confirm `PostToolUse_DeletesSession` fails**

```bash
dotnet test ClaudeCycler.Core.Tests --filter "PostToolUse_"
```

Expected: `PostToolUse_DeletesSession` FAILs (entry still present after dispatch); `PostToolUse_NoMatchingEntry_IsNoop` may pass already (the default branch is a no-op).

- [ ] **Step 3: Add the case to `HookDispatcher.Dispatch`**

In `ClaudeCycler.Core/HookDispatcher.cs`, change:

```csharp
case "UserPromptSubmit":
case "Stop":
    _store.Delete(payload.SessionId);
    break;
```

to:

```csharp
case "UserPromptSubmit":
case "Stop":
case "PostToolUse":
    _store.Delete(payload.SessionId);
    break;
```

- [ ] **Step 4: Run tests; confirm both pass**

```bash
dotnet test ClaudeCycler.Core.Tests --filter "HookDispatcherTests"
```

Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/HookDispatcher.cs ClaudeCycler.Core.Tests/HookDispatcherTests.cs
git commit -m "feat(cycler): clear needy entry on PostToolUse

PostToolUse fires after a tool ran successfully, which means any
permission prompt gating it has been resolved. Closes the
'approved-mid-turn-but-turn-still-ongoing' staleness case."
```

---

## Task 4: Dispatch on `SessionEnd` (delete entry)

Catches clean `/exit` and any other Claude-initiated shutdown path.

**Files:**
- Modify: `ClaudeCycler.Core/HookDispatcher.cs`
- Modify: `ClaudeCycler.Core.Tests/HookDispatcherTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `HookDispatcherTests.cs`:

```csharp
[TestMethod]
public void SessionEnd_DeletesSession()
{
    _store.Upsert("s1", new SessionEntry { Cwd = "C:\\foo", NotifiedAt = DateTimeOffset.UtcNow });
    _dispatcher.Dispatch(new HookPayload { HookEventName = "SessionEnd", SessionId = "s1" });

    Assert.IsEmpty(_store.Read().Sessions);
}
```

- [ ] **Step 2: Run; confirm fail**

```bash
dotnet test ClaudeCycler.Core.Tests --filter "SessionEnd_DeletesSession"
```

Expected: FAIL.

- [ ] **Step 3: Extend the dispatcher case**

Update `HookDispatcher.cs` to:

```csharp
case "UserPromptSubmit":
case "Stop":
case "PostToolUse":
case "SessionEnd":
    _store.Delete(payload.SessionId);
    break;
```

- [ ] **Step 4: Run tests; confirm pass**

```bash
dotnet test ClaudeCycler.Core.Tests --filter "HookDispatcherTests"
```

Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/HookDispatcher.cs ClaudeCycler.Core.Tests/HookDispatcherTests.cs
git commit -m "feat(cycler): clear needy entry on SessionEnd

Catches clean /exit and other Claude-initiated session shutdown
paths that don't fire Stop."
```

---

## Task 5: Extend `SettingsJsonInstaller` to install all five hook events

Once the dispatcher knows how to handle them, the installer must register them in `~/.claude/settings.json`.

**Files:**
- Modify: `ClaudeCycler.Core/SettingsJsonInstaller.cs`
- Modify: `ClaudeCycler.Core.Tests/SettingsJsonInstallerTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `SettingsJsonInstallerTests.cs`:

```csharp
[TestMethod]
public void Install_MissingFile_CreatesAllFiveHooks()
{
    var installer = new SettingsJsonInstaller(_tempSettings);
    installer.Install("C:\\bridge.exe");

    var contents = File.ReadAllText(_tempSettings);
    StringAssert.Contains(contents, "Notification");
    StringAssert.Contains(contents, "UserPromptSubmit");
    StringAssert.Contains(contents, "Stop");
    StringAssert.Contains(contents, "PostToolUse");
    StringAssert.Contains(contents, "SessionEnd");
}

[TestMethod]
public void GetStatus_AfterInstall_ReportsAllFiveInstalledHere()
{
    var installer = new SettingsJsonInstaller(_tempSettings);
    installer.Install("C:\\bridge.exe");

    var status = installer.GetStatus("C:\\bridge.exe");
    Assert.AreEqual(InstallState.InstalledHere, status.Notification);
    Assert.AreEqual(InstallState.InstalledHere, status.UserPromptSubmit);
    Assert.AreEqual(InstallState.InstalledHere, status.Stop);
    Assert.AreEqual(InstallState.InstalledHere, status.PostToolUse);
    Assert.AreEqual(InstallState.InstalledHere, status.SessionEnd);
}
```

Also update the existing `Install_MissingFile_CreatesWithThreeHooks` test method name and body to reflect five hooks — or leave it as-is (it still passes; "three" in the name refers to the original count). Recommend renaming for accuracy:

In the existing test, change the method name `Install_MissingFile_CreatesWithThreeHooks` → `Install_MissingFile_CreatesNotificationHook` (narrow it to the hook it specifically asserts about), keeping the existing assertions.

- [ ] **Step 2: Run tests; confirm new ones fail**

```bash
dotnet test ClaudeCycler.Core.Tests --filter "SettingsJsonInstallerTests"
```

Expected: the two new tests FAIL — `EventInstallStatus` doesn't have `PostToolUse` / `SessionEnd` properties yet, and `Install` only writes three event names.

- [ ] **Step 3: Extend `EventInstallStatus`**

In `ClaudeCycler.Core/SettingsJsonInstaller.cs`, replace the `EventInstallStatus` class with:

```csharp
public sealed class EventInstallStatus
{
    public InstallState Notification { get; set; }
    public InstallState UserPromptSubmit { get; set; }
    public InstallState Stop { get; set; }
    public InstallState PostToolUse { get; set; }
    public InstallState SessionEnd { get; set; }

    public string? NotificationPath { get; set; }
    public string? UserPromptSubmitPath { get; set; }
    public string? StopPath { get; set; }
    public string? PostToolUsePath { get; set; }
    public string? SessionEndPath { get; set; }
}
```

- [ ] **Step 4: Extend `EventNames` and the per-event status switch**

In the same file, change the `EventNames` field to include the two new events:

```csharp
static readonly string[] EventNames =
    { "Notification", "UserPromptSubmit", "Stop", "PostToolUse", "SessionEnd" };
```

In `GetStatus`, extend the inner switch on `eventName` to cover the new events:

```csharp
switch (eventName)
{
    case "Notification":
        status.Notification = state;
        status.NotificationPath = path;
        break;
    case "UserPromptSubmit":
        status.UserPromptSubmit = state;
        status.UserPromptSubmitPath = path;
        break;
    case "Stop":
        status.Stop = state;
        status.StopPath = path;
        break;
    case "PostToolUse":
        status.PostToolUse = state;
        status.PostToolUsePath = path;
        break;
    case "SessionEnd":
        status.SessionEnd = state;
        status.SessionEndPath = path;
        break;
}
```

- [ ] **Step 5: Run tests; confirm all pass**

```bash
dotnet test ClaudeCycler.Core.Tests --filter "SettingsJsonInstallerTests"
```

Expected: all green.

- [ ] **Step 6: Commit**

```bash
git add ClaudeCycler.Core/SettingsJsonInstaller.cs \
        ClaudeCycler.Core.Tests/SettingsJsonInstallerTests.cs
git commit -m "feat(cycler): extend installer to register PostToolUse + SessionEnd hooks"
```

---

## Task 6: Update `CheckCommand` to print and gate on all five hooks

Keeps the developer CLI consistent with the installer's expanded coverage.

**Files:**
- Modify: `ClaudeHookBridge/Commands/CheckCommand.cs`

(No new test — this is presentational output. The existing `CheckCommand` has no tests; matching that pattern.)

- [ ] **Step 1: Replace the entire `Run()` method**

In `ClaudeHookBridge/Commands/CheckCommand.cs`, replace the body of `CheckCommand.Run()`:

```csharp
public static int Run()
{
    var bridgeExePath = Environment.ProcessPath ?? "";
    var installer = new SettingsJsonInstaller();
    var status = installer.GetStatus(bridgeExePath);

    Console.WriteLine($"Bridge exe:     {bridgeExePath}");
    Console.WriteLine($"Settings file:  {Paths.ClaudeSettingsFile}");
    Console.WriteLine();
    Print("Notification",     status.Notification,     status.NotificationPath);
    Print("UserPromptSubmit", status.UserPromptSubmit, status.UserPromptSubmitPath);
    Print("Stop",             status.Stop,             status.StopPath);
    Print("PostToolUse",      status.PostToolUse,      status.PostToolUsePath);
    Print("SessionEnd",       status.SessionEnd,       status.SessionEndPath);

    var allInstalled =
        status.Notification == InstallState.InstalledHere &&
        status.UserPromptSubmit == InstallState.InstalledHere &&
        status.Stop == InstallState.InstalledHere &&
        status.PostToolUse == InstallState.InstalledHere &&
        status.SessionEnd == InstallState.InstalledHere;

    return allInstalled ? 0 : 2;
}
```

The `Print(...)` helper at the bottom of the file is unchanged.

- [ ] **Step 2: Build the bridge to make sure it still compiles**

```bash
MSBuild.exe ClaudeHookBridge\ClaudeHookBridge.csproj -p:Configuration=Debug
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add ClaudeHookBridge/Commands/CheckCommand.cs
git commit -m "feat(cycler): show PostToolUse + SessionEnd in check command"
```

---

## Task 7: Create `SessionLivenessVerifier`

Pure-function class that compares an entry's `NotifiedAt` against the transcript file's `LastWriteTimeUtc`. Lives in `ClaudeCycler.Core` (cross-platform; tested with real temp files matching the existing test style).

**Files:**
- Create: `ClaudeCycler.Core/SessionLivenessVerifier.cs`
- Create: `ClaudeCycler.Core.Tests/SessionLivenessVerifierTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `ClaudeCycler.Core.Tests/SessionLivenessVerifierTests.cs`:

```csharp
using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core.Tests;

[TestClass]
public class SessionLivenessVerifierTests
{
    string _tempTranscript = "";

    [TestInitialize]
    public void Setup()
    {
        _tempTranscript = Path.Combine(Path.GetTempPath(), $"transcript-{Guid.NewGuid():N}.jsonl");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempTranscript)) File.Delete(_tempTranscript);
    }

    [TestMethod]
    public void IsStillWaiting_TranscriptMissing_ReturnsFalse()
    {
        var verifier = new SessionLivenessVerifier();
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        Assert.IsFalse(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void IsStillWaiting_TranscriptPathNull_ReturnsFalse()
    {
        var verifier = new SessionLivenessVerifier();
        var entry = new SessionEntry
        {
            TranscriptPath = null,
            NotifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        Assert.IsFalse(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void IsStillWaiting_TranscriptOlderThanNotification_ReturnsTrue()
    {
        File.WriteAllText(_tempTranscript, "{}");
        var transcriptModTime = File.GetLastWriteTimeUtc(_tempTranscript);

        var verifier = new SessionLivenessVerifier();
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = new DateTimeOffset(transcriptModTime.AddSeconds(10), TimeSpan.Zero)
        };

        Assert.IsTrue(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void IsStillWaiting_TranscriptWithinGraceWindow_ReturnsTrue()
    {
        File.WriteAllText(_tempTranscript, "{}");
        var transcriptModTime = File.GetLastWriteTimeUtc(_tempTranscript);

        var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(5));
        // Notification fired 2 seconds before the transcript's last touch — within grace.
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = new DateTimeOffset(transcriptModTime.AddSeconds(-2), TimeSpan.Zero)
        };

        Assert.IsTrue(verifier.IsStillWaiting(entry));
    }

    [TestMethod]
    public void IsStillWaiting_TranscriptTouchedAfterGraceWindow_ReturnsFalse()
    {
        File.WriteAllText(_tempTranscript, "{}");
        var transcriptModTime = File.GetLastWriteTimeUtc(_tempTranscript);

        var verifier = new SessionLivenessVerifier(grace: TimeSpan.FromSeconds(5));
        // Notification fired 30 seconds before the transcript's last touch — well past grace.
        var entry = new SessionEntry
        {
            TranscriptPath = _tempTranscript,
            NotifiedAt = new DateTimeOffset(transcriptModTime.AddSeconds(-30), TimeSpan.Zero)
        };

        Assert.IsFalse(verifier.IsStillWaiting(entry));
    }
}
```

- [ ] **Step 2: Run; confirm all five tests fail to compile**

```bash
dotnet test ClaudeCycler.Core.Tests --filter "SessionLivenessVerifierTests"
```

Expected: build error — `SessionLivenessVerifier` does not exist.

- [ ] **Step 3: Create `SessionLivenessVerifier`**

Create `ClaudeCycler.Core/SessionLivenessVerifier.cs`:

```csharp
using ClaudeCycler.Core.Models;

namespace ClaudeCycler.Core;

public sealed class SessionLivenessVerifier
{
    static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(5);

    readonly TimeSpan _grace;

    public SessionLivenessVerifier(TimeSpan? grace = null)
    {
        _grace = grace ?? DefaultGrace;
    }

    public bool IsStillWaiting(SessionEntry entry)
    {
        if (string.IsNullOrEmpty(entry.TranscriptPath) || !File.Exists(entry.TranscriptPath))
        {
            return false;
        }

        var transcriptTouchedAt = File.GetLastWriteTimeUtc(entry.TranscriptPath);
        var threshold = entry.NotifiedAt.UtcDateTime + _grace;
        return transcriptTouchedAt <= threshold;
    }
}
```

- [ ] **Step 4: Run; confirm all five pass**

```bash
dotnet test ClaudeCycler.Core.Tests --filter "SessionLivenessVerifierTests"
```

Expected: all green.

- [ ] **Step 5: Commit**

```bash
git add ClaudeCycler.Core/SessionLivenessVerifier.cs \
        ClaudeCycler.Core.Tests/SessionLivenessVerifierTests.cs
git commit -m "feat(cycler): add SessionLivenessVerifier

Compares an entry's NotifiedAt against the transcript file's last
write time (with a configurable grace window). If the transcript has
been touched since the notification, the prompt is treated as
resolved. Pure file-metadata check; no JSONL parsing."
```

---

## Task 8: Wire the verifier into `ClaudeWindowService.CycleToNext`

Insert the liveness check between cwd-match and `candidates.Add`. Stale entries get deleted from the state file as a side effect.

**Files:**
- Modify: `MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs`

(No new unit test — `ClaudeWindowService` lives in the MAUI Windows-only project, which depends on `MegaSchoen.UITests`-style infrastructure that doesn't currently exist for this service. Coverage comes from the manual smoke test in Task 11. The verifier itself is fully unit-tested in Task 7.)

- [ ] **Step 1: Add the verifier field and use it in `CycleToNext`**

In `MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs`, add a `SessionLivenessVerifier` field next to `_store`, and modify the loop that builds `candidates`:

Replace the file contents with:

```csharp
using ClaudeCycler.Core;

namespace MegaSchoen.Platforms.Windows.Services;

sealed class ClaudeWindowService
{
    readonly TrayIconService _tray;
    readonly StateStore _store = new();
    readonly SessionLivenessVerifier _verifier = new();
    IntPtr _lastFocused = IntPtr.Zero;

    public ClaudeWindowService(TrayIconService tray)
    {
        _tray = tray;
    }

    public void CycleToNext()
    {
        var file = _store.Read();
        if (file.Sessions.Count == 0)
        {
            _tray.ShowNotification("MegaSchoen", "No Claude windows waiting", NotificationIcon.Info);
            return;
        }

        var windows = ProcessResolver.EnumerateCmdExeWindows();
        var candidates = new List<(string SessionId, CmdWindow Window, DateTimeOffset NotifiedAt)>();
        var matchedSessionIds = new HashSet<string>();
        foreach (var (id, entry) in file.Sessions)
        {
            if (!_verifier.IsStillWaiting(entry))
            {
                _store.Delete(id);
                continue;
            }

            foreach (var window in windows)
            {
                if (CwdMatches(window.WorkingDirectory, entry.Cwd))
                {
                    candidates.Add((id, window, entry.NotifiedAt));
                    matchedSessionIds.Add(id);
                }
            }
        }

        foreach (var id in file.Sessions.Keys)
        {
            if (!matchedSessionIds.Contains(id) && file.Sessions.ContainsKey(id))
            {
                _store.Delete(id);
            }
        }

        if (candidates.Count == 0)
        {
            _tray.ShowNotification("MegaSchoen", "No live Claude windows waiting", NotificationIcon.Info);
            return;
        }

        candidates.Sort((a, b) => a.NotifiedAt.CompareTo(b.NotifiedAt));

        var lastIndex = candidates.FindIndex(c => c.Window.WindowHandle == _lastFocused);
        var nextIndex = (lastIndex + 1) % candidates.Count;
        var next = candidates[nextIndex];

        Win32ForegroundHelper.BringToFront(next.Window.WindowHandle);
        _lastFocused = next.Window.WindowHandle;
    }

    static bool CwdMatches(string? windowCwd, string sessionCwd) =>
        windowCwd is not null
        && string.Equals(
            windowCwd.TrimEnd('\\'),
            sessionCwd.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
}
```

Notes for the implementer:
- The `continue` after `_store.Delete(id)` skips the cwd-match loop for verifier-rejected entries, so they never enter `matchedSessionIds` and never become candidates.
- The second `foreach` loop (orphan cleanup for entries with no matching window) needs the `file.Sessions.ContainsKey(id)` re-check because the verifier loop may have already deleted some entries, which would mutate `file.Sessions` if `Delete` happened to refresh it. Belt-and-suspenders.
- The `using ClaudeCycler.Core;` import already exists in this file — no change to imports.

- [ ] **Step 2: Build the MAUI project**

```bash
MSBuild.exe MegaSchoen\MegaSchoen.csproj -p:Configuration=Debug
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add MegaSchoen/Platforms/Windows/Services/ClaudeWindowService.cs
git commit -m "feat(cycler): verify entry liveness via transcript modtime at cycle time

Each entry now goes through SessionLivenessVerifier before becoming
a focus candidate. Verifier-rejected entries are deleted from state
in the same pass, keeping the state file self-cleaning."
```

---

## Task 9: Switch the global hotkey from Ctrl+Alt+9 to Ctrl+Alt+Tab

Assumes Task 1 confirmed `Ctrl+Alt+Tab` is registerable. If Task 1 chose a fallback chord, substitute it for `"Tab"` below.

**Files:**
- Modify: `MegaSchoen/Platforms/Windows/App.xaml.cs:135`

- [ ] **Step 1: Change the hotkey registration**

In `MegaSchoen/Platforms/Windows/App.xaml.cs`, line 135 currently reads:

```csharp
hotkeys.RegisterNamedHotkey("claude-cycle", "9", new[] { "Control", "Alt" });
```

Change to:

```csharp
hotkeys.RegisterNamedHotkey("claude-cycle", "Tab", new[] { "Control", "Alt" });
```

- [ ] **Step 2: Build**

```bash
MSBuild.exe MegaSchoen\MegaSchoen.csproj -p:Configuration=Debug
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add MegaSchoen/Platforms/Windows/App.xaml.cs
git commit -m "feat(cycler): bind Ctrl+Alt+Tab to claude-cycle"
```

---

## Task 10: Add "Clear Needy Sessions" tray menu item

Manual escape hatch for the rare cases when the verifier can't help (e.g. abrupt kill leaves an entry whose transcript was untouched).

**Files:**
- Modify: `MegaSchoen/Platforms/Windows/Services/TrayIconService.cs`
- Modify: `MegaSchoen/Platforms/Windows/App.xaml.cs`

- [ ] **Step 1: Add menu ID, event, and menu insertion in `TrayIconService`**

In `TrayIconService.cs`, add a new const next to `MenuIdInstallClaudeHooks`:

```csharp
const int MenuIdClearNeedyClaude = 1004;
```

Add a new event next to `CycleClaudeRequested`:

```csharp
public event EventHandler? ClearNeedyClaudeRequested;
```

In `ShowContextMenu`, after the existing `MenuIdInstallClaudeHooks` insert (currently right before the separator), add:

```csharp
InsertMenu(hMenu, position++, MF_STRING, MenuIdClearNeedyClaude, "Clear Needy Sessions");
```

So that block becomes:

```csharp
InsertMenu(hMenu, position++, MF_STRING, MenuIdOpen, "Open MegaSchoen");
InsertMenu(hMenu, position++, MF_STRING, MenuIdCycleClaude, "Cycle Claude Now");
InsertMenu(hMenu, position++, MF_STRING, MenuIdInstallClaudeHooks, "Install Claude Hooks");
InsertMenu(hMenu, position++, MF_STRING, MenuIdClearNeedyClaude, "Clear Needy Sessions");
InsertMenu(hMenu, position++, MF_SEPARATOR, 0, null);
InsertMenu(hMenu, position, MF_STRING, MenuIdExit, "Exit");
```

In `HandleMenuCommand`, add a branch alongside the existing `MenuIdInstallClaudeHooks` branch:

```csharp
else if (cmd == MenuIdClearNeedyClaude)
{
    ClearNeedyClaudeRequested?.Invoke(this, EventArgs.Empty);
}
```

- [ ] **Step 2: Wire the event in `App.xaml.cs`**

In `MegaSchoen/Platforms/Windows/App.xaml.cs`, after the existing `tray.CycleClaudeRequested += ...` handler block (lines 104-115), add:

```csharp
tray.ClearNeedyClaudeRequested += (s, e) =>
{
    try
    {
        var store = new ClaudeCycler.Core.StateStore();
        store.Write(new ClaudeCycler.Core.Models.NeedySessionsFile());
        tray.ShowNotification("MegaSchoen", "Needy sessions cleared");
    }
    catch (Exception exception)
    {
        ClaudeCycler.Core.Logger.Log($"ClearNeedyClaude threw: {exception}");
        tray.ShowNotification("MegaSchoen", $"Clear failed: {exception.Message}", NotificationIcon.Error);
    }
};
```

- [ ] **Step 3: Build**

```bash
MSBuild.exe MegaSchoen\MegaSchoen.csproj -p:Configuration=Debug
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add MegaSchoen/Platforms/Windows/Services/TrayIconService.cs \
        MegaSchoen/Platforms/Windows/App.xaml.cs
git commit -m "feat(cycler): add 'Clear Needy Sessions' tray menu item

User-facing escape hatch for the rare cases where automatic
verification leaves stale state — e.g. abrupt session kills."
```

---

## Task 11: Manual smoke test

Per the spec's smoke-test plan. No automated test covers the end-to-end MAUI + tray + hotkey + bridge integration; this is the verification step.

**Files:** none modified.

- [ ] **Step 1: Stop any running MegaSchoen instance**

Right-click the tray icon → Exit, or:

```powershell
Get-Process MegaSchoen -ErrorAction SilentlyContinue | Stop-Process
```

- [ ] **Step 2: Build the whole solution**

```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug
```

Expected: 0 errors. Note any warnings but proceed.

- [ ] **Step 3: Run the freshly built app**

```bash
"./MegaSchoen/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/MegaSchoen.exe"
```

Expected: app launches, tray icon appears.

- [ ] **Step 4: Re-install hooks**

Right-click the tray icon → "Install Claude Hooks". Then verify all five are present:

```bash
"./MegaSchoen/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/ClaudeHookBridge.exe" check
```

Expected output includes `INSTALLED (this binary)` for all five of: Notification, UserPromptSubmit, Stop, PostToolUse, SessionEnd. Exit code 0.

- [ ] **Step 5: Trigger a permission prompt**

In any cmd.exe Claude session, prompt Claude to do something that needs permission (e.g. "run `git log` for me"). When the prompt appears, in another shell run:

```bash
"./MegaSchoen/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/ClaudeHookBridge.exe" status
```

Expected: one entry, `cwd` matching the session's working directory, age < 1 minute.

- [ ] **Step 6: Verify `Ctrl+Alt+Tab` cycles to the prompting window**

Press `Ctrl+Alt+Tab`. Expected: the prompting cmd.exe comes to the foreground.

- [ ] **Step 7: Approve the prompt and verify the entry clears**

Press `1` to approve. Wait ~5 seconds for `PostToolUse` to fire. Re-run `status`. Expected: 0 entries.

- [ ] **Step 8: Test the verifier on a stale entry**

Hand-write a stale entry into the state file. Pick a real recent transcript from `~/.claude/projects/`:

```bash
LATEST_TRANSCRIPT=$(ls -t ~/.claude/projects/*/*.jsonl | head -1)
echo "Using transcript: $LATEST_TRANSCRIPT"
cat > "$LOCALAPPDATA/MegaSchoen/needy-sessions.json" <<EOF
{
  "version": 1,
  "sessions": {
    "fake-stale-session": {
      "cwd": "C:\\\\Users\\\\$USER",
      "transcriptPath": "$(echo "$LATEST_TRANSCRIPT" | sed 's/\\\\/\\\\\\\\/g')",
      "notifiedAt": "2025-01-01T00:00:00Z",
      "message": "Stale test entry"
    }
  }
}
EOF
```

Press `Ctrl+Alt+Tab`. Expected: tray notification "No live Claude windows waiting" (the verifier rejected the stale entry because the transcript was touched after the year-old `notifiedAt`). Re-run `status`: 0 entries (verifier deleted it as a side effect).

- [ ] **Step 9: Test "Clear Needy Sessions" tray item**

Trigger a real permission prompt (don't approve it). Verify `status` shows the entry. Right-click the tray icon → "Clear Needy Sessions". Re-run `status`: 0 entries. Verify the tray balloon "Needy sessions cleared" appeared.

- [ ] **Step 10: Test SessionEnd**

Trigger a permission prompt. Without approving, type `/exit` to close the Claude session. Run `status` — entry should be gone (cleared by `SessionEnd`).

- [ ] **Step 11: Update spec spike notes**

Add a short note to `docs/superpowers/specs/2026-04-24-claude-window-cycler-needy-verification-design.md` under "Open implementation questions (spike early)" indicating which spikes were resolved how. Sketch:

> **Resolved during implementation (2026-04-25):** Spike 1 obviated — `HookPayload.transcript_path` is already populated by Claude Code, so the cwd-encoding rule is unnecessary; the path is stored on `SessionEntry` and read directly. Spike 2 confirmed — transcripts are touched within ~1s of every turn-progression event. Spike 3 confirmed — `PostToolUse` payload includes `session_id`. Spike 4 confirmed — `Ctrl+Alt+Tab` registers cleanly on Windows 11 26200.

- [ ] **Step 12: Commit smoke-test artifacts**

```bash
git add docs/superpowers/specs/2026-04-24-claude-window-cycler-needy-verification-design.md
git commit -m "docs: mark cycler verification spikes resolved post-implementation"
```

---

## Self-review notes

This section is a record of the writing-plans self-review that was run before handoff; it is not an implementer task.

**Spec coverage:** Every spec section has at least one task — broader hooks (Tasks 3, 4, 5), verifier (Tasks 7, 8), tray escape hatch (Task 10), hotkey change (Task 9), spike items (Task 1, plus Task 11 step 11). Edge case "abrupt kill leaves stale state" is covered by Task 10's escape hatch.

**Placeholder scan:** No "TBD" / "implement later" / "appropriate error handling" / vague test descriptions. Every code step shows the actual code; every command step shows the exact command and expected output.

**Type consistency:** `SessionLivenessVerifier.IsStillWaiting(SessionEntry entry)` signature matches across Tasks 7 and 8. `EventInstallStatus`'s new fields (`PostToolUse`, `SessionEnd`, plus `*Path`) match between Task 5's class definition, Task 5's switch update, and Task 6's `CheckCommand` reads. `MenuIdClearNeedyClaude` constant name matches across Task 10's three edits in the same file.

**Spec simplification noted:** Spec spike 1 (transcript path encoding) is invalidated by `HookPayload.TranscriptPath` already existing on the payload. Plan documents this in Task 11 step 11 instead of pretending the spike is still needed.
