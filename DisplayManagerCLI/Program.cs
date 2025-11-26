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

static void ListDisplays()
{
    var displays = DisplayManager.Core.DisplayManager.GetAllDisplays();
    Console.WriteLine($"Found {displays.Count} displays:");

    for (int i = 0; i < displays.Count; i++)
    {
        var display = displays[i];
        Console.WriteLine($"\nDisplay {i + 1}:");
        Console.WriteLine($"  Device Name: {display.DeviceName}");
        Console.WriteLine($"  Device String: {display.DeviceString}");
        Console.WriteLine($"  Resolution: {display.Width}x{display.Height}");
        Console.WriteLine($"  Position: ({display.PositionX}, {display.PositionY})");
        Console.WriteLine($"  Frequency: {display.Frequency}Hz");
        Console.WriteLine($"  Bits Per Pixel: {display.BitsPerPixel}");
        Console.WriteLine($"  Is Active: {display.IsActive}");
        Console.WriteLine($"  Is Primary: {display.IsPrimary}");
        Console.WriteLine($"  State Flags: 0x{display.StateFlags:X8}");
        Console.WriteLine($"  Settings Source: {display.SettingsSource}");
        Console.WriteLine($"  Device ID: {display.DeviceID}");
        Console.WriteLine($"  Device Key: {display.DeviceKey}");
        if (!string.IsNullOrEmpty(display.MonitorName))
        {
            Console.WriteLine($"  Monitor Name: {display.MonitorName}");
            Console.WriteLine($"  Monitor ID: {display.MonitorID}");
            Console.WriteLine($"  Monitor State Flags: 0x{display.MonitorStateFlags:X8}");
        }
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
