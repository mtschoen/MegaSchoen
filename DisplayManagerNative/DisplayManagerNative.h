#pragma once

#define DISPLAYMANAGER_API __declspec(dllexport)

extern "C" {
    // Get all display information as JSON array
    // Returns: JSON length on success, negative error code on failure
    //   -1: Invalid parameters
    //   -2: Failed to get buffer sizes
    //   -3: Failed to query display config
    //   -(n): Buffer too small, need n bytes
    DISPLAYMANAGER_API int GetAllDisplaysJson(char* buffer, int bufferSize);

    // Apply a full display configuration (enable/disable multiple displays at once)
    // Parameters:
    //   activeDevicesJson: JSON array of device names that should be active
    //                      e.g. ["\\\\.\\\DISPLAY1", "\\\\.\\\DISPLAY2"]
    //                      All devices in the list will be enabled; all others disabled
    // Returns: 0 on success, negative error code on failure
    //   -1: Invalid parameter (null JSON)
    //   -2: JSON is not an array
    //   -3: JSON parse error
    //   -100 - x: GetDisplayConfigBufferSizes failed with error x
    //   -200 - x: QueryDisplayConfig failed with error x
    //   -300 - x: SetDisplayConfig failed with error x
    DISPLAYMANAGER_API int ApplyConfiguration(const char* activeDevicesJson);
}