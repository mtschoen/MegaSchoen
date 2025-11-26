# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Current Status (Last updated: 2025-11-26)

### ✅ Phase 2 COMPLETE: Display Profile Management UI

**What's Working:**
- MAUI app builds and runs on Windows (Visual Studio 2026)
- Native DLL properly integrated and copying to output directory
- Display detection via native Windows API (GetAllDisplays)
- Full profile management UI:
  - View current displays (resolution, refresh rate, primary/active status)
  - Save current arrangement with custom name
  - List all saved profiles with metadata
  - Delete profiles with confirmation dialog
- Profile storage: `%APPDATA%\MegaSchoen\configs.json`

**Recent Fixes:**
- Added MSBuild target to copy DisplayManagerNative.dll to Windows output folder
- Fixed namespace conflicts between DisplayManager.Core.DisplayInfo and MAUI's DisplayInfo
- Updated all projects to .NET 10
- Upgraded to Visual Studio 2026 with v145 platform toolset

### 🎯 Next Steps (Phase 3 - Profile Application):

**Priority 1: Apply Saved Profiles**
- Add "Apply" button to each saved profile in the UI
- Implement native DLL function to restore display configuration
- Handle edge cases (missing displays, resolution mismatch, etc.)

**Priority 2: Global Hotkey Support**
- Research Windows hotkey registration (RegisterHotKey Win32 API)
- Add HotkeyDefinition to SavedDisplayProfile model (already exists!)
- Implement hotkey UI (key combination picker)
- Background service to monitor and trigger hotkeys

**Priority 3: Profile Management Enhancements**
- Edit profile names/descriptions
- Duplicate profiles
- Export/import profiles
- Profile validation (check if displays exist)

**Future Considerations:**
- Visual display arrangement designer (drag-and-drop positioning)
- Per-display resolution/refresh rate configuration
- Profile auto-switching based on connected displays
- System tray integration

**Key Files for Next Session:**
- `MegaSchoen/ViewModels/MainPageViewModel.cs` - UI logic
- `DisplayManager.Core/Services/DisplayProfileService.cs` - Profile operations
- `DisplayManagerNative/DisplayManagerNative.cpp` - Native API calls
- `DisplayManager.Core/Models/SavedDisplayProfile.cs` - Data model

## Build Commands

Build the entire solution (includes C++ native DLL):
```bash
MSBuild.exe MegaSchoen.sln
```

Build for specific configurations:
```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug
MSBuild.exe MegaSchoen.sln -p:Configuration=Release
```

Build specific projects:
```bash
MSBuild.exe DisplayManagerNative\DisplayManagerNative.vcxproj
MSBuild.exe DisplayManager.Core\DisplayManager.Core.csproj
MSBuild.exe DisplayManagerCLI\DisplayManagerCLI.csproj
```

Run the CLI tool (after building with MSBuild):
```bash
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug    # Build first

".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" list      # List all displays
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" enable    # Enable all displays
".\DisplayManagerCLI\bin\Debug\net10.0\DisplayManagerCLI.exe" disable   # Disable all except primary
```

## Architecture Overview

MegaSchoen is a cross-platform utility suite focused on display management with a hybrid C#/.NET and native C++ architecture:

### Core Components

- **DisplayManagerNative** (C++ DLL) - Native Windows display API wrapper using SetDisplayConfig and related Win32 APIs. Exports JSON-based display information and core switching functionality.

- **DisplayManager.Core** (.NET 9 Library) - Managed wrapper around the native DLL, provides C# interop via P/Invoke. Contains the main DisplayManager static class and DisplayInfo data models.

- **DisplayManagerCLI** (.NET 9 Console App) - Command-line interface for testing and basic operations. Provides list, enable, disable commands.

- **MegaSchoen** (MAUI App) - Cross-platform GUI application targeting Android, iOS, macOS, and Windows. Currently uses .NET 8 MAUI framework.

### Key Architectural Patterns

**Native/Managed Interop**: The C++ native DLL handles low-level Windows display APIs (SetDisplayConfig, QueryDisplayConfig, EnumDisplayDevices) and serializes display information as JSON. The C# layer deserializes this JSON into strongly-typed DisplayInfo objects.

**Multi-Method Display Control**: The codebase implements multiple approaches to display management:
- Direct Win32 API calls (ChangeDisplaySettingsEx)
- SetDisplayConfig topology changes 
- DisplaySwitch.exe process invocation
- Custom native DLL methods

**Cross-Platform Preparation**: While currently Windows-only, the architecture separates platform-specific code (native DLL) from cross-platform logic (MAUI app), preparing for macOS/Linux support.

### Project Dependencies

- DisplayManagerCLI depends on DisplayManager.Core
- DisplayManager.Core depends on DisplayManagerNative (C++ DLL)
- MegaSchoen (MAUI) depends on DisplayManager.Core and DisplayManagerNative
- Native DLL includes nlohmann/json for JSON serialization
- MAUI app includes custom MSBuild target to copy native DLL to Windows output folder

### Build Configuration Notes

- Native C++ project outputs to solution bin folder and automatically copies DLL/PDB to dependent projects' output directories via post-build events
- All .NET projects now target .NET 10
- C++ project requires Visual Studio 2026 with Windows 10 SDK and v145 platform toolset