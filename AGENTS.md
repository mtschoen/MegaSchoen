# MegaSchoen

## Coding Conventions

These conventions are enforced by `.editorconfig` and must be followed when writing code.

### C# Style

- **Use `var` everywhere** - Always use `var` instead of explicit types
- **Use file-scoped namespaces** - `namespace Foo;` not `namespace Foo { }`
- **Use language keywords** - `string`, `int`, `bool` not `String`, `Int32`, `Boolean`
- **Omit default access modifiers** - Don't write `private` for members or `internal` for types
- **No `this.` qualification** - Omit `this.` for fields, properties, methods, events
- **Use expression-bodied members** for simple accessors and properties
- **Use pattern matching** - Prefer `is null`, `is not null`, pattern matching over casts
- **Use null propagation** - `foo?.Bar` and `foo ?? default`
- **Use collection/object initializers** where applicable
- **Always use braces** for control flow statements
- **Private fields use camelCase** - `int _count;` (no `private` keyword)
- **Interfaces start with I** - `IDisplayService`
- **Types, methods, properties use PascalCase**

```csharp
// Good
class MyService                    // implicit internal
{
    readonly string _name;         // implicit private

    void DoWork() { }              // implicit private
    public void DoPublicWork() { } // explicit public (required)
}

// Bad
internal class MyService
{
    private readonly string _name;
    private void DoWork() { }
}
```

## Build Commands

Use MSBuild (not `dotnet build` — it can't build the native C++ dependency).

```bash
# Build the CLI
MSBuild.exe DisplayManagerCLI\DisplayManagerCLI.csproj -p:Configuration=Debug

# Build entire solution
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug
```

**Use Visual Studio 18's MSBuild, not VS 2022's.** Only VS 18 ships the v145 native toolset *and* the .NET 10 SDK this solution needs; VS 2022's MSBuild resolves SDK 9.0.x and lacks v145, so it fails on both `DisplayManagerNative.vcxproj` (MSB8020 "v145 not found") and every net10 project (NETSDK1045 "does not support .NET 10"). Discover the right one with `vswhere -latest` — the VS install folder scheme flipped from year-based (`2022`) to sequential (`18`), so a plain numeric folder sort wrongly ranks `2022` above `18`. Build the **solution** (`MegaSchoen.sln`), never the bare `MegaSchoen.csproj` (it fans out to all four TFMs — android/ios/maccatalyst/windows — and fails). The `screenshot-editor.ps1` pipeline encodes this discovery.

**`-p:Platform=x64` is no longer required.** The `.sln` maps every solution-level platform selection correctly: MegaSchoen (MAUI) always builds as `x64` (needed by `WindowsAppSDKSelfContained`), library projects always build as `AnyCPU`, and `DisplayManagerNative` always builds as `x64`. Passing the flag is harmless but redundant. IDE F5 also produces the right outputs without any config tweaks.

Output locations after a successful build:

- `MegaSchoen\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\MegaSchoen.exe` — **the MAUI app (authoritative path)**
- `DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe` — display-management CLI (AnyCPU)
- `ClaudeSessionsCLI\bin\Debug\net10.0-windows10.0.26100.0\ClaudeSessionsCLI.exe` — active-Claude-sessions CLI (AnyCPU, Windows TFM)
- `ClaudeHookBridge\bin\Debug\net10.0-windows10.0.26100.0\ClaudeHookBridge.exe` — hook bridge (AnyCPU)
- Library DLLs at `<project>\bin\Debug\...` (AnyCPU)

If `MegaSchoen\bin\Debug\` ever reappears, something has bypassed the solution mappings (e.g., a direct `dotnet build MegaSchoen.csproj`). It should not be produced by any normal workflow.

### Running the Display Manager CLI
```bash
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" list              # List all displays
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" save "My Profile" # Save current config
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" load "My Profile" # Load a profile
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" profiles          # List all profiles
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" raw               # Show raw JSON
```

### Running the Claude Sessions CLI
```bash
".\ClaudeSessionsCLI\bin\Debug\net10.0-windows10.0.26100.0\ClaudeSessionsCLI.exe" list                    # Spectre.Console live table (refreshes on FileSystemWatcher events)
".\ClaudeSessionsCLI\bin\Debug\net10.0-windows10.0.26100.0\ClaudeSessionsCLI.exe" list --json             # One-shot JSON snapshot, exit
".\ClaudeSessionsCLI\bin\Debug\net10.0-windows10.0.26100.0\ClaudeSessionsCLI.exe" list --json-stream      # NDJSON, one snapshot per --interval (default 1.5s)
".\ClaudeSessionsCLI\bin\Debug\net10.0-windows10.0.26100.0\ClaudeSessionsCLI.exe" focus <session-prefix>  # Bring matching window to foreground
```
Default `list` is pipe-aware: when stdout is redirected (e.g., `... | clip`) it emits one-shot JSON instead of an ANSI live table.

## Current Status (Last updated: 2026-05-23)

### ✅ Active Claude Sessions Dashboard Working

**What's Working:**
- New `ClaudeSessionsCLI` binary with `list` (one-shot JSON / NDJSON stream / Spectre.Console live table) and `focus <prefix>` verbs
- New MAUI Sessions tab (sibling to Display Manager via AppShell flyout) showing per-session cards: state badge, cwd, last-activity, Focus button, plus optional Refresh button
- Session enumeration is **source-of-truth-first** (inverted 2026-05-28, issue #9): session *identity* is the StateStore key / transcript filename (the real `session_id`) — never a guess about which terminal window owns which transcript. Live `claude.exe` processes (grouped by real cwd) serve two roles only: (a) a **liveness gate** — a session is live iff a claude process runs in its cwd, so waiting-on-user sessions stay listed with no timer (a blocked Claude is still a live process); and (b) a **best-effort window attach** for the Focus button (a process whose `StartTimeUtc` is within 30s of the transcript's creation, used ONLY to pick a window — never for identity). Windowless/headless sessions (`claude -p`, unresolved `--resume`) surface with `Window.IsZero` and a disabled Focus rather than being dropped. Shared-cwd is bounded by capping each cwd to its live-process count (freshest-by-mtime, a relative rank — no time cutoff). Subagent rollup retained. Slug collisions are disambiguated by the transcript's recorded first-line `cwd`.
- State classification is event-driven and authoritative (no time gates): every state-relevant hook event upserts the session's current state into a per-session file under `%LOCALAPPDATA%\MegaSchoen\needy-sessions\<sessionId>.json` (`permission_prompt`→`PendingPermission`; `Stop`/`idle_prompt`→`AwaitingInput`; `UserPromptSubmit`/`PreToolUse`/`PostToolUse`→`Working`; `SessionEnd`→delete). `PostToolUse` is what clears a stale `PendingPermission` once an approved tool runs. The classifier maps the stored reason directly; transcript tail-read is only a fallback for sessions that have no state file yet (`Working`/`Idle`). The store therefore holds entries for **all** live sessions including `Working`, so the cycler's "any waiting" filter uses `WaitingReason.IsNeedy()` (Permission/AwaitingInput) to exclude Working.
- Refresh is event-driven: two `FileSystemWatcher`s (`needy-sessions/*.json` directory + recursive on `~/.claude/projects/`) funnel into a `Channel<byte>` with 250ms debounce; no polling
- State store is sharded one-file-per-session (changed 2026-05-23 from a single shared `needy-sessions.json`) so concurrent `ClaudeHookBridge.exe` invocations don't contend or drop events. A per-session named `Mutex` (`Local\\MegaSchoen.Session.<sessionId>`) serializes within-session writes. MAUI app does a one-time legacy-file delete + zombie sweep at startup (deletes per-session files whose session ID has no corresponding live transcript).

**Key Implementation Details:**
- `ClaudeCycler.Core` was renamed to `Claude.Core` in this same work (it now owns more than the cycler)
- Cycler debug section moved from DisplayManagerPage to SessionsPage — conceptually belongs with sessions
- Win32 platform code wrapped behind `IClaudeProcessLocator` / `IClaudeWindowFocuser` interfaces; non-Windows impls are TODO

### ✅ Display Toggle Working

**What's Working:**
- Display detection and listing via CCD API (QueryDisplayConfig)
- Cross-adapter display switching (multi-GPU) via SDC_TOPOLOGY_SUPPLIED
- EDID-based monitor matching (survives GPU swaps and port changes)
- Profile save/load with hotkey preservation on overwrite
- Global hotkeys, system tray, and startup support (MAUI app)
- Profiles stored in `%APPDATA%\MegaSchoen\configs.json`

**Key Implementation Details:**
- Profiles store EDID hardware IDs (manufacturerId, productCodeId, serialNumber) — no unstable system handles
- Display matching: cascading EDID match (container ID → model+serial → model+date)
- Apply uses `SDC_TOPOLOGY_SUPPLIED | SDC_ALLOW_PATH_ORDER_CHANGES` — Windows restores full config from its topology database
- Path selection picks non-conflicting (adapter, sourceId) pairs to avoid clone conflicts
- Requires each profile's layout to have been manually configured at least once via Windows Display Settings

### ✅ Visual Layout Editor (verified-before-commit)

A drag-to-arrange layout editor opens in its own window per preset (**✎ Edit** on each preset card → `Application.Current.OpenWindow`, multi-instance). It edits a *draft* of the preset and only writes it back after the layout has been applied to live hardware and read back with **no drift** — a preset is never overwritten by an untested layout.

**What's Working:**
- Canvas of draggable monitor rectangles (`AbsoluteLayout` + `PanGestureRecognizer`); tap to select, drag to move, optional edge **snapping**, and a **Normalize** button (one primary, primary anchored at (0,0), overlaps pushed right; footprint respects rotation).
- Per-monitor **Set as Primary**, **Rotation** (0/90/180/270), and an **Advanced** panel for **resolution + refresh** populated from the monitor's enumerated supported modes.
- **Test → Stash → Commit** action bar. **Test** applies the draft and compares live config to it; **Commit** is enabled only after a clean Test, and editing afterward re-disables it (hash invalidation). **Stash** saves the draft without touching the preset; commit is blocked if the target preset was deleted elsewhere.

**Key Implementation Details:**
- Native `ApplyConfiguration` Step 2 now patches **all** verified fields (position, resolution, rotation, refresh), not just position — so a resolution/rotation/refresh edit no longer reads back as drift. Position-only profiles are unaffected (resolution/refresh patched only when non-zero). New native export `GetSupportedModesJson` feeds the Advanced dropdowns (EDID → GDI device name → `EnumDisplaySettingsExW`).
- Managed core (all in `DisplayManager.Core`, unit-tested): `LayoutDraft` model; `LayoutHasher` (stable order-independent SHA-256 over exactly the drift-compared fields, EDID-keyed); `LayoutNormalizer` (pure overlap/primary/origin normalization); `LayoutDraftStore` (one JSON file per preset under `%APPDATA%\MegaSchoen\layout-drafts\`, separate from `configs.json`); `LayoutCommitService` (the test→verify→commit gate; apply + drift + preset-existence injected for testability). The "verified" stamp is `LayoutHasher.Hash(draft)` recorded on a clean Test; `CanCommit` = stamp non-empty && equals the current draft's hash.
- Verify gate reuses Phase-1 `DisplayDriftService.CompareToLive`.
- Known follow-ups (need the multi-GPU rig to validate): canvas drag uses a DIP-agnostic scale (may need a DPI density factor at non-100% scaling); `TestAsync` applies synchronously on the UI thread (a multi-second GPU switch will block the editor window).

### 🎯 Next Steps

**Priority 1: Test GPU Swap Survival** (Display Manager)
- Verify EDID matching and topology database survive adapter LUID changes after GPU swap

**Priority 2: Identical Model Disambiguation** (Display Manager)
- Handle case where user has multiple monitors of the same model (deferred)

**Priority 3: Cross-platform Sessions Dashboard**
- macOS / Linux impls of `IClaudeProcessLocator` / `IClaudeWindowFocuser` (interfaces in place; impls deferred)

## Architecture Overview

### Core Components

- **DisplayManagerNative** (C++ DLL) - Native Windows CCD API wrapper. Uses `QueryDisplayConfig` and `SetDisplayConfig` for display enumeration and control. Exports JSON-based display information.

- **DisplayManager.Core** (.NET 10 Library) - Managed wrapper around the native DLL via P/Invoke. Contains DisplayManager static class, DisplayInfo model, and profile services.

- **DisplayManagerCLI** (.NET 10 Console App) - Display CLI. Commands: list, apply, save, load, profiles, delete, config, raw.

- **Claude.Core** (.NET 10 Library) - Cycler + active-sessions primitives. Owns `StateStore` (`needy-sessions.json`), `SessionLivenessVerifier`, `SlugEncoder`, `SessionStateClassifier`, `ActiveSessionEnumerator`, the `IClaudeProcessLocator` / `IClaudeWindowFocuser` interfaces, and Windows impls (`WindowsClaudeProcessLocator`, `WindowsClaudeWindowFocuser`). Win32 interop in `Claude.Core/Interop/`. Was originally `ClaudeCycler.Core`; renamed when scope grew beyond cycling.

- **ClaudeSessionsCLI** (.NET 10 Console App, Windows TFM) - Active-Claude-sessions CLI. `list` (default human / `--json` / `--json-stream`) + `focus <prefix>`. Uses `Spectre.Console` for live table.

- **ClaudeHookBridge** (.NET 10 Console App) - Claude Code hook receiver. Spawned by hooks; writes one file per session under `%LOCALAPPDATA%\MegaSchoen\needy-sessions\<sessionId>.json` via `Claude.Core.StateStore`.

- **MegaSchoen** (MAUI App) - Cross-platform GUI. AppShell flyout with two pages: **Display Manager** (display profiles, save/apply/hotkeys) and **Claude Sessions** (live cards driven by `FileSystemWatcher` + bounded-channel debounce). Currently Windows-only for the active features.

### Key Files

- `DisplayManagerNative/DisplayManagerNative.cpp` - Native CCD API implementation
- `DisplayManager.Core/DisplayManager.cs` - P/Invoke wrappers
- `DisplayManager.Core/DisplayInfo.cs` - Display data model
- `DisplayManager.Core/Services/DisplayProfileService.cs` - Profile save/load/apply
- `DisplayManagerCLI/Program.cs` - Display CLI commands
- `Claude.Core/ActiveSessionEnumerator.cs` - Source-of-truth-first enumeration: (transcripts ∪ StateStore) keyed by real `session_id`, gated by process-presence liveness per cwd; best-effort window attach for Focus (identity never depends on it)
- `Claude.Core/SessionStateClassifier.cs` - Pure-function state mapping: stored `WaitingReason` → `SessionState` (Permission/AwaitingInput/Working), with transcript tail-read only as the no-state-file fallback
- `Claude.Core/HookDispatcher.cs` - Maps each Claude Code hook event to a state upsert/delete; churn-guarded so the per-tool `PostToolUse`/`PreToolUse` floods don't rewrite unchanged state
- `Claude.Core/SlugEncoder.cs` - cwd → `~/.claude/projects/<slug>` directory naming
- `Claude.Core/AncestorWindowResolver.cs` - pure process-ancestor → first-windowed-ancestor walk (shared by embedded-IDE and ssh Focus; see "Exotic-context session Focus" below)
- `Claude.Core/SshSessionWindowResolver.cs` - ssh client port → owning `ssh.exe` pid → hosting terminal window (pure orchestration, Win32 injected)
- `Claude.Core/Windows/WindowsClaudeProcessLocator.cs` - `EnumerateLiveSessions()`: every live claude.exe (via `ProcessResolver`), each mapped to its parent shell's terminal window when one exists; windowless processes are **kept** (emitted with `WindowToken.Null`), not dropped, so headless sessions still count for liveness
- `Claude.Core/Windows/WindowsClaudeWindowFocuser.cs` - `BringToFront` via `Win32ForegroundHelper` (three-way `AttachThreadInput`)
- `ClaudeSessionsCLI/Commands/ListCommand.cs` - Three-mode list (human/JSON/NDJSON)
- `ClaudeSessionsCLI/Commands/FocusCommand.cs` - Unique-prefix focus
- `MegaSchoen/AppShell.xaml` - Two-entry flyout (Display Manager + Claude Sessions)
- `MegaSchoen/SessionsPage.xaml(.cs)` - Sessions UI; `OnAppearing` calls `_viewModel.Start()`, `OnDisappearing` calls `Dispose`
- `MegaSchoen/ViewModels/SessionsPageViewModel.cs` - FileSystemWatcher → bounded `Channel<byte>` → 250ms debounce → re-enumerate; idempotent `Start()`
- `MegaSchoen/ViewModels/DisplayManagerPageViewModel.cs` - Display Manager UI logic

### Exotic-context session Focus

Focus works beyond the plain "claude in its own terminal window" case via two resolution paths sharing one primitive:

- **Shared primitive:** `AncestorWindowResolver.Resolve` walks the process-ancestor chain from a start pid to the first ancestor owning a visible top-level window. Pure and platform-neutral: Win32 is injected as delegates (parent-pid lookup + a prebuilt pid→window map), cycle-guarded and depth-bounded. A stop-set halts the climb *without* matching, so a standalone-terminal chain never resolves to explorer.exe (the desktop).
- **Embedded IDE terminals** (Rider / VS Code / devenv): claude descends from the IDE via a windowless shell, so the walk starts at the shell pid and climbs to the IDE's own top-level window.
- **Remote ssh sessions** (cmd/Windows Terminal → `ssh.exe` → remote claude): the Linux enumerator reads `/proc/<pid>/environ` for `SSH_CONNECTION` and serializes the client source port as `SshClientPort` on the NDJSON stream. Locally, `SshSessionWindowResolver` maps that port → owning pid via `TcpConnectionTable` (`GetExtendedTcpTable`), rejects any owner that is not `ssh` (stale port reuse), then ancestor-walks to the hosting terminal window. **Limitation:** claude inside remote `tmux`/`mosh` doesn't inherit `SSH_CONNECTION`, so those sessions list with Focus hidden (graceful degradation).

Rig-verified gotchas baked into the implementation (do not "simplify" these away):

- When a cwd has exactly one live process, `ActiveSessionEnumerator` attaches that process's window/title and carries its `SshClientPort` even when the 30s start-time match misses - the association is unambiguous. The match misses in two rig-verified ways: on Linux .NET, file *creation* time tracks mtime (drops the piggybacked port); on Windows, a freshly started claude creates no transcript until the first prompt, so the cwd's freshest *old* transcript surfaces as the session (its creation time is days from the process start - found via Rider, where this grayed out Focus; also fixes resumed sessions). Multi-process cwds stay best-effort: never guess a window or port there.
- `TcpConnectionTable` queries **both** the IPv4 and IPv6 owner-pid tables - ssh to a link-local host runs over IPv6, and the AF_INET table alone finds nothing.
- The resolver's window map merges `GetTerminalWindowsByCmdPid` (keyed by shell pid) into `GetVisibleTopLevelWindowsByPid` (keyed by root-owner pid): a cmd/pwsh hosting ssh owns only a ConPTY pseudoconsole window whose root owner is the terminal host, so without the merge the ssh→cmd walk dead-ends.

### Native API Functions

```cpp
// Get all display paths as JSON array (includes EDID fields)
int GetAllDisplaysJson(char* buffer, int bufferSize);

// Apply a display configuration
// Takes JSON array of SavedDisplayConfig objects with:
//   - EDID fields (edidManufactureId, edidProductCodeId, edidSerialNumber) for matching
//   - width, height, positionX, positionY, refreshRate, rotation
// Matches by EDID, selects non-conflicting CCD paths, applies via SDC_TOPOLOGY_SUPPLIED
int ApplyConfiguration(const char* configJson);
```

### Build Configuration Notes

- Native C++ project has both Win32 and x64 configurations; solution mappings always select x64
- Post-build events copy DLL to dependent project output directories
- All .NET projects target .NET 10
- C++ project requires Visual Studio 2022+ with Windows 10 SDK
- Solution-level platform selection (`Any CPU` / `x64` / `x86`) is honored by the `.sln`: MegaSchoen always → x64, libraries always → AnyCPU, native always → x64

## Quality gate: aislop

This project uses **aislop** as a deterministic quality gate for AI-written code
(narrative comments, swallowed exceptions, `as any`, dead stubs, oversized
functions, etc.) across TS/JS, Python, Go, Rust, Ruby, PHP, Java, and C#.

`aislop` is installed globally on this machine (pinned to the fork
`mtschoen/aislop`, which adds C#/roslynator support). Call the installed binary
directly — do NOT use `npx aislop`, which pulls upstream from npm with no C#
support:

- **Before declaring work complete**, run `aislop scan .` and address findings.
- **Before committing**, run `aislop scan --staged` (staged files only).
- `aislop fix` auto-clears mechanical issues (formatting, unused imports, dead
  code); `aislop fix --claude` hands the rest back with full context.
- `aislop ci .` is the gate — exits non-zero if the score drops below the
  threshold in `.aislop/config.yml`. Treat a failing gate like a failing test.

To refresh the pinned binary after new commits land on the fork branch:
`pnpm add -g --allow-build=aislop "github:mtschoen/aislop#feat/csharp-support"`
