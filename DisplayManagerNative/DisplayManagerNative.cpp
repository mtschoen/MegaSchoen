#include "DisplayManagerNative.h"
#include "json.hpp"
#include <windows.h>
#include <vector>
#include <string>
#include <set>
#include <map>

using json = nlohmann::json;

namespace {

// Helper to convert wide string to UTF-8
std::string WideToUtf8(const std::wstring& wide) {
    if (wide.empty()) return "";
    int utf8Len = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (utf8Len <= 0) return "";
    std::vector<char> utf8(utf8Len);
    WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, utf8.data(), utf8Len, nullptr, nullptr);
    return {utf8.data()};
}

// Helper to convert UTF-8 to wide string
std::wstring Utf8ToWide(const std::string& utf8) {
    if (utf8.empty()) return L"";
    int wideLen = MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), -1, nullptr, 0);
    if (wideLen <= 0) return L"";
    std::vector<wchar_t> wide(wideLen);
    MultiByteToWideChar(CP_UTF8, 0, utf8.c_str(), -1, wide.data(), wideLen);
    return {wide.data()};
}

// Structure to hold parsed display config from JSON
struct DisplayConfigRequest {
    std::wstring monitorDevicePath;
    int width = 0;
    int height = 0;
    int positionX = 0;
    int positionY = 0;
    double refreshRate = 0.0;
    int rotation = 0;  // degrees: 0, 90, 180, 270
};

} // anonymous namespace

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
        display["isPrimary"] = display["isActive"].get<bool>() && posX == 0 && posY == 0;

        // Get rotation from path targetInfo (convert enum to degrees)
        int rotationDegrees = 0;
        switch (path.targetInfo.rotation) {
            case DISPLAYCONFIG_ROTATION_ROTATE90:  rotationDegrees = 90;  break;
            case DISPLAYCONFIG_ROTATION_ROTATE180: rotationDegrees = 180; break;
            case DISPLAYCONFIG_ROTATION_ROTATE270: rotationDegrees = 270; break;
            default: rotationDegrees = 0; break;
        }
        display["rotation"] = rotationDegrees;

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
                req.rotation = item.value("rotation", 0);
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

    // First pass: identify which path indices we want to enable
    // We need to assign unique source IDs for extend mode
    std::vector<std::pair<UINT32, DisplayConfigRequest>> pathsToEnable;

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
        bool isWanted = !monitorPath.empty() && wantedIt != wantedConfigs.end();
        bool alreadyEnabled = enabledMonitors.count(monitorPath) > 0;

        if (isWanted && !alreadyEnabled) {
            pathsToEnable.push_back({i, wantedIt->second});
            enabledMonitors.insert(monitorPath);
        } else {
            // Disable paths that aren't wanted or already handled
            paths[i].flags &= ~DISPLAYCONFIG_PATH_ACTIVE;
        }
    }

    // Build new modes array - we'll create fresh source/target modes for each enabled path
    // This ensures unique source IDs (extend mode) and correct positions
    std::vector<DISPLAYCONFIG_MODE_INFO> newModes;

    // Assign sequential source IDs starting from 0
    UINT32 nextSourceId = 0;

    for (auto& [pathIdx, config] : pathsToEnable) {
        auto& path = paths[pathIdx];

        // Assign a unique source ID for this path (critical for extend mode)
        UINT32 sourceId = nextSourceId++;
        path.sourceInfo.id = sourceId;

        // Create source mode with correct resolution and position
        DISPLAYCONFIG_MODE_INFO sourceMode = {};
        sourceMode.infoType = DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE;
        sourceMode.adapterId = path.sourceInfo.adapterId;
        sourceMode.id = sourceId;
        sourceMode.sourceMode.width = config.width;
        sourceMode.sourceMode.height = config.height;
        sourceMode.sourceMode.pixelFormat = DISPLAYCONFIG_PIXELFORMAT_32BPP;
        sourceMode.sourceMode.position.x = config.positionX;
        sourceMode.sourceMode.position.y = config.positionY;

        newModes.push_back(sourceMode);
        path.sourceInfo.modeInfoIdx = static_cast<UINT32>(newModes.size() - 1);

        // Create target mode with correct refresh rate
        DISPLAYCONFIG_MODE_INFO targetMode = {};
        targetMode.infoType = DISPLAYCONFIG_MODE_INFO_TYPE_TARGET;
        targetMode.adapterId = path.targetInfo.adapterId;
        targetMode.id = path.targetInfo.id;
        // Set refresh rate (convert Hz to rational)
        auto refreshNumerator = static_cast<UINT32>(config.refreshRate * 1000);
        targetMode.targetMode.targetVideoSignalInfo.vSyncFreq.Numerator = refreshNumerator;
        targetMode.targetMode.targetVideoSignalInfo.vSyncFreq.Denominator = 1000;
        targetMode.targetMode.targetVideoSignalInfo.hSyncFreq.Numerator = 0;
        targetMode.targetMode.targetVideoSignalInfo.hSyncFreq.Denominator = 0;
        // Active size matches resolution
        targetMode.targetMode.targetVideoSignalInfo.activeSize.cx = config.width;
        targetMode.targetMode.targetVideoSignalInfo.activeSize.cy = config.height;
        targetMode.targetMode.targetVideoSignalInfo.totalSize.cx = config.width;
        targetMode.targetMode.targetVideoSignalInfo.totalSize.cy = config.height;
        targetMode.targetMode.targetVideoSignalInfo.videoStandard = 255; // D3DKMDT_VSS_OTHER
        targetMode.targetMode.targetVideoSignalInfo.scanLineOrdering = DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE;

        newModes.push_back(targetMode);
        path.targetInfo.modeInfoIdx = static_cast<UINT32>(newModes.size() - 1);

        // Set rotation (convert degrees to enum)
        switch (config.rotation) {
            case 90:  path.targetInfo.rotation = DISPLAYCONFIG_ROTATION_ROTATE90;  break;
            case 180: path.targetInfo.rotation = DISPLAYCONFIG_ROTATION_ROTATE180; break;
            case 270: path.targetInfo.rotation = DISPLAYCONFIG_ROTATION_ROTATE270; break;
            default:  path.targetInfo.rotation = DISPLAYCONFIG_ROTATION_IDENTITY;  break;
        }

        // Enable this path
        path.flags |= DISPLAYCONFIG_PATH_ACTIVE;
    }

    // Update mode indices for disabled paths to invalid
    for (UINT32 i = 0; i < pathCount; i++) {
        if (!(paths[i].flags & DISPLAYCONFIG_PATH_ACTIVE)) {
            paths[i].sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
            paths[i].targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        }
    }

    // Apply the configuration with our new modes array
    auto modeCount2 = static_cast<UINT32>(newModes.size());
    DWORD flags = SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES;
    result = SetDisplayConfig(pathCount, paths.data(), modeCount2, newModes.data(), flags);

    if (result != ERROR_SUCCESS) {
        return -300 - static_cast<int>(result);
    }

    return 0;
}
