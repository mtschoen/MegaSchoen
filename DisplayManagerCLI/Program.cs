using DisplayManager.Core.Services;

var profileService = new DisplayProfileService();

if (args.Length == 0)
{
    Console.WriteLine("Usage: DisplayManagerCLI <command> [arguments]");
    Console.WriteLine("\nDisplay Commands:");
    Console.WriteLine("  list                      - List all displays");
    Console.WriteLine("  raw                       - Show raw JSON from native DLL");
    Console.WriteLine("\nProfile Commands:");
    Console.WriteLine("  save <name> [description] - Save current configuration as a profile");
    Console.WriteLine("  profiles                  - List all saved profiles");
    Console.WriteLine("  load <name>               - Apply a saved profile by name");
    Console.WriteLine("  delete <name>             - Delete a saved profile");
    Console.WriteLine("  config                    - Show configuration file location");
    return;
}

var command = args[0].ToLower();

switch (command)
{
    case "list":
        ListDisplays();
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
    case "load":
        await LoadProfile(args);
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
    var physicalDisplays = displays.Where(d => !string.IsNullOrEmpty(d.DeviceName)).ToList();

    Console.WriteLine($"Found {physicalDisplays.Count} display paths:\n");

    foreach (var display in physicalDisplays)
    {
        var status = display.IsActive ? "ACTIVE" : "inactive";
        var primary = display.IsPrimary ? " (PRIMARY)" : "";
        var name = !string.IsNullOrEmpty(display.MonitorName) ? display.MonitorName : "(unknown)";

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

    var name = args[1];
    var description = args.Length > 2 ? string.Join(" ", args.Skip(2)) : null;

    try
    {
        var profiles = await profileService.GetAllProfilesAsync();
        var existing = profiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        var profile = profileService.CaptureCurrentConfiguration(name, description);

        // Preserve the ID and hotkey of an existing profile so we overwrite rather than duplicate
        if (existing != null)
        {
            profile.Id = existing.Id;
            profile.Hotkey = existing.Hotkey;
            profile.Created = existing.Created;
        }

        await profileService.SaveProfileAsync(profile);

        Console.WriteLine($"Saved profile '{name}'");
        Console.WriteLine($"  Displays: {string.Join(", ", profile.Displays.Select(d => d.MonitorName))}");
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
        var profiles = await profileService.GetAllProfilesAsync();

        if (profiles.Count == 0)
        {
            Console.WriteLine("No saved profiles found.");
            return;
        }

        Console.WriteLine($"Found {profiles.Count} profile(s):\n");

        foreach (var profile in profiles)
        {
            Console.WriteLine($"  {profile.Name}");
            if (!string.IsNullOrEmpty(profile.Description))
            {
                Console.WriteLine($"    {profile.Description}");
            }
            Console.WriteLine($"    Displays: {string.Join(", ", profile.Displays.Select(d => d.MonitorName))}");
            if (profile.Hotkey != null && profile.Hotkey.Enabled)
            {
                var modifiers = string.Join("+", profile.Hotkey.Modifiers);
                Console.WriteLine($"    Hotkey: {modifiers}+{profile.Hotkey.Key}");
            }
            Console.WriteLine();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error listing profiles: {ex.Message}");
    }
}

async Task LoadProfile(string[] args)
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: load <name>");
        return;
    }

    var name = args[1];

    try
    {
        var profiles = await profileService.GetAllProfilesAsync();
        var profile = profiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
        {
            Console.WriteLine($"Profile '{name}' not found.");
            return;
        }

        Console.WriteLine($"Applying profile '{profile.Name}'...");
        var result = profileService.ApplyProfile(profile);

        if (result.Success)
        {
            Console.WriteLine("Success!");
            foreach (var applied in result.Applied)
            {
                Console.WriteLine($"  {applied}");
            }
        }
        else
        {
            Console.WriteLine("Failed:");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"  Error: {error}");
            }
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

    var name = args[1];

    try
    {
        var profiles = await profileService.GetAllProfilesAsync();
        var profile = profiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (profile == null)
        {
            Console.WriteLine($"Profile '{name}' not found.");
            return;
        }

        await profileService.DeleteProfileAsync(profile.Id);
        Console.WriteLine($"Deleted profile '{name}'");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error deleting profile: {ex.Message}");
    }
}

void ShowConfigPath()
{
    var storage = new ProfileStorageService();
    Console.WriteLine($"Configuration directory: {storage.GetConfigDirectory()}");
    Console.WriteLine($"Configuration file: {storage.GetConfigFilePath()}");
}
