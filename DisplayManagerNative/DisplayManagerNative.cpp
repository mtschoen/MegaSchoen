#include "DisplayManagerNative.h"
#include "json.hpp"
#include <windows.h>
#include <vector>
#include <string>

// QueryDisplayConfig constants
#define QDC_ALL_PATHS                    0x00000001
#define QDC_ONLY_ACTIVE_PATHS            0x00000002
#define DISPLAYCONFIG_PATH_ACTIVE        0x00000001

using json = nlohmann::json;

// Helper to convert wide string to UTF-8
std::string WideToUtf8(const std::wstring& wide) {
    if (wide.empty()) return "";
    int utf8Len = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (utf8Len <= 0) return "";
    std::vector<char> utf8(utf8Len);
    WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, utf8.data(), utf8Len, nullptr, nullptr);
    return std::string(utf8.data());
}

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

    json displays = json::array();

    // Use QueryDisplayConfig (CCD API) to get all display paths
    UINT32 pathCount = 0;
    UINT32 modeCount = 0;

    LONG result = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, &pathCount, &modeCount);
    if (result != ERROR_SUCCESS) {
        return -2; // Failed to get buffer sizes
    }

    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);

    result = QueryDisplayConfig(QDC_ALL_PATHS, &pathCount, paths.data(), &modeCount, modes.data(), nullptr);
    if (result != ERROR_SUCCESS) {
        return -3; // Failed to query display config
    }

    for (UINT32 i = 0; i < pathCount; i++) {
        DISPLAYCONFIG_PATH_INFO& path = paths[i];

        // Skip paths without a target available (no monitor connected)
        // unless they're currently active
        bool isActive = (path.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0;
        if (!isActive && !path.targetInfo.targetAvailable) {
            continue;
        }

        json display;

        display["pathIndex"] = (int)i;
        display["isActive"] = isActive;

        // Get source device name (e.g., \\.\DISPLAY1)
        DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName = {};
        sourceName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        sourceName.header.size = sizeof(sourceName);
        sourceName.header.adapterId = path.sourceInfo.adapterId;
        sourceName.header.id = path.sourceInfo.id;

        if (DisplayConfigGetDeviceInfo(&sourceName.header) == ERROR_SUCCESS) {
            display["deviceName"] = WideToUtf8(sourceName.viewGdiDeviceName);
        } else {
            display["deviceName"] = "";
        }

        // Get target (monitor) device info
        display["targetAvailable"] = path.targetInfo.targetAvailable ? true : false;

        DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = {};
        targetName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        targetName.header.size = sizeof(targetName);
        targetName.header.adapterId = path.targetInfo.adapterId;
        targetName.header.id = path.targetInfo.id;

        if (DisplayConfigGetDeviceInfo(&targetName.header) == ERROR_SUCCESS) {
            display["monitorName"] = WideToUtf8(targetName.monitorFriendlyDeviceName);
            display["monitorDevicePath"] = WideToUtf8(targetName.monitorDevicePath);
        } else {
            display["monitorName"] = "";
            display["monitorDevicePath"] = "";
        }

        // Get resolution and position from source mode
        display["width"] = 0;
        display["height"] = 0;
        display["positionX"] = 0;
        display["positionY"] = 0;
        display["refreshRate"] = 0.0;

        if (path.sourceInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID) {
            UINT32 modeIdx = path.sourceInfo.modeInfoIdx;
            if (modeIdx < modeCount && modes[modeIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE) {
                auto& sourceMode = modes[modeIdx].sourceMode;
                display["width"] = (int)sourceMode.width;
                display["height"] = (int)sourceMode.height;
                display["positionX"] = (int)sourceMode.position.x;
                display["positionY"] = (int)sourceMode.position.y;
            }
        }

        // Get refresh rate from target mode
        if (path.targetInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID) {
            UINT32 modeIdx = path.targetInfo.modeInfoIdx;
            if (modeIdx < modeCount && modes[modeIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_TARGET) {
                auto& targetMode = modes[modeIdx].targetMode.targetVideoSignalInfo;
                // Refresh rate = vSyncFreq.Numerator / vSyncFreq.Denominator
                if (targetMode.vSyncFreq.Denominator > 0) {
                    display["refreshRate"] = (double)targetMode.vSyncFreq.Numerator / targetMode.vSyncFreq.Denominator;
                }
            }
        }

        // Determine if this is the primary display (position 0,0)
        int posX = display["positionX"].get<int>();
        int posY = display["positionY"].get<int>();
        display["isPrimary"] = display["isActive"].get<bool>() && (posX == 0 && posY == 0);

        // Include IDs for matching/identification
        display["sourceId"] = (int)path.sourceInfo.id;
        display["targetId"] = (int)path.targetInfo.id;

        displays.push_back(display);
    }

    std::string jsonString = displays.dump(2);
    int jsonLength = static_cast<int>(jsonString.length());

    if (jsonLength >= bufferSize) {
        return -(jsonLength + 1); // Return negative required size
    }

    strcpy_s(buffer, bufferSize, jsonString.c_str());
    return jsonLength;
}

// Toggle a display on/off using the CCD API (SetDisplayConfig)
// deviceName: GDI device name like "\\\\.\\DISPLAY5"
// enable: true to enable, false to disable
// Returns: 0 on success, negative error code on failure
int ToggleDisplayCCD(const char* deviceName, bool enable)
{
    if (!deviceName) {
        return -1;
    }

    // Convert device name to wide string
    int wideLen = MultiByteToWideChar(CP_UTF8, 0, deviceName, -1, nullptr, 0);
    if (wideLen <= 0) {
        return -2;
    }
    std::vector<wchar_t> wideDeviceName(wideLen);
    MultiByteToWideChar(CP_UTF8, 0, deviceName, -1, wideDeviceName.data(), wideLen);
    std::wstring targetDeviceName(wideDeviceName.data());

    // Get all display paths
    UINT32 pathCount = 0;
    UINT32 modeCount = 0;

    LONG result = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, &pathCount, &modeCount);
    if (result != ERROR_SUCCESS) {
        return -100 - (int)result;
    }

    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);

    result = QueryDisplayConfig(QDC_ALL_PATHS, &pathCount, paths.data(), &modeCount, modes.data(), nullptr);
    if (result != ERROR_SUCCESS) {
        return -200 - (int)result;
    }

    // Find the path matching our device name
    int targetPathIndex = -1;
    for (UINT32 i = 0; i < pathCount; i++) {
        DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName = {};
        sourceName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        sourceName.header.size = sizeof(sourceName);
        sourceName.header.adapterId = paths[i].sourceInfo.adapterId;
        sourceName.header.id = paths[i].sourceInfo.id;

        if (DisplayConfigGetDeviceInfo(&sourceName.header) == ERROR_SUCCESS) {
            if (wcscmp(sourceName.viewGdiDeviceName, targetDeviceName.c_str()) == 0) {
                targetPathIndex = i;
                break;
            }
        }
    }

    if (targetPathIndex < 0) {
        return -3; // Device not found
    }

    // Toggle the DISPLAYCONFIG_PATH_ACTIVE flag
    if (enable) {
        paths[targetPathIndex].flags |= DISPLAYCONFIG_PATH_ACTIVE;
    } else {
        paths[targetPathIndex].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;
    }

    // Apply the new configuration
    // SDC_APPLY: Apply immediately
    // SDC_USE_SUPPLIED_DISPLAY_CONFIG: Use the paths/modes we're supplying
    // SDC_SAVE_TO_DATABASE: Persist the change
    // SDC_ALLOW_CHANGES: Allow Windows to make adjustments if needed
    DWORD flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES;

    result = SetDisplayConfig(pathCount, paths.data(), modeCount, modes.data(), flags);

    if (result != ERROR_SUCCESS) {
        return -300 - (int)result;
    }

    return 0;
}