# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Coding Conventions

These conventions are enforced by `.editorconfig` and must be followed when writing code.

### C# Style

- **Use `var` everywhere** - Always use `var` instead of explicit types
- **Use file-scoped namespaces** - `namespace Foo;` not `namespace Foo { }`
- **Use language keywords** - `string`, `int`, `bool` not `String`, `Int32`, `Boolean`
- **No `this.` qualification** - Omit `this.` for fields, properties, methods, events
- **Use expression-bodied members** for simple accessors and properties
- **Use pattern matching** - Prefer `is null`, `is not null`, pattern matching over casts
- **Use null propagation** - `foo?.Bar` and `foo ?? default`
- **Use collection/object initializers** where applicable
- **Always use braces** for control flow statements
- **Private fields use camelCase** - `private int count;`
- **Interfaces start with I** - `IDisplayService`
- **Types, methods, properties use PascalCase**

```csharp
// Good
var displays = GetDisplays();
var count = 5;
var name = displays?.FirstOrDefault()?.Name ?? "Unknown";

// Bad
List<DisplayInfo> displays = GetDisplays();
int count = 5;
string name = displays != null && displays.Count > 0 ? displays[0].Name : "Unknown";
```

## Build Commands

**ALWAYS use MSBuild with `-p:Platform=x64`** - the native DLL must be 64-bit.

```bash
# Build the CLI
MSBuild.exe DisplayManagerCLI\DisplayManagerCLI.csproj -p:Configuration=Debug -p:Platform=x64

# Build entire solution
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug -p:Platform=x64
```

Do NOT use `dotnet build` - it cannot build the native C++ dependency.

### Running the CLI
```bash
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" list              # List all displays
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" toggle DISPLAY5 off  # Toggle display off
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" toggle DISPLAY5 on   # Toggle display on
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" raw              # Show raw JSON
```

## Current Status (Last updated: 2026-01-19)

### 🔍 TODO: Review for Further Cleanup
User wants to review the codebase for more cleanup opportunities before continuing. Areas to potentially examine:
- Are there unused services or models?
- Can the profile system be simplified now that we use CCD API?
- Is ConfigurationManager redundant with DisplayProfileService?
- Any dead code paths in the MAUI ViewModel?

### ✅ CCD API Refactor Complete

**What's Working:**
- Display detection and listing via CCD API (QueryDisplayConfig)
- Individual display toggle on/off via CCD API (SetDisplayConfig)
- Toggle uses `SDC_USE_SUPPLIED_DISPLAY_CONFIG` which is less disruptive than the old `ChangeDisplaySettingsEx` approach
- MAUI app builds and runs on Windows
- CLI tool for testing display operations
- Profile save/load (profiles stored in `%APPDATA%\MegaSchoen\configs.json`)

**Recent Changes:**
- Removed legacy `EnumDisplayDevices`/`EnumDisplaySettings` code
- Removed `ApplyDisplayConfiguration` function (replaced by `ToggleDisplayCCD`)
- Switched entirely to CCD API (`QueryDisplayConfig`/`SetDisplayConfig`)
- Simplified `DisplayInfo` model to match CCD output
- Filter display paths to only show active or connected monitors

### 🎯 Next Steps

**Priority 1: Display Position Restoration**
- When re-enabling a display, Windows places it at a default position
- Need to also restore position/resolution from saved profile

**Priority 2: Test Audio Preservation**
- The CCD API toggle should be less disruptive than the old method
- Need to test if YouTube Music (etc.) keeps playing during display switches

**Priority 3: Global Hotkey Support**
- RegisterHotKey Win32 API for hotkey registration
- Background listener to trigger profile switches
- HotkeyDefinition model already exists in SavedDisplayProfile

## Architecture Overview

### Core Components

- **DisplayManagerNative** (C++ DLL) - Native Windows CCD API wrapper. Uses `QueryDisplayConfig` and `SetDisplayConfig` for display enumeration and control. Exports JSON-based display information.

- **DisplayManager.Core** (.NET 10 Library) - Managed wrapper around the native DLL via P/Invoke. Contains DisplayManager static class, DisplayInfo model, and profile services.

- **DisplayManagerCLI** (.NET 10 Console App) - Command-line interface for testing. Commands: list, toggle, raw, save, profiles, apply, delete.

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
// Get all display paths as JSON array
int GetAllDisplaysJson(char* buffer, int bufferSize);

// Toggle a display on/off using CCD API
int ToggleDisplayCCD(const char* deviceName, bool enable);

// Topology shortcuts
int SwitchToInternalDisplay();  // SDC_TOPOLOGY_INTERNAL
int EnableAllDisplays();        // SDC_TOPOLOGY_EXTEND
```

### Build Configuration Notes

- Native C++ project has both Win32 and x64 configurations - **always use x64**
- Post-build events copy DLL to dependent project output directories
- All .NET projects target .NET 10
- C++ project requires Visual Studio 2022+ with Windows 10 SDK
