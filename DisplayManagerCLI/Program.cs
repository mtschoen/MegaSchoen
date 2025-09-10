using DisplayManager.Core;

if (args.Length == 0)
{
    Console.WriteLine("Usage: DisplayManagerCLI <command>");
    Console.WriteLine("Commands:");
    Console.WriteLine("  list        - List all displays");
    Console.WriteLine("  enable      - Enable all displays");
    Console.WriteLine("  disable     - Disable all displays except primary");
    Console.WriteLine("  raw         - Show raw JSON from native DLL");
    return;
}

string command = args[0].ToLower();

switch (command)
{
    case "list":
        ListDisplays();
        break;
    case "enable":
        bool enableSuccess = DisplayManager.Core.DisplayManager.EnableAllDisplaysNative();
        Console.WriteLine(enableSuccess ? "Successfully enabled all displays" : "Failed to enable all displays");
        break;
    case "disable":
        bool success = DisplayManager.Core.DisplayManager.SwitchToInternalDisplayNative();
        Console.WriteLine(success ? "Successfully switched to internal display only" : "Failed to switch to internal display");
        break;
    case "raw":
        Console.WriteLine(DisplayManager.Core.DisplayManager.GetRawDisplayJson());
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
