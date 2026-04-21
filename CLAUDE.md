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
- `DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe` — CLI (AnyCPU)
- `ClaudeHookBridge\bin\Debug\net10.0-windows10.0.26100.0\ClaudeHookBridge.exe` — hook bridge (AnyCPU)
- Library DLLs at `<project>\bin\Debug\...` (AnyCPU)

If `MegaSchoen\bin\Debug\` ever reappears, something has bypassed the solution mappings (e.g., a direct `dotnet build MegaSchoen.csproj`). It should not be produced by any normal workflow.

### Running the CLI
```bash
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" list              # List all displays
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" save "My Profile" # Save current config
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" load "My Profile" # Load a profile
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" profiles          # List all profiles
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" raw               # Show raw JSON
```

## Current Status (Last updated: 2026-03-24)

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

**Priority 1: Test GPU Swap Survival**
- Verify EDID matching and topology database survive adapter LUID changes after GPU swap

**Priority 2: Identical Model Disambiguation**
- Handle case where user has multiple monitors of the same model (deferred)

## Architecture Overview

### Core Components

- **DisplayManagerNative** (C++ DLL) - Native Windows CCD API wrapper. Uses `QueryDisplayConfig` and `SetDisplayConfig` for display enumeration and control. Exports JSON-based display information.

- **DisplayManager.Core** (.NET 10 Library) - Managed wrapper around the native DLL via P/Invoke. Contains DisplayManager static class, DisplayInfo model, and profile services.

- **DisplayManagerCLI** (.NET 10 Console App) - Command-line interface. Commands: list, apply, save, load, profiles, delete, config, raw.

- **MegaSchoen** (MAUI App) - Cross-platform GUI application. Currently Windows-only for display management features.

### Key Files

- `DisplayManagerNative/DisplayManagerNative.cpp` - Native CCD API implementation
- `DisplayManager.Core/DisplayManager.cs` - P/Invoke wrappers
- `DisplayManager.Core/DisplayInfo.cs` - Display data model
- `DisplayManager.Core/Services/DisplayProfileService.cs` - Profile save/load/apply
- `DisplayManagerCLI/Program.cs` - CLI commands
- `MegaSchoen/ViewModels/MainPageViewModel.cs` - MAUI UI logic

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
