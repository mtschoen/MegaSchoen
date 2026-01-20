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
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" save "My Profile" # Save current config
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" load "My Profile" # Load a profile
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" profiles          # List all profiles
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" raw               # Show raw JSON
```

## Current Status (Last updated: 2026-01-20)

### ✅ Display Toggle Working

**What's Working:**
- Display detection and listing via CCD API (QueryDisplayConfig)
- Full display enable/disable including displays on secondary GPUs
- Position, resolution, and refresh rate restoration when re-enabling displays
- Profile save/load with full display configuration
- Profiles stored in `%APPDATA%\MegaSchoen\configs.json`

**Key Implementation Details:**
- Profiles store full `SavedDisplayConfig` objects (monitorDevicePath, resolution, position, refresh rate)
- `monitorDevicePath` is used for reliable display matching (stable across reboots)
- When re-enabling a display with cleared mode info, native code creates mode entries from saved profile data
- Works across multiple GPUs (tested with displays on different adapters)

### 🎯 Next Steps

**Priority 1: Test Audio Preservation**
- The CCD API approach should be less disruptive than ChangeDisplaySettingsEx
- Need to test if YouTube Music (etc.) keeps playing during display switches

**Priority 2: Global Hotkey Support**
- RegisterHotKey Win32 API for hotkey registration
- HotkeyDefinition model exists in SavedDisplayProfile

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
// Get all display paths as JSON array
int GetAllDisplaysJson(char* buffer, int bufferSize);

// Apply a full display configuration
// Takes JSON array of SavedDisplayConfig objects with:
//   - monitorDevicePath (for matching)
//   - width, height, positionX, positionY, refreshRate (for mode creation)
// Displays in array are enabled; all others are disabled
// Creates mode entries from saved config if path lacks mode info
int ApplyConfiguration(const char* configJson);
```

### Build Configuration Notes

- Native C++ project has both Win32 and x64 configurations - **always use x64**
- Post-build events copy DLL to dependent project output directories
- All .NET projects target .NET 10
- C++ project requires Visual Studio 2022+ with Windows 10 SDK
