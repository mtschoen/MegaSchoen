#include "DisplayManagerNative.h"
#include "json.hpp"
#include <windows.h>
#include <vector>
#include <string>
#include <set>
#include <map>

using json = nlohmann::json;

// Helper to convert wide string to UTF-8
static std::string WideToUtf8(const std::wstring& wide) {
    if (wide.empty()) return "";
    int utf8Len = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (utf8Len <= 0) return "";
    std::vector<char> utf8(utf8Len);
    WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, utf8.data(), utf8Len, nullptr, nullptr);
    return std::string(utf8.data());
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

        display["pathIndex"] = static_cast<int>(i);
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
        display["targetAvailable"] = path.targetInfo.targetAvailable != 0;

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
                display["width"] = static_cast<int>(sourceMode.width);
                display["height"] = static_cast<int>(sourceMode.height);
                display["positionX"] = static_cast<int>(sourceMode.position.x);
                display["positionY"] = static_cast<int>(sourceMode.position.y);
            }
        }

        // Get refresh rate from target mode
        if (path.targetInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID) {
            UINT32 modeIdx = path.targetInfo.modeInfoIdx;
            if (modeIdx < modeCount && modes[modeIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_TARGET) {
                auto& targetMode = modes[modeIdx].targetMode.targetVideoSignalInfo;
                // Refresh rate = vSyncFreq.Numerator / vSyncFreq.Denominator
                if (targetMode.vSyncFreq.Denominator > 0) {
                    display["refreshRate"] = static_cast<double>(targetMode.vSyncFreq.Numerator) / targetMode.vSyncFreq.Denominator;
                }
            }
        }

        // Determine if this is the primary display (position 0,0)
        int posX = display["positionX"].get<int>();
        int posY = display["positionY"].get<int>();
        display["isPrimary"] = display["isActive"].get<bool>() && (posX == 0 && posY == 0);

        // Include IDs for matching/identification
        display["sourceId"] = static_cast<int>(path.sourceInfo.id);
        display["targetId"] = static_cast<int>(path.targetInfo.id);

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

// Helper to convert UTF-8 to wide string
static std::wstring Utf8ToWide(const std::string& utf8) {
    if (utf8.empty()) return L"";
    int wideLen = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), -1, nullptr, 0);
    if (wideLen <= 0) return L"";
    std::vector<wchar_t> wide(wideLen);
    MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), -1, wide.data(), wideLen);
    return std::wstring(wide.data());
}

// Structure to hold parsed display config from JSON
struct DisplayConfigRequest {
    std::wstring monitorDevicePath;
    int width = 0;
    int height = 0;
    int positionX = 0;
    int positionY = 0;
    double refreshRate = 0.0;
};

// Apply a full display configuration
// configJson: JSON array of display configs with monitorDevicePath for matching
// All displays in the list will be enabled; all others will be disabled
// Returns: 0 on success, negative error code on failure
int ApplyConfiguration(const char* configJson)
{
    if (!configJson) {
        return -1;
    }

    // Parse the JSON array of display configs
    std::map<std::wstring, DisplayConfigRequest> wantedConfigs;
    try {
        json configList = json::parse(configJson);
        if (!configList.is_array()) {
            return -2; // Not a JSON array
        }
        for (const auto& item : configList) {
            if (item.is_object() && item.contains("monitorDevicePath")) {
                DisplayConfigRequest req;
                req.monitorDevicePath = Utf8ToWide(item["monitorDevicePath"].get<std::string>());
                req.width = item.value("width", 0);
                req.height = item.value("height", 0);
                req.positionX = item.value("positionX", 0);
                req.positionY = item.value("positionY", 0);
                req.refreshRate = item.value("refreshRate", 60.0);
                wantedConfigs[req.monitorDevicePath] = req;
            }
        }
    } catch (...) {
        return -3; // JSON parse error
    }

    // Get ALL display paths (including inactive ones)
    UINT32 pathCount = 0;
    UINT32 modeCount = 0;

    LONG result = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, &pathCount, &modeCount);
    if (result != ERROR_SUCCESS) {
        return -100 - static_cast<int>(result);
    }

    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);

    result = QueryDisplayConfig(QDC_ALL_PATHS, &pathCount, paths.data(), &modeCount, modes.data(), nullptr);
    if (result != ERROR_SUCCESS) {
        return -200 - static_cast<int>(result);
    }

    // Track which monitors we've already enabled (to avoid duplicates)
    std::set<std::wstring> enabledMonitors;

    // For each path, decide if it should be active
    for (UINT32 i = 0; i < pathCount; i++) {
        // Get monitor device path for this target
        DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = {};
        targetName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        targetName.header.size = sizeof(targetName);
        targetName.header.adapterId = paths[i].targetInfo.adapterId;
        targetName.header.id = paths[i].targetInfo.id;

        std::wstring monitorPath;
        if (DisplayConfigGetDeviceInfo(&targetName.header) == ERROR_SUCCESS) {
            monitorPath = std::wstring(targetName.monitorDevicePath);
        }

        auto wantedIt = wantedConfigs.find(monitorPath);
        bool isWanted = !monitorPath.empty() && (wantedIt != wantedConfigs.end());
        bool alreadyEnabled = enabledMonitors.count(monitorPath) > 0;
        bool hasValidSourceMode = (paths[i].sourceInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID);
        bool hasValidTargetMode = (paths[i].targetInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID);

        if (isWanted && !alreadyEnabled) {
            const auto& config = wantedIt->second;

            // If path lacks mode info, create it from saved config
            if (!hasValidSourceMode && config.width > 0 && config.height > 0) {
                // Create a new source mode entry
                DISPLAYCONFIG_MODE_INFO newSourceMode = {};
                newSourceMode.infoType = DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE;
                newSourceMode.adapterId = paths[i].sourceInfo.adapterId;
                newSourceMode.id = paths[i].sourceInfo.id;
                newSourceMode.sourceMode.width = config.width;
                newSourceMode.sourceMode.height = config.height;
                newSourceMode.sourceMode.pixelFormat = DISPLAYCONFIG_PIXELFORMAT_32BPP;
                newSourceMode.sourceMode.position.x = config.positionX;
                newSourceMode.sourceMode.position.y = config.positionY;

                modes.push_back(newSourceMode);
                paths[i].sourceInfo.modeInfoIdx = static_cast<UINT32>(modes.size() - 1);
                hasValidSourceMode = true;
            }

            if (!hasValidTargetMode && config.refreshRate > 0) {
                // Create a new target mode entry
                DISPLAYCONFIG_MODE_INFO newTargetMode = {};
                newTargetMode.infoType = DISPLAYCONFIG_MODE_INFO_TYPE_TARGET;
                newTargetMode.adapterId = paths[i].targetInfo.adapterId;
                newTargetMode.id = paths[i].targetInfo.id;
                // Set refresh rate (convert Hz to rational)
                auto refreshNumerator = static_cast<UINT32>(config.refreshRate * 1000);
                newTargetMode.targetMode.targetVideoSignalInfo.vSyncFreq.Numerator = refreshNumerator;
                newTargetMode.targetMode.targetVideoSignalInfo.vSyncFreq.Denominator = 1000;
                newTargetMode.targetMode.targetVideoSignalInfo.hSyncFreq.Numerator = 0;
                newTargetMode.targetMode.targetVideoSignalInfo.hSyncFreq.Denominator = 0;
                // Active size matches resolution
                newTargetMode.targetMode.targetVideoSignalInfo.activeSize.cx = config.width;
                newTargetMode.targetMode.targetVideoSignalInfo.activeSize.cy = config.height;
                newTargetMode.targetMode.targetVideoSignalInfo.totalSize.cx = config.width;
                newTargetMode.targetMode.targetVideoSignalInfo.totalSize.cy = config.height;
                newTargetMode.targetMode.targetVideoSignalInfo.videoStandard = 255; // D3DKMDT_VSS_OTHER
                newTargetMode.targetMode.targetVideoSignalInfo.scanLineOrdering = DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE;

                modes.push_back(newTargetMode);
                paths[i].targetInfo.modeInfoIdx = static_cast<UINT32>(modes.size() - 1);
                hasValidTargetMode = true;
            }

            if (hasValidSourceMode && hasValidTargetMode) {
                // Enable this path
                paths[i].flags |= DISPLAYCONFIG_PATH_ACTIVE;
                enabledMonitors.insert(monitorPath);
            }
        }

        // Disable paths that aren't wanted or already enabled
        if (!isWanted || alreadyEnabled) {
            paths[i].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;
        }
    }

    // Apply the configuration
    auto newModeCount = static_cast<UINT32>(modes.size());
    DWORD flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES;
    result = SetDisplayConfig(pathCount, paths.data(), newModeCount, modes.data(), flags);

    if (result != ERROR_SUCCESS) {
        return -300 - static_cast<int>(result);
    }

    return 0;
}
