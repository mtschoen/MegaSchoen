using System.Runtime.InteropServices;
using System.Text.Json;
using DisplayManager.Core.Models;

namespace DisplayManager.Core;

/// <summary>
/// Result of applying a display configuration.
/// </summary>
public class ApplyResult
{
    public bool Success { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<string> Applied { get; set; } = [];
}

public static class DisplayManager
{
    [DllImport("DisplayManagerNative.dll")]
    static extern int GetAllDisplaysJson(byte[] buffer, int bufferSize);

    [DllImport("DisplayManagerNative.dll", EntryPoint = "ApplyConfiguration", CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    static extern int ApplyConfigurationNative([MarshalAs(UnmanagedType.LPStr)] string activeDevicesJson);

    [DllImport("DisplayManagerNative.dll")]
    static extern int GetSupportedModesJson(int edidManufactureId, int edidProductCodeId, byte[] buffer, int bufferSize);

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static List<DisplayInfo> GetAllDisplays()
    {
        const int bufferSize = 256 * 1024; // 256KB buffer
        var buffer = new byte[bufferSize];

        try
        {
            var result = GetAllDisplaysJson(buffer, bufferSize);
            if (result < 0)
            {
                throw new InvalidOperationException($"Native call failed. Error code: {result}");
            }

            var jsonString = System.Text.Encoding.UTF8.GetString(buffer, 0, result);
            var displays = JsonSerializer.Deserialize<DisplayInfo[]>(jsonString, JsonOptions);
            return displays?.ToList() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static string GetRawDisplayJson()
    {
        const int bufferSize = 256 * 1024; // 256KB buffer
        var buffer = new byte[bufferSize];

        try
        {
            var result = GetAllDisplaysJson(buffer, bufferSize);
            if (result < 0)
            {
                return $"Error: Native call failed. Error code: {result}";
            }

            return System.Text.Encoding.UTF8.GetString(buffer, 0, result);
        }
        catch (Exception ex)
        {
            return $"Error getting raw JSON: {ex.Message}";
        }
    }

    /// <summary>
    /// Enumerates the supported display modes for a monitor identified by EDID.
    /// Returns an empty list if the monitor is not currently active or on error.
    /// </summary>
    public static List<DisplayMode> GetSupportedModes(int edidManufactureId, int edidProductCodeId)
    {
        const int bufferSize = 64 * 1024;
        var buffer = new byte[bufferSize];

        try
        {
            var result = GetSupportedModesJson(edidManufactureId, edidProductCodeId, buffer, bufferSize);
            if (result < 0)
            {
                return [];
            }

            var jsonString = System.Text.Encoding.UTF8.GetString(buffer, 0, result);
            var modes = JsonSerializer.Deserialize<DisplayMode[]>(jsonString, JsonOptions);
            return modes?.ToList() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Apply a display configuration. All displays in the list will be enabled,
    /// all displays NOT in the list will be disabled.
    /// </summary>
    /// <param name="displays">Display configurations that should be active</param>
    /// <returns>Result with success status and any errors</returns>
    public static ApplyResult ApplyConfiguration(IEnumerable<SavedDisplayConfig> displays)
    {
        var result = new ApplyResult { Success = true };
        var displayList = displays.ToList();

        try
        {
            // Serialize full display config to JSON array
            var json = JsonSerializer.Serialize(displayList, JsonOptions);

            // Call the native function
            var nativeResult = ApplyConfigurationNative(json);

            if (nativeResult == 0)
            {
                var names = string.Join(", ", displayList.Select(d => d.MonitorName));
                result.Applied.Add($"Applied configuration: {names}");
            }
            else
            {
                result.Success = false;
                // Native encodes each failure stage as -(stageBase) - win32error, stage bases 100 apart:
                // 100 = GetDisplayConfigBufferSizes, 200 = QueryDisplayConfig, 300 = SetDisplayConfig (Step 1
                // topology activation), 400 = SetDisplayConfig (Step 2 positioning). Arms are bounded to their
                // 100-wide band so a stage's win32error (<100 in practice) can't bleed into a neighbouring
                // stage's message; an out-of-band code falls through to "Unknown" rather than being mislabeled.
                var errorMessage = nativeResult switch
                {
                    -1 => "Invalid parameter (null JSON)",
                    -2 => "JSON is not an array",
                    -3 => "JSON parse error",
                    _ when nativeResult is <= -400 and > -500 => $"Monitors activated, but positioning failed: SetDisplayConfig error {-(nativeResult + 400)}",
                    _ when nativeResult is <= -300 and > -400 => $"SetDisplayConfig failed with error {-(nativeResult + 300)}",
                    _ when nativeResult is <= -200 and > -300 => $"QueryDisplayConfig failed with error {-(nativeResult + 200)}",
                    _ when nativeResult is <= -100 and > -200 => $"GetDisplayConfigBufferSizes failed with error {-(nativeResult + 100)}",
                    _ => $"Unknown error: {nativeResult}"
                };
                result.Errors.Add(errorMessage);
            }
        }
        catch (DllNotFoundException)
        {
            result.Success = false;
            result.Errors.Add("DisplayManagerNative.dll not found");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Exception: {ex.Message}");
        }

        return result;
    }
}
