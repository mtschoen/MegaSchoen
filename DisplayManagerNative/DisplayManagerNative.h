#pragma once

#define DISPLAYMANAGER_API __declspec(dllexport)

extern "C" {
    DISPLAYMANAGER_API int SwitchToInternalDisplay();
    DISPLAYMANAGER_API int EnableAllDisplays();
    
    // Get all display information as JSON array
    // Returns: JSON length on success, negative error code on failure
    //   -1: Invalid parameters
    //   -2: Failed to get buffer sizes
    //   -3: Failed to query display config
    //   -(n): Buffer too small, need n bytes
    DISPLAYMANAGER_API int GetAllDisplaysJson(char* buffer, int bufferSize);

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