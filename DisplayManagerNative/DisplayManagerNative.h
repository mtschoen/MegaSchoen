#pragma once

#define DISPLAYMANAGER_API __declspec(dllexport)

extern "C" {
    DISPLAYMANAGER_API int SwitchToInternalDisplay();
    DISPLAYMANAGER_API int EnableAllDisplays();
    
    // Get all display information as JSON
    // Returns: JSON length on success, negative error code on failure
    // If buffer too small, returns -(required size)
    DISPLAYMANAGER_API int GetAllDisplaysJson(char* buffer, int bufferSize);

    // Apply a display configuration from JSON
    // Parameters:
    //   configJson: JSON string containing display configuration with format:
    //               { "displays": [{ "enabled": true/false }] }
    // Returns: 0 on success, negative error code on failure
    //   -1: Invalid parameter (null pointer)
    //   -2: Invalid configuration (no displays enabled)
    //   -3: JSON parsing error
    //   -4: Unknown error
    //   Other positive values: Windows error codes from SetDisplayConfig
    DISPLAYMANAGER_API int ApplyDisplayConfiguration(const char* configJson);

    // Toggle a display on/off using the CCD API (SetDisplayConfig)
    // Parameters:
    //   deviceName: GDI device name like "\\\\.\\DISPLAY5"
    //   enable: true to enable, false to disable
    // Returns: 0 on success, negative error code on failure
    //   -1: Invalid parameter (null device name)
    //   -2: String conversion error
    //   -3: Device not found
    //   -100 - x: GetDisplayConfigBufferSizes failed with error x
    //   -200 - x: QueryDisplayConfig failed with error x
    //   -300 - x: SetDisplayConfig failed with error x
    DISPLAYMANAGER_API int ToggleDisplayCCD(const char* deviceName, bool enable);
}