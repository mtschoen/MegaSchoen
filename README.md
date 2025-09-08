# MegaSchoen

A cross-platform utility suite. Currently includes display management functionality for Windows with plans for additional system utilities across multiple platforms.

## Project Structure

- **DisplayManager.Core** - Core library containing platform-specific API wrappers for display management
- **DisplayManagerCLI** - Command-line interface for testing display operations
- **MegaSchoen** - MAUI application providing cross-platform GUI interface

## Current Features

### Display Management (Windows)
- Enumerate all connected displays with detailed information
- Get current display configurations (resolution, position, frequency, etc.)
- Enable/disable individual displays
- Set display configurations programmatically
- Quick display switching functionality

### DisplayManagerCLI
- `list` - Display information for all connected monitors
- `enable` - Enable all displays
- `disable` - Disable all displays except primary

## Next Steps

1. **Enhanced Display Features**
   - Individual display enable/disable by device name
   - Custom resolution and positioning
   - Display profile save/load functionality

2. **Cross-Platform Support** - Extend display management to macOS and Linux

3. **GUI Implementation** - Complete the MAUI application for user-friendly cross-platform interface

4. **Additional Utilities** - Expand MegaSchoen with more system management tools

5. **Advanced Features**
   - Multi-monitor arrangement presets
   - Hotkey support
   - System tray integration

## Building

```bash
# Build the entire solution
dotnet build

# Run the CLI tool (Windows)
dotnet run --project DisplayManagerCLI -- list
dotnet run --project DisplayManagerCLI -- enable
dotnet run --project DisplayManagerCLI -- disable
```

## Requirements

- .NET 8.0 or later
- Windows 10/11 (for current display management features)