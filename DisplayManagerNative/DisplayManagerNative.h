#pragma once

#define DISPLAYMANAGER_API __declspec(dllexport)

extern "C" {
    DISPLAYMANAGER_API int SwitchToInternalDisplay();
    DISPLAYMANAGER_API int EnableAllDisplays();
    
    // Get all display information as JSON
    // Returns: JSON length on success, negative error code on failure
    // If buffer too small, returns -(required size)
    DISPLAYMANAGER_API int GetAllDisplaysJson(char* buffer, int bufferSize);
}