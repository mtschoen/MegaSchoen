// Part of the DisplayManagerNative component. Included by DisplayManagerNative.cpp; do not compile directly.
#ifndef AISLOP_TU_FRAGMENT
#error                                                                                                                 \
    "DisplayManagerNative.DisplayQuery.cpp is a fragment included by DisplayManagerNative.cpp; do not compile it directly"
#endif

#include "DisplayManagerNative.internal.h"

namespace
{

// Rotation enum -> degrees
int RotationToDegrees(DISPLAYCONFIG_ROTATION rotation)
{
    switch (rotation)
    {
    case DISPLAYCONFIG_ROTATION_ROTATE90:
        return 90;
    case DISPLAYCONFIG_ROTATION_ROTATE180:
        return 180;
    case DISPLAYCONFIG_ROTATION_ROTATE270:
        return 270;
    default:
        return 0;
    }
}

// Get source/target device names and EDID-derived fields for one path
void PopulateDeviceNames(const DISPLAYCONFIG_PATH_INFO &path, json &display)
{
    DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName = {};
    sourceName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
    sourceName.header.size = sizeof(sourceName);
    sourceName.header.adapterId = path.sourceInfo.adapterId;
    sourceName.header.id = path.sourceInfo.id;

    if (DisplayConfigGetDeviceInfo(&sourceName.header) == ERROR_SUCCESS)
    {
        display["deviceName"] = WideToUtf8(sourceName.viewGdiDeviceName);
    }
    else
    {
        display["deviceName"] = "";
    }

    display["targetAvailable"] = path.targetInfo.targetAvailable != 0;

    DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = {};
    targetName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
    targetName.header.size = sizeof(targetName);
    targetName.header.adapterId = path.targetInfo.adapterId;
    targetName.header.id = path.targetInfo.id;

    if (DisplayConfigGetDeviceInfo(&targetName.header) == ERROR_SUCCESS)
    {
        std::wstring monitorPath = targetName.monitorDevicePath;
        display["monitorName"] = WideToUtf8(targetName.monitorFriendlyDeviceName);
        display["monitorDevicePath"] = WideToUtf8(monitorPath);
        display["edidManufactureId"] = targetName.edidManufactureId;
        display["edidProductCodeId"] = targetName.edidProductCodeId;

        auto edid = ReadEdidFromRegistry(monitorPath);
        display["edidSerialNumber"] = ParseEdidSerial(edid);
        display["edidManufactureDate"] = ParseEdidManufactureDate(edid);
        display["edidContainerId"] = ParseEdidContainerId(edid);
    }
    else
    {
        display["monitorName"] = "";
        display["monitorDevicePath"] = "";
        display["edidManufactureId"] = 0;
        display["edidProductCodeId"] = 0;
        display["edidSerialNumber"] = "";
        display["edidManufactureDate"] = "";
        display["edidContainerId"] = "";
    }
}

// Get resolution, position, and refresh rate for one path's active mode
void PopulateModeInfo(const DISPLAYCONFIG_PATH_INFO &path, const std::vector<DISPLAYCONFIG_MODE_INFO> &modes,
                      UINT32 modeCount, json &display)
{
    display["width"] = 0;
    display["height"] = 0;
    display["positionX"] = 0;
    display["positionY"] = 0;
    display["refreshRate"] = 0.0;

    if (path.sourceInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID)
    {
        UINT32 modeIdx = path.sourceInfo.modeInfoIdx;
        if (modeIdx < modeCount && modes[modeIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE)
        {
            const auto &sourceMode = modes[modeIdx].sourceMode;
            display["width"] = static_cast<int>(sourceMode.width);
            display["height"] = static_cast<int>(sourceMode.height);
            display["positionX"] = static_cast<int>(sourceMode.position.x);
            display["positionY"] = static_cast<int>(sourceMode.position.y);
        }
    }

    if (path.targetInfo.modeInfoIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID)
    {
        UINT32 modeIdx = path.targetInfo.modeInfoIdx;
        if (modeIdx < modeCount && modes[modeIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_TARGET)
        {
            const auto &targetMode = modes[modeIdx].targetMode.targetVideoSignalInfo;
            // Refresh rate = vSyncFreq.Numerator / vSyncFreq.Denominator
            if (targetMode.vSyncFreq.Denominator > 0)
            {
                display["refreshRate"] =
                    static_cast<double>(targetMode.vSyncFreq.Numerator) / targetMode.vSyncFreq.Denominator;
            }
        }
    }
}

// Assemble the full JSON entry for one display path
json BuildDisplayEntry(const DISPLAYCONFIG_PATH_INFO &path, UINT32 pathIndex, bool isActive,
                       const std::vector<DISPLAYCONFIG_MODE_INFO> &modes, UINT32 modeCount)
{
    json display;

    display["pathIndex"] = static_cast<int>(pathIndex);
    display["isActive"] = isActive;

    PopulateDeviceNames(path, display);
    PopulateModeInfo(path, modes, modeCount, display);

    // Determine if this is the primary display (position 0,0)
    int posX = display["positionX"].get<int>();
    int posY = display["positionY"].get<int>();
    display["isPrimary"] = display["isActive"].get<bool>() && posX == 0 && posY == 0;

    display["rotation"] = RotationToDegrees(path.targetInfo.rotation);

    // Include IDs for matching/identification
    display["sourceId"] = static_cast<int>(path.sourceInfo.id);
    display["targetId"] = static_cast<int>(path.targetInfo.id);

    return display;
}

} // anonymous namespace

int GetAllDisplaysJson(char *buffer, int bufferSize)
{
    if (!buffer || bufferSize <= 0)
    {
        return -1; // Invalid parameters
    }

    json displays = json::array();

    // Use QueryDisplayConfig (CCD API) to get all display paths
    UINT32 pathCount = 0;
    UINT32 modeCount = 0;

    LONG result = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, &pathCount, &modeCount);
    if (result != ERROR_SUCCESS)
    {
        return -2; // Failed to get buffer sizes
    }

    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);

    result = QueryDisplayConfig(QDC_ALL_PATHS, &pathCount, paths.data(), &modeCount, modes.data(), nullptr);
    if (result != ERROR_SUCCESS)
    {
        return -3; // Failed to query display config
    }

    for (UINT32 i = 0; i < pathCount; i++)
    {
        const DISPLAYCONFIG_PATH_INFO &path = paths[i];

        // Skip paths without a target available (no monitor connected)
        // unless they're currently active
        bool isActive = (path.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0;
        if (!isActive && !path.targetInfo.targetAvailable)
        {
            continue;
        }

        displays.push_back(BuildDisplayEntry(path, i, isActive, modes, modeCount));
    }

    std::string jsonString = displays.dump(2);
    int jsonLength = static_cast<int>(jsonString.length());

    if (jsonLength >= bufferSize)
    {
        return -(jsonLength + 1); // Return negative required size
    }

    strcpy_s(buffer, bufferSize, jsonString.c_str());
    return jsonLength;
}
