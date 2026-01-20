using DisplayManager.Core;
using DisplayManager.Core.Services;

// Initialize services
var storage = new ProfileStorageService();
var configManager = new ConfigurationManager(storage);

if (args.Length == 0)
{
    Console.WriteLine("Usage: DisplayManagerCLI <command> [arguments]");
    Console.WriteLine("\nDisplay Commands:");
    Console.WriteLine("  list        - List all displays");
    Console.WriteLine("  enable      - Enable all displays");
    Console.WriteLine("  disable     - Disable all displays except primary");
    Console.WriteLine("  raw         - Show raw JSON from native DLL");
    Console.WriteLine("  toggle <device> <on|off> - Toggle a display on/off (e.g., toggle DISPLAY5 off)");
    Console.WriteLine("\nProfile Commands:");
    Console.WriteLine("  save <name> [description] - Save current configuration as a profile");
    Console.WriteLine("  profiles    - List all saved profiles");
    Console.WriteLine("  apply <name>  - Apply a saved profile by name");
    Console.WriteLine("  delete <name> - Delete a saved profile");
    Console.WriteLine("  config      - Show configuration file location");
    return;
}

string command = args[0].ToLower();

switch (command)
{
    case "list":
        ListDisplays();
        break;
    case "enable":
        bool enableSuccess = DisplayManager.Core.DisplayManager.EnableAllDisplays();
        Console.WriteLine(enableSuccess ? "Successfully enabled all displays" : "Failed to enable all displays");
        break;
    case "disable":
        bool success = DisplayManager.Core.DisplayManager.SwitchToInternalDisplay();
        Console.WriteLine(success ? "Successfully switched to internal display only" : "Failed to switch to internal display");
        break;
    case "raw":
        Console.WriteLine(DisplayManager.Core.DisplayManager.GetRawDisplayJson());
        break;
    case "toggle":
        ToggleDisplay(args);
        break;
    case "save":
        await SaveProfile(args);
        break;
    case "profiles":
        await ListProfiles();
        break;
    case "apply":
        await ApplyProfile(args);
        break;
    case "delete":
        await DeleteProfile(args);
        break;
    case "config":
        ShowConfigPath();
        break;
    default:
        Console.WriteLine($"Unknown command: {command}");
        break;
}

static void ToggleDisplay(string[] args)
{
    if (args.Length < 3)
    {
        Console.WriteLine("Usage: toggle <device> <on|off>");
        Console.WriteLine("Example: toggle DISPLAY5 off");
        Console.WriteLine("         toggle \\\\.\\DISPLAY5 on");
        return;
    }

    string deviceName = args[1];
    string action = args[2].ToLower();

    // Add \\.\ prefix if not present
    if (!deviceName.StartsWith("\\\\.\\"))
    {
        deviceName = "\\\\.\\" + deviceName;
    }

    bool enable = action switch
    {
        "on" or "enable" or "1" or "true" => true,
        "off" or "disable" or "0" or "false" => false,
        _ => throw new ArgumentException($"Invalid action: {action}. Use 'on' or 'off'.")
    };

    Console.WriteLine($"Toggling {deviceName} {(enable ? "ON" : "OFF")}...");
    int result = DisplayManager.Core.DisplayManager.ToggleDisplay(deviceName, enable);

    if (result == 0)
    {
        Console.WriteLine("Success!");
    }
    else
    {
        Console.WriteLine($"Failed with error code: {result}");
    }
}

static void ListDisplays()
{
    var displays = DisplayManager.Core.DisplayManager.GetAllDisplays();

    // Filter to only show paths with a device name (physical outputs)
    var physicalDisplays = displays.Where(d => !string.IsNullOrEmpty(d.DeviceName)).ToList();
    Console.WriteLine($"Found {physicalDisplays.Count} display paths:\n");

    foreach (var display in physicalDisplays)
    {
        string status = display.IsActive ? "ACTIVE" : "inactive";
        string primary = display.IsPrimary ? " (PRIMARY)" : "";
        string name = !string.IsNullOrEmpty(display.MonitorName) ? display.MonitorName : "(unknown)";

        Console.WriteLine($"{display.DeviceName}: {name} [{status}]{primary}");

        if (display.IsActive)
        {
            Console.WriteLine($"  Resolution: {display.Width}x{display.Height} @ {display.RefreshRate:F1}Hz");
            Console.WriteLine($"  Position: ({display.PositionX}, {display.PositionY})");
        }
        Console.WriteLine();
    }
}

async Task SaveProfile(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: save <name> [description]");
        return;
    }

    string name = args[1];
    string? description = args.Length > 2 ? string.Join(" ", args.Skip(2)) : null;

    try
    {
        var profile = await configManager.CaptureCurrentConfigurationAsync(name, description);
        Console.WriteLine($"✓ Saved profile '{name}' (ID: {profile.Id})");
        Console.WriteLine($"  Topology: {profile.Topology}");
        Console.WriteLine($"  Displays: {profile.Displays.Count}");
        Console.WriteLine($"  Active Displays: {profile.Displays.Count(d => d.Enabled)}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error saving profile: {ex.Message}");
    }
}

async Task ListProfiles()
{
    try
    {
        var profiles = await configManager.GetAllProfilesAsync();

        if (profiles.Count == 0)
        {
            Console.WriteLine("No saved profiles found.");
            return;
        }

        Console.WriteLine($"Found {profiles.Count} profile(s):\n");

        foreach (var profile in profiles)
        {
            Console.WriteLine($"  {profile.Name}");
            Console.WriteLine($"    ID: {profile.Id}");
            if (!string.IsNullOrEmpty(profile.Description))
            {
                Console.WriteLine($"    Description: {profile.Description}");
            }
            Console.WriteLine($"    Type: {profile.ConfigType} ({profile.Topology})");
            Console.WriteLine($"    Displays: {profile.Displays.Count} ({profile.Displays.Count(d => d.Enabled)} enabled)");
            if (profile.Hotkey != null && profile.Hotkey.Enabled)
            {
                var modifiers = string.Join("+", profile.Hotkey.Modifiers);
                Console.WriteLine($"    Hotkey: {modifiers}+{profile.Hotkey.Key}");
            }
            Console.WriteLine($"    Created: {profile.Created:yyyy-MM-dd HH:mm}");
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error listing profiles: {ex.Message}");
    }
}

async Task ApplyProfile(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: apply <name>");
        return;
    }

    string name = args[1];

    try
    {
        var profile = await configManager.GetProfileByNameAsync(name);

        if (profile == null)
        {
            Console.WriteLine($"Profile '{name}' not found.");
            return;
        }

        Console.WriteLine($"Applying profile '{profile.Name}'...");
        bool success = configManager.ApplyProfile(profile);

        if (success)
        {
            Console.WriteLine($"✓ Successfully applied profile '{profile.Name}'");
        }
        else
        {
            Console.WriteLine($"Failed to apply profile '{profile.Name}'");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error applying profile: {ex.Message}");
    }
}

async Task DeleteProfile(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: delete <name>");
        return;
    }

    string name = args[1];

    try
    {
        var profile = await configManager.GetProfileByNameAsync(name);

        if (profile == null)
        {
            Console.WriteLine($"Profile '{name}' not found.");
            return;
        }

        bool success = await configManager.DeleteProfileAsync(profile.Id);

        if (success)
        {
            Console.WriteLine($"✓ Deleted profile '{name}'");
        }
        else
        {
            Console.WriteLine($"Failed to delete profile '{name}'");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error deleting profile: {ex.Message}");
    }
}

void ShowConfigPath()
{
    Console.WriteLine($"Configuration directory: {storage.GetConfigDirectory()}");
    Console.WriteLine($"Configuration file: {storage.GetConfigFilePath()}");
}
