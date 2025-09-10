using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DisplayManager.Core
{
    public static class DisplayManager
    {
        [DllImport("user32.dll")]
        private static extern int EnumDisplayDevices(string lpDevice, int iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, int dwFlags);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

        [DllImport("user32.dll")]
        private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(uint numPathArrayElements, IntPtr pathArray, uint numModeInfoArrayElements, IntPtr modeInfoArray, uint flags);

        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("gdi32.dll")]
        private static extern int D3DKMTPollDisplayChildren(IntPtr pData);

        private const int ENUM_CURRENT_SETTINGS = -1;
        private const int CDS_UPDATEREGISTRY = 0x01;
        private const int DISP_CHANGE_SUCCESSFUL = 0;
        private const int DISPLAY_DEVICE_ACTIVE = 0x00000001;

        // SetDisplayConfig constants
        private const uint SDC_TOPOLOGY_INTERNAL = 0x80000000;
        private const uint SDC_TOPOLOGY_CLONE = 0x40000000;
        private const uint SDC_TOPOLOGY_EXTEND = 0x20000000;
        private const uint SDC_TOPOLOGY_EXTERNAL = 0x10000000;
        private const uint SDC_APPLY = 0x80;
        private const uint SDC_SAVE_TO_DATABASE = 0x800;
        private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
        private const uint QDC_ALL_PATHS = 0x01;
        private const int ERROR_INVALID_PARAMETER = 0x57;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVMODE
        {
            private const int CCHDEVICENAME = 32;
            private const int CCHFORMNAME = 32;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        public static List<DisplayInfo> GetAllDisplays()
        {
            const int bufferSize = 128 * 1024; // 64KB buffer
            byte[] buffer = new byte[bufferSize];

            try
            {
                int result = GetAllDisplaysJson(buffer, bufferSize);
                if (result < 0)
                {
                    throw new InvalidOperationException($"Native call failed or buffer too small. Required size: {-result}");
                }

                string jsonString = System.Text.Encoding.UTF8.GetString(buffer, 0, result);
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                };
                
                // Try new format first (with legacy and queryConfig sections)
                try 
                {
                    var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonString);
                    if (jsonDoc.RootElement.TryGetProperty("legacy", out var legacyElement))
                    {
                        var legacyArray = System.Text.Json.JsonSerializer.Deserialize<DisplayInfo[]>(legacyElement.GetRawText(), options);
                        return legacyArray?.ToList() ?? new List<DisplayInfo>();
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Fall back to old format
                }
                
                // Old format fallback
                var displayArray = System.Text.Json.JsonSerializer.Deserialize<DisplayInfo[]>(jsonString, options);
                return displayArray?.ToList() ?? new List<DisplayInfo>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get displays from native DLL: {ex.Message}");
                return new List<DisplayInfo>();
            }
        }

        public static string GetRawDisplayJson()
        {
            const int bufferSize = 128 * 1024; // 64KB buffer
            byte[] buffer = new byte[bufferSize];

            try
            {
                int result = GetAllDisplaysJson(buffer, bufferSize);
                if (result < 0)
                {
                    return $"Error: Native call failed or buffer too small. Required size: {-result}";
                }

                return System.Text.Encoding.UTF8.GetString(buffer, 0, result);
            }
            catch (Exception ex)
            {
                return $"Error getting raw JSON: {ex.Message}";
            }
        }

        public static bool SetDisplayConfiguration(List<DisplayInfo> displays)
        {
            bool allSuccessful = true;

            foreach (var display in displays)
            {
                DEVMODE dm = new DEVMODE();
                dm.dmSize = (short)Marshal.SizeOf(dm);
                dm.dmPelsWidth = display.Width;
                dm.dmPelsHeight = display.Height;
                dm.dmPositionX = display.PositionX;
                dm.dmPositionY = display.PositionY;
                dm.dmDisplayFrequency = display.Frequency;
                dm.dmBitsPerPel = display.BitsPerPixel;
                dm.dmFields = 0x20000 | 0x80000 | 0x100000 | 0x400000 | 0x40000; // DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY | DM_BITSPERPEL | DM_POSITION

                uint flags = display.IsActive ? CDS_UPDATEREGISTRY : (uint)0;
                int result = ChangeDisplaySettingsEx(display.DeviceName, ref dm, IntPtr.Zero, flags, IntPtr.Zero);

                if (result != DISP_CHANGE_SUCCESSFUL)
                {
                    Debug.WriteLine($"Failed to set display configuration for {display.DeviceName}, error code: {result}");
                    allSuccessful = false;
                }
            }

            return allSuccessful;
        }

        public static bool DisableAllDisplaysExceptPrimaryUsingDisplaySwitch()
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "DisplaySwitch.exe",
                    Arguments = "/internal",  // Switch to internal (primary) display only
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                process?.WaitForExit();
                return process?.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool DisableAllDisplaysExceptPrimary()
        {
            var displays = GetAllDisplays();
            bool allSuccessful = true;

            foreach (var display in displays)
            {
                if (!display.IsPrimary && display.IsActive)
                {
                    Console.WriteLine($"Disabling display {display.DeviceName}");
                    
                    // Try to disable by setting resolution to 0x0
                    DEVMODE dm = new DEVMODE();
                    dm.dmSize = (short)Marshal.SizeOf(dm);
                    dm.dmPelsWidth = 0;
                    dm.dmPelsHeight = 0;
                    dm.dmFields = 0x80000 | 0x100000; // DM_PELSWIDTH | DM_PELSHEIGHT
                    
                    int result = ChangeDisplaySettingsEx(display.DeviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
                    Console.WriteLine($"  Disable call result: {result} (0=success)");
                    
                    if (result != DISP_CHANGE_SUCCESSFUL)
                    {
                        Console.WriteLine($"  Trying alternative method...");
                        // Alternative: try with CDS_RESET flag
                        result = ChangeDisplaySettingsEx(display.DeviceName, IntPtr.Zero, IntPtr.Zero, 4, IntPtr.Zero); // CDS_RESET = 0x40000000 but trying 4
                        Console.WriteLine($"  Alternative result: {result} (0=success)");
                        
                        if (result != DISP_CHANGE_SUCCESSFUL)
                        {
                            Debug.WriteLine($"Failed to disable display {display.DeviceName}, error code: {result}");
                            allSuccessful = false;
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"Successfully disabled display {display.DeviceName}");
                    }
                }
            }

            // Second call: apply all changes with all nulls
            Console.WriteLine("Applying display changes with null call...");
            int finalResult = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
            Console.WriteLine($"Apply changes result: {finalResult} (0=success)");
            
            if (finalResult != DISP_CHANGE_SUCCESSFUL)
            {
                Debug.WriteLine($"Failed to apply display changes, error code: {finalResult}");
                allSuccessful = false;
            }

            return allSuccessful;
        }

        public static bool SetDisplayModeUsingSetDisplayConfig(uint topologyFlags)
        {
            uint flags;
            
            if ((topologyFlags & 0xc0000000) == 0)
            {
                // Call D3DKMTPollDisplayChildren first, like FUN_1400161c8 does
                ulong pollData = 0x1e00000000; // From decompiled code
                IntPtr pollDataPtr = Marshal.AllocHGlobal(8);
                Marshal.WriteInt64(pollDataPtr, (long)pollData);
                int pollResult = D3DKMTPollDisplayChildren(pollDataPtr);
                Marshal.FreeHGlobal(pollDataPtr);
                Console.WriteLine($"D3DKMTPollDisplayChildren returned: 0x{pollResult:X8}");
                
                flags = topologyFlags | 0x880; // SDC_APPLY | SDC_SAVE_TO_DATABASE
            }
            else if (topologyFlags == SDC_TOPOLOGY_INTERNAL)
            {
                flags = topologyFlags | 0x80; // SDC_APPLY only
            }
            else
            {
                return false; // Invalid parameter
            }
            
            Console.WriteLine($"Calling SetDisplayConfig with flags: 0x{flags:X8}");
            int result = SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, flags);
            
            if (result != 0)
            {
                Console.WriteLine($"SetDisplayConfig failed with error code: 0x{result:X8} ({result})");
                Debug.WriteLine($"SetDisplayConfig failed with error code: 0x{result:X8} ({result})");
            }
            else
            {
                Console.WriteLine("SetDisplayConfig succeeded!");
            }
            
            return result == 0;
        }

        // Import from our native DLL
        [DllImport("DisplayManagerNative.dll")]
        private static extern int SwitchToInternalDisplay();

        [DllImport("DisplayManagerNative.dll")]
        private static extern int EnableAllDisplays();

        [DllImport("DisplayManagerNative.dll")]
        private static extern int GetAllDisplaysJson(byte[] buffer, int bufferSize);

        public static bool SwitchToInternalDisplayNative()
        {
            try
            {
                int result = SwitchToInternalDisplay();
                if (result == 0)
                {
                    Console.WriteLine("Successfully switched to internal display using native DLL!");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Native DLL failed with error code: {result}");
                    return false;
                }
            }
            catch (DllNotFoundException)
            {
                Console.WriteLine("DisplayManagerNative.dll not found!");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling native DLL: {ex.Message}");
                return false;
            }
        }

        public static bool EnableAllDisplaysNative()
        {
            try
            {
                int result = EnableAllDisplays();
                if (result == 0)
                {
                    Console.WriteLine("Successfully enabled all displays using native DLL!");
                    return true;
                }
                else
                {
                    Console.WriteLine($"Native DLL failed with error code: {result}");
                    return false;
                }
            }
            catch (DllNotFoundException)
            {
                Console.WriteLine("DisplayManagerNative.dll not found!");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling native DLL: {ex.Message}");
                return false;
            }
        }
    }
}