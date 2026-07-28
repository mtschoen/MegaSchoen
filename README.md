# MegaSchoen

Dumping ground for various cross-platform utilities. Two functions live here today:

- **Display Manager** (Windows-only) - save and switch between monitor configurations.
- **Claude Sessions** (Windows + Linux) - a live dashboard for active Claude Code sessions.

## Display Manager (Windows-only)

A Windows display profile manager that lets you save and quickly switch between different monitor configurations.

![MegaSchoen UI](Screenshots/MegaSchoen-UI.png)

## Features

- **Save Display Profiles** - Capture your current monitor arrangement including resolution, position, refresh rate, and rotation
- **Quick Profile Switching** - Apply saved profiles with one click to switch between configurations (e.g., "Work" vs "Gaming" vs "TV Only")
- **Global Hotkeys** - Assign keyboard shortcuts (e.g., Ctrl+Alt+1) to instantly switch profiles
- **System Tray** - Runs in the background with quick access to profiles from the tray icon
- **Start with Windows** - Optional startup registration to have hotkeys ready when you log in
- **Single Instance** - Only one instance runs; launching again brings the existing window to focus
- **Multi-GPU Support** - Works with displays connected to different graphics adapters
- **Extend Mode** - Properly restores extended desktop layouts (not mirrored)
- **Portrait Mode Support** - Preserves monitor rotation settings

## Use Cases

- Switch between a multi-monitor work setup and a single TV for gaming/media
- Toggle portrait monitors on/off while preserving rotation
- Quickly disable secondary displays for screen sharing
- Restore complex multi-monitor arrangements after disconnecting displays

## Installation

Download the latest release or build from source.

### Building from Source

**Requirements:**
- Visual Studio 18 (not 2022) with C++ and .NET MAUI workloads - it's the one that ships both the v145 native toolset and the .NET 10 SDK this solution needs
- Windows 10 SDK
- .NET 10

Build the solution with MSBuild (not `dotnet build` - it can't build the native C++ dependency). See `AGENTS.md` ("Build Commands") for the full details, including why the VS version matters and where each project's output lands.

```powershell
# Build the entire solution
MSBuild.exe MegaSchoen.sln -p:Configuration=Debug

# Or build just the Display Manager CLI
MSBuild.exe DisplayManagerCLI/DisplayManagerCLI.csproj -p:Configuration=Debug
```

## Usage

### GUI Application

Launch `MegaSchoen.exe` for the graphical interface:

1. **Current Displays** - Shows all connected monitors with their current settings
2. **Save Current Arrangement** - Enter a name and save your current display configuration
3. **Saved Profiles** - Lists all saved profiles with options to:
   - **Set Hotkey** - Assign a global hotkey (e.g., Ctrl+Alt+1) to this profile
   - **Apply** - Switch to this display configuration
   - **Overwrite** - Update the profile with current settings
   - **Delete** - Remove the profile
4. **Settings** - Configure minimize-to-tray and start-with-Windows options

**System Tray:** Closing the window minimizes to the system tray. Right-click the tray icon for quick profile access or to exit.

### Command Line Interface

```powershell
DisplayManagerCLI.exe list              # List all displays
DisplayManagerCLI.exe save "My Profile" # Save current config as a profile
DisplayManagerCLI.exe load "My Profile" # Load and apply a saved profile
DisplayManagerCLI.exe profiles          # List all saved profiles
DisplayManagerCLI.exe raw               # Show raw JSON display data
```

Profiles are stored in `%APPDATA%\MegaSchoen\configs.json`.

## Display Manager Project Structure

- **DisplayManagerNative** (C++ DLL) - Windows CCD API wrapper for display enumeration and configuration
- **DisplayManager.Core** (.NET 10) - Managed wrapper with profile management
- **DisplayManagerCLI** (.NET 10) - Command-line interface
- **MegaSchoen** (MAUI) - Cross-platform GUI (currently Windows-only for display features)

## How Display Manager Works

MegaSchoen uses the Windows [CCD (Connecting and Configuring Displays) API](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/ccd-apis) to:

1. Query all display paths via `QueryDisplayConfig`
2. Store configuration data (resolution, position, refresh rate, rotation) per monitor
3. Apply configurations via `SetDisplayConfig` with unique source IDs for extend mode

Monitors are identified by their hardware device path, which remains stable across reboots.

## Claude Sessions (Windows + Linux)

A live dashboard for active Claude Code sessions - see at a glance which sessions are working, waiting on a permission prompt, or idle and waiting for input, and jump straight to the terminal window hosting one.

**Features:**

- **State Badges** - each session shows its current state (Working, PendingPermission, AwaitingInput), computed from Claude Code hook events, not a timer
- **Session Titles** - Claude Code's generated session title, read straight from the transcript
- **Cwd and Last Activity** - at a glance for every live session
- **Subagent Rollup** - subagent sessions roll up under their parent
- **Focus** - brings the terminal window hosting a session to the foreground (Windows: ancestor-window walk, including embedded IDE terminals and remote ssh sessions; Linux/steamdeck: verified with a KWin-based focuser)
- **Copy Path** - copies a session's transcript path to the clipboard

**Three frontends, one backend:**

- **MegaSchoen Sessions tab** (MAUI, Windows) - live cards driven by a `FileSystemWatcher`
- **MegaSchoen.Avalonia** - a cross-platform Avalonia app with the same session cards, verified on Linux/steamdeck
- **AgentSessionsCLI** (`agent-sessions`) - `list` (Spectre.Console live table, one-shot `--json`, or NDJSON `--json-stream`) and `focus <session-prefix>`, cross-platform

All three read from the same source of truth: `ClaudeHookBridge`, a small console app that Claude Code's hooks invoke on every event, writing one state file per session that the frontends watch and re-render from. See `AGENTS.md` ("Architecture Overview") for the full component breakdown.

`ActiveSessionEnumerator` combines provider-neutral `ISessionSource`
implementations; `ClaudeSessionSource` is currently the only configured
provider. This is the extension point for adding other coding agents without
changing dashboard or CLI consumers.

To provision a Linux sessions host, run `scripts/setup-sessions-host.sh` on that
host. It installs `agent-sessions` and retains `claude-sessions` as a
compatibility launcher for existing remote-host configurations.

## License

MIT
