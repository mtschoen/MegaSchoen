# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

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

".\DisplayManagerCLI\bin\Debug\net9.0\DisplayManagerCLI.exe" list      # List all displays
".\DisplayManagerCLI\bin\Debug\net9.0\DisplayManagerCLI.exe" enable    # Enable all displays
".\DisplayManagerCLI\bin\Debug\net9.0\DisplayManagerCLI.exe" disable   # Disable all except primary
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
- MegaSchoen (MAUI) is currently independent but will integrate with DisplayManager.Core
- Native DLL includes nlohmann/json for JSON serialization

### Build Configuration Notes

- Native C++ project outputs to solution bin folder and automatically copies DLL/PDB to dependent projects' output directories via post-build events
- .NET projects target different frameworks: Core/CLI use .NET 9, MAUI uses .NET 8
- C++ project requires Visual Studio with Windows 10 SDK and v143 platform toolset