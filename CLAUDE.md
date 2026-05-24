# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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
- Session enumeration: `claude.exe` processes (filtered to those with a shell parent: `cmd.exe` / `powershell.exe` / `pwsh.exe`) each attributed to a specific `~/.claude/projects/<slug>/*.jsonl` by matching the process's `StartTimeUtc` to the JSONL's `CreationTimeUtc` within 30s (freshest-unassigned fallback for `claude --resume`), with subagent rollup
- State classification: StateStore presence is the discriminator (no time gates) — `PendingPermission`/`AwaitingInput` from per-session files under `%LOCALAPPDATA%\MegaSchoen\needy-sessions\<sessionId>.json`, `Working`/`Idle` from transcript tail-read
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
- `Claude.Core/ActiveSessionEnumerator.cs` - The window ⨝ slug-JSONL ⨝ StateStore join
- `Claude.Core/SessionStateClassifier.cs` - Pure-function state mapping (StateStore presence is the discriminator; no time gates)
- `Claude.Core/SlugEncoder.cs` - cwd → `~/.claude/projects/<slug>` directory naming
- `Claude.Core/Windows/WindowsClaudeProcessLocator.cs` - claude.exe process enumeration (parent-shell filter via `ProcessResolver`); each claude.exe's parent cmd.exe is then mapped to its visible terminal window for focus
- `Claude.Core/Windows/WindowsClaudeWindowFocuser.cs` - `BringToFront` via `Win32ForegroundHelper` (three-way `AttachThreadInput`)
- `ClaudeSessionsCLI/Commands/ListCommand.cs` - Three-mode list (human/JSON/NDJSON)
- `ClaudeSessionsCLI/Commands/FocusCommand.cs` - Unique-prefix focus
- `MegaSchoen/AppShell.xaml` - Two-entry flyout (Display Manager + Claude Sessions)
- `MegaSchoen/SessionsPage.xaml(.cs)` - Sessions UI; `OnAppearing` calls `_viewModel.Start()`, `OnDisappearing` calls `Dispose`
- `MegaSchoen/ViewModels/SessionsPageViewModel.cs` - FileSystemWatcher → bounded `Channel<byte>` → 250ms debounce → re-enumerate; idempotent `Start()`
- `MegaSchoen/ViewModels/DisplayManagerPageViewModel.cs` - Display Manager UI logic

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
