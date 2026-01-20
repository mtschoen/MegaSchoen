using System.Diagnostics;
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
    private static extern int GetAllDisplaysJson(byte[] buffer, int bufferSize);

    [DllImport("DisplayManagerNative.dll", EntryPoint = "ApplyConfiguration", CharSet = CharSet.Ansi)]
    private static extern int ApplyConfigurationNative([MarshalAs(UnmanagedType.LPStr)] string activeDevicesJson);

    private static readonly JsonSerializerOptions JsonOptions = new()
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
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get displays from native DLL: {ex.Message}");
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
                var errorMessage = nativeResult switch
                {
                    -1 => "Invalid parameter (null JSON)",
                    -2 => "JSON is not an array",
                    -3 => "JSON parse error",
                    _ when nativeResult <= -300 => $"SetDisplayConfig failed with error {-(nativeResult + 300)}",
                    _ when nativeResult <= -200 => $"QueryDisplayConfig failed with error {-(nativeResult + 200)}",
                    _ when nativeResult <= -100 => $"GetDisplayConfigBufferSizes failed with error {-(nativeResult + 100)}",
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
