using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DisplayManager.Core
{
    public static class DisplayManager
    {
        // Native DLL imports
        [DllImport("DisplayManagerNative.dll", EntryPoint = "SwitchToInternalDisplay")]
        private static extern int SwitchToInternalDisplayNative();

        [DllImport("DisplayManagerNative.dll", EntryPoint = "EnableAllDisplays")]
        private static extern int EnableAllDisplaysNative();

        [DllImport("DisplayManagerNative.dll")]
        private static extern int GetAllDisplaysJson(byte[] buffer, int bufferSize);

        [DllImport("DisplayManagerNative.dll", EntryPoint = "ApplyDisplayConfiguration", CharSet = CharSet.Ansi)]
        private static extern int ApplyDisplayConfigurationNative([MarshalAs(UnmanagedType.LPStr)] string configJson);

        [DllImport("DisplayManagerNative.dll", EntryPoint = "ToggleDisplayCCD", CharSet = CharSet.Ansi)]
        private static extern int ToggleDisplayCCDNative([MarshalAs(UnmanagedType.LPStr)] string deviceName, [MarshalAs(UnmanagedType.I1)] bool enable);

        public static List<DisplayInfo> GetAllDisplays()
        {
            const int bufferSize = 256 * 1024; // 256KB buffer
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
            const int bufferSize = 256 * 1024; // 256KB buffer
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

        public static bool SwitchToInternalDisplay()
        {
            try
            {
                int result = SwitchToInternalDisplayNative();
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

        public static bool EnableAllDisplays()
        {
            try
            {
                int result = EnableAllDisplaysNative();
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

        public static bool ApplyDisplayConfiguration(string configJson)
        {
            try
            {
                int result = ApplyDisplayConfigurationNative(configJson);
                if (result == 0)
                {
                    Console.WriteLine("Successfully applied display configuration!");
                    return true;
                }
                else
                {
                    string errorMessage = result switch
                    {
                        -1 => "Invalid parameter (null pointer)",
                        -2 => "Invalid configuration (no displays enabled)",
                        -3 => "JSON parsing error",
                        -4 => "Unknown error",
                        _ => $"Windows error code: {result}"
                    };
                    Console.WriteLine($"Failed to apply configuration: {errorMessage}");
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

        /// <summary>
        /// Toggle a display on or off using the CCD API (SetDisplayConfig).
        /// This should be less disruptive than ChangeDisplaySettingsEx.
        /// </summary>
        /// <param name="deviceName">GDI device name like "\\\\.\\DISPLAY5"</param>
        /// <param name="enable">true to enable, false to disable</param>
        /// <returns>0 on success, error code on failure</returns>
        public static int ToggleDisplay(string deviceName, bool enable)
        {
            try
            {
                int result = ToggleDisplayCCDNative(deviceName, enable);
                if (result == 0)
                {
                    Console.WriteLine($"Successfully {(enable ? "enabled" : "disabled")} {deviceName}");
                }
                else
                {
                    string errorMessage = result switch
                    {
                        -1 => "Invalid parameter (null device name)",
                        -2 => "String conversion error",
                        -3 => "Device not found in display paths",
                        _ when result <= -300 => $"SetDisplayConfig failed with error {-(result + 300)}",
                        _ when result <= -200 => $"QueryDisplayConfig failed with error {-(result + 200)}",
                        _ when result <= -100 => $"GetDisplayConfigBufferSizes failed with error {-(result + 100)}",
                        _ => $"Unknown error: {result}"
                    };
                    Console.WriteLine($"Failed to toggle {deviceName}: {errorMessage}");
                }
                return result;
            }
            catch (DllNotFoundException)
            {
                Console.WriteLine("DisplayManagerNative.dll not found!");
                return -999;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calling native DLL: {ex.Message}");
                return -998;
            }
        }
    }
}