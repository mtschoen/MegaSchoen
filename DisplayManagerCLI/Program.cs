using DisplayManager.Core;

if (args.Length == 0)
{
    Console.WriteLine("Usage: DisplayManagerCLI <command>");
    Console.WriteLine("Commands:");
    Console.WriteLine("  list        - List all displays");
    Console.WriteLine("  enable      - Enable all displays");
    Console.WriteLine("  disable     - Disable all displays except primary");
    return;
}

string command = args[0].ToLower();

switch (command)
{
    case "list":
        ListDisplays();
        break;
    case "enable":
        DisplayManager.Core.DisplayManager.EnableAllDisplays();
        Console.WriteLine("Attempted to enable all displays");
        break;
    case "disable":
        bool success = DisplayManager.Core.DisplayManager.DisableAllDisplaysExceptPrimaryUsingDisplaySwitch();
        Console.WriteLine(success ? "Successfully disabled displays except primary using DisplaySwitch" : "Failed to disable displays using DisplaySwitch");
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
    }
}
