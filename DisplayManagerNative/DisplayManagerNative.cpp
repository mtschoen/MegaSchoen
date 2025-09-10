#include "DisplayManagerNative.h"
#include "DisplayInfo.h"
#include "json.hpp"
#include <windows.h>
#include <vector>
#include <string>

// Constants for display enumeration
#define ENUM_CURRENT_SETTINGS   ((DWORD)-1)
#define ENUM_REGISTRY_SETTINGS  ((DWORD)-2)
#define DISPLAY_DEVICE_ATTACHED_TO_DESKTOP 0x00000001
#define DISPLAY_DEVICE_ACTIVE              0x00000001
#define DISPLAY_DEVICE_MULTI_DRIVER        0x00000002
#define DISPLAY_DEVICE_PRIMARY_DEVICE      0x00000004
#define DISPLAY_DEVICE_MIRRORING_DRIVER    0x00000008
#define DISPLAY_DEVICE_VGA_COMPATIBLE      0x00000010
#define DISPLAY_DEVICE_REMOVABLE           0x00000020
#define DISPLAY_DEVICE_MODESPRUNED         0x08000000
#define DISPLAY_DEVICE_REMOTE              0x04000000
#define DISPLAY_DEVICE_DISCONNECT          0x02000000

// QueryDisplayConfig constants
#define QDC_ALL_PATHS                    0x00000001
#define DISPLAYCONFIG_PATH_ACTIVE        0x00000001

using json = nlohmann::json;

int SwitchToInternalDisplay()
{
    // First, get the current display configuration
    UINT32 pathCount = 0;
    UINT32 modeCount = 0;
    
    // Get buffer sizes for current configuration
    LONG result = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, &pathCount, &modeCount);
    if (result != ERROR_SUCCESS)
    {
        return result;
    }
    
    // Check if we already have only one display path (likely already internal-only)
    if (pathCount <= 1)
    {
        // Already in single display mode, nothing to do
        return 0;
    }
    
    // Allocate buffers
    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);
    
    // Get current configuration
    result = QueryDisplayConfig(QDC_ALL_PATHS, &pathCount, paths.data(), 
                               &modeCount, modes.data(), nullptr);
    if (result != ERROR_SUCCESS)
    {
        return result;
    }
    
    // Now apply topology change to internal display only
    // Use SDC_TOPOLOGY_INTERNAL with SDC_APPLY
    DWORD flags = SDC_TOPOLOGY_INTERNAL | SDC_APPLY;
    
    // For topology changes, we don't need to pass the current configuration
    result = SetDisplayConfig(0, nullptr, 0, nullptr, flags);
    
    return result == ERROR_SUCCESS ? 0 : (int)result;
}

int EnableAllDisplays()
{
    // Use SetDisplayConfig to enable extended desktop (all displays)
    // SDC_TOPOLOGY_EXTEND enables all available displays in extended mode
    DWORD flags = SDC_TOPOLOGY_EXTEND | SDC_APPLY;
    
    LONG result = SetDisplayConfig(0, nullptr, 0, nullptr, flags);
    
    return result == ERROR_SUCCESS ? 0 : (int)result;
}

int GetAllDisplaysJson(char* buffer, int bufferSize)
{
    if (!buffer || bufferSize <= 0) {
        return -1; // Invalid parameters
    }

    json jsonResult;
    json legacyDisplays = json::array();
    json queryConfigDisplays = json::array();
    
    DISPLAY_DEVICE d = {};
    d.cb = sizeof(d);

    int deviceIndex = 0;
    while (EnumDisplayDevicesW(nullptr, deviceIndex, &d, 0) != 0)
    {
        // Check if this display adapter has any monitors connected
        DISPLAY_DEVICE monitor = {};
        monitor.cb = sizeof(monitor);
        bool hasMonitor = EnumDisplayDevicesW(d.DeviceName, 0, &monitor, 0) != 0;
        
        // Filter to only PCI devices (real hardware) that have monitors
        bool isPCIDevice = wcsstr(d.DeviceID, L"PCI\\") != nullptr;
        
        if (!isPCIDevice || !hasMonitor) {
            deviceIndex++;
            continue;
        }

        json display;
        
        // Convert wide strings to UTF-8
        std::wstring deviceName(d.DeviceName);
        std::wstring deviceString(d.DeviceString);
        
        display["deviceName"] = std::string(deviceName.begin(), deviceName.end());
        display["deviceString"] = std::string(deviceString.begin(), deviceString.end());
        display["isActive"] = (d.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0;
        display["stateFlags"] = (int)d.StateFlags; // Debug info
        
        // Add DeviceID and DeviceKey for debugging
        std::wstring deviceID(d.DeviceID);
        std::wstring deviceKey(d.DeviceKey);
        display["deviceID"] = std::string(deviceID.begin(), deviceID.end());
        display["deviceKey"] = std::string(deviceKey.begin(), deviceKey.end());
        
        // Add monitor information if available
        if (hasMonitor) {
            std::wstring monitorName(monitor.DeviceString);
            std::wstring monitorID(monitor.DeviceID);
            display["monitorName"] = std::string(monitorName.begin(), monitorName.end());
            display["monitorID"] = std::string(monitorID.begin(), monitorID.end());
            display["monitorStateFlags"] = (int)monitor.StateFlags;
        }

        // Try to get current settings for enabled displays
        DEVMODEW dm = {};
        dm.dmSize = sizeof(dm);
        
        if (EnumDisplaySettingsW(d.DeviceName, ENUM_CURRENT_SETTINGS, &dm))
        {
            // Display is enabled and has current settings
            display["width"] = (int)dm.dmPelsWidth;
            display["height"] = (int)dm.dmPelsHeight;
            display["positionX"] = (int)dm.dmPosition.x;
            display["positionY"] = (int)dm.dmPosition.y;
            display["frequency"] = (int)dm.dmDisplayFrequency;
            display["bitsPerPixel"] = (int)dm.dmBitsPerPel;
            display["isPrimary"] = (dm.dmPosition.x == 0 && dm.dmPosition.y == 0);
            display["settingsSource"] = "current";
        }
        else if (EnumDisplaySettingsW(d.DeviceName, ENUM_REGISTRY_SETTINGS, &dm))
        {
            // Display is disabled but has registry settings
            display["width"] = (int)dm.dmPelsWidth;
            display["height"] = (int)dm.dmPelsHeight;
            display["positionX"] = (int)dm.dmPosition.x;
            display["positionY"] = (int)dm.dmPosition.y;
            display["frequency"] = (int)dm.dmDisplayFrequency;
            display["bitsPerPixel"] = (int)dm.dmBitsPerPel;
            display["isPrimary"] = false;
            display["settingsSource"] = "registry";
        }
        else
        {
            // No settings available at all
            display["width"] = 0;
            display["height"] = 0;
            display["positionX"] = 0;
            display["positionY"] = 0;
            display["frequency"] = 0;
            display["bitsPerPixel"] = 0;
            display["isPrimary"] = false;
            display["settingsSource"] = "none";
        }

        legacyDisplays.push_back(display);
        deviceIndex++;
    }

    // Now try QueryDisplayConfig method
    UINT32 pathCount = 0;
    UINT32 modeCount = 0;
    
    LONG queryResult = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, &pathCount, &modeCount);
    if (queryResult == ERROR_SUCCESS && pathCount > 0) {
        std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
        std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);
        
        queryResult = QueryDisplayConfig(QDC_ALL_PATHS, &pathCount, paths.data(), 
                                   &modeCount, modes.data(), nullptr);
        if (queryResult == ERROR_SUCCESS) {
            for (UINT32 i = 0; i < pathCount; i++) {
                json display;
                
                DISPLAYCONFIG_PATH_INFO& path = paths[i];
                display["pathIndex"] = (int)i;
                display["isActive"] = (path.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0;
                display["pathFlags"] = (int)path.flags;
                
                // Get source device name
                DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName = {};
                sourceName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                sourceName.header.size = sizeof(sourceName);
                sourceName.header.adapterId = path.sourceInfo.adapterId;
                sourceName.header.id = path.sourceInfo.id;
                
                if (DisplayConfigGetDeviceInfo(&sourceName.header) == ERROR_SUCCESS) {
                    std::wstring sourceDeviceName(sourceName.viewGdiDeviceName);
                    display["deviceName"] = std::string(sourceDeviceName.begin(), sourceDeviceName.end());
                }
                
                // Get target device info (monitor)
                if (path.targetInfo.targetAvailable) {
                    DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = {};
                    targetName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                    targetName.header.size = sizeof(targetName);
                    targetName.header.adapterId = path.targetInfo.adapterId;
                    targetName.header.id = path.targetInfo.id;
                    
                    if (DisplayConfigGetDeviceInfo(&targetName.header) == ERROR_SUCCESS) {
                        std::wstring monitorFriendlyName(targetName.monitorFriendlyDeviceName);
                        std::wstring monitorDevicePath(targetName.monitorDevicePath);
                        display["monitorName"] = std::string(monitorFriendlyName.begin(), monitorFriendlyName.end());
                        display["monitorDevicePath"] = std::string(monitorDevicePath.begin(), monitorDevicePath.end());
                    }
                    
                    display["targetAvailable"] = true;
                    display["outputTechnology"] = (int)path.targetInfo.outputTechnology;
                } else {
                    continue;
                }
                
                queryConfigDisplays.push_back(display);
            }
        }
    }

    // Combine results
    jsonResult["legacy"] = legacyDisplays;
    jsonResult["queryConfig"] = queryConfigDisplays;
    
    std::string jsonString = jsonResult.dump(2);
    int jsonLength = static_cast<int>(jsonString.length());

    if (jsonLength >= bufferSize) {
        return -(jsonLength + 1); // Return negative required size (including null terminator)
    }

    strcpy_s(buffer, bufferSize, jsonString.c_str());
    return jsonLength;
}