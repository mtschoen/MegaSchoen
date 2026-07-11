// Part of the DisplayManagerNative component. Included by DisplayManagerNative.cpp; do not compile directly.
#ifndef AISLOP_TU_FRAGMENT
#error                                                                                                                 \
    "DisplayManagerNative.ApplyConfiguration.cpp is a fragment included by DisplayManagerNative.cpp; do not compile it directly"
#endif

#include "DisplayManagerNative.internal.h"

namespace
{

// Structure to hold parsed display config from JSON
struct DisplayConfigRequest
{
    UINT16 edidManufactureId = 0;
    UINT16 edidProductCodeId = 0;
    std::string edidSerialNumber;
    std::string edidManufactureDate;
    std::string edidContainerId;
    int width = 0;
    int height = 0;
    int positionX = 0;
    int positionY = 0;
    double refreshRate = 0.0;
    int rotation = 0; // degrees: 0, 90, 180, 270
};

// Parse the JSON array of display configs into wantedList.
// Returns 0 on success, or the ApplyConfiguration error code to propagate.
int ParseDisplayConfigRequests(const char *configJson, std::vector<DisplayConfigRequest> &wantedList)
{
    try
    {
        json configList = json::parse(configJson);
        if (!configList.is_array())
        {
            return -2; // Not a JSON array
        }
        for (const auto &item : configList)
        {
            if (!item.is_object())
                continue;
            DisplayConfigRequest req;
            req.edidManufactureId = item.value("edidManufactureId", static_cast<UINT16>(0));
            req.edidProductCodeId = item.value("edidProductCodeId", static_cast<UINT16>(0));
            req.edidSerialNumber = item.value("edidSerialNumber", "");
            req.edidManufactureDate = item.value("edidManufactureDate", "");
            req.edidContainerId = item.value("edidContainerId", "");
            req.width = item.value("width", 0);
            req.height = item.value("height", 0);
            req.positionX = item.value("positionX", 0);
            req.positionY = item.value("positionY", 0);
            req.refreshRate = item.value("refreshRate", 60.0);
            req.rotation = item.value("rotation", 0);
            wantedList.push_back(req);
        }
        return 0;
    }
    catch (...)
    {
        return -3; // JSON parse error
    }
}

uint64_t LuidKey(const LUID &id)
{
    return (static_cast<uint64_t>(id.HighPart) << 32) | static_cast<uint64_t>(id.LowPart);
}

struct PathCandidate
{
    UINT32 pathIdx;
    uint64_t adapterKey;
    UINT32 sourceId;
};

// For each wanted display, collect ALL candidate paths from QDC_ALL_PATHS.
// Each monitor has multiple path entries with different source IDs per adapter.
// We must pick paths with non-conflicting (adapter, sourceId) pairs.
// Returns candidatesPerWanted[j] = all path entries that could serve wantedList[j].
std::vector<std::vector<PathCandidate>> MatchCandidatePaths(const std::vector<DISPLAYCONFIG_PATH_INFO> &paths,
                                                            UINT32 pathCount,
                                                            const std::vector<DisplayConfigRequest> &wantedList)
{
    std::vector<std::vector<PathCandidate>> candidatesPerWanted(wantedList.size());

    for (UINT32 i = 0; i < pathCount; i++)
    {
        DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = {};
        targetName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        targetName.header.size = sizeof(targetName);
        targetName.header.adapterId = paths[i].targetInfo.adapterId;
        targetName.header.id = paths[i].targetInfo.id;

        UINT16 curMfgId = 0, curProdId = 0;
        std::string curSerial;
        if (DisplayConfigGetDeviceInfo(&targetName.header) == ERROR_SUCCESS)
        {
            curMfgId = targetName.edidManufactureId;
            curProdId = targetName.edidProductCodeId;
            auto edid = ReadEdidFromRegistry(targetName.monitorDevicePath);
            curSerial = ParseEdidSerial(edid);
        }
        if (curMfgId == 0 && curProdId == 0)
            continue;

        for (size_t j = 0; j < wantedList.size(); j++)
        {
            const auto &w = wantedList[j];
            if (w.edidManufactureId != curMfgId || w.edidProductCodeId != curProdId)
                continue;
            if (!w.edidSerialNumber.empty() && !curSerial.empty() && w.edidSerialNumber != curSerial)
                continue;

            PathCandidate pc;
            pc.pathIdx = i;
            pc.adapterKey = LuidKey(paths[i].sourceInfo.adapterId);
            pc.sourceId = paths[i].sourceInfo.id;
            candidatesPerWanted[j].push_back(pc);
        }
    }

    return candidatesPerWanted;
}

// Greedily select one path per wanted display with non-conflicting source IDs.
std::vector<std::pair<UINT32, DisplayConfigRequest>> SelectPathsToEnable(
    const std::vector<std::vector<PathCandidate>> &candidatesPerWanted,
    const std::vector<DisplayConfigRequest> &wantedList)
{
    std::set<std::pair<uint64_t, UINT32>> usedSources;
    std::vector<std::pair<UINT32, DisplayConfigRequest>> pathsToEnable;

    for (size_t j = 0; j < wantedList.size(); j++)
    {
        for (auto &pc : candidatesPerWanted[j])
        {
            auto key = std::make_pair(pc.adapterKey, pc.sourceId);
            if (usedSources.insert(key).second)
            {
                pathsToEnable.push_back({pc.pathIdx, wantedList[j]});
                break;
            }
        }
    }

    return pathsToEnable;
}

// Build compact active paths for topology activation (SDC_TOPOLOGY_SUPPLIED).
std::vector<DISPLAYCONFIG_PATH_INFO> BuildTopologyPaths(
    const std::vector<DISPLAYCONFIG_PATH_INFO> &paths,
    const std::vector<std::pair<UINT32, DisplayConfigRequest>> &pathsToEnable)
{
    std::vector<DISPLAYCONFIG_PATH_INFO> topoPaths;
    for (auto &[pathIdx, config] : pathsToEnable)
    {
        auto p = paths[pathIdx];
        p.flags |= DISPLAYCONFIG_PATH_ACTIVE;
        p.sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        p.targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        topoPaths.push_back(p);
    }
    return topoPaths;
}

} // anonymous namespace

// Apply a full display configuration
// configJson: JSON array of display configs with EDID fields for matching
// All displays in the list will be enabled; all others will be disabled
// Returns: 0 on success, negative error code on failure
int ApplyConfiguration(const char *configJson)
{
    if (!configJson)
    {
        return -1;
    }

    std::vector<DisplayConfigRequest> wantedList;
    int parseResult = ParseDisplayConfigRequests(configJson, wantedList);
    if (parseResult != 0)
    {
        return parseResult;
    }

    // Get ALL display paths (including inactive ones)
    UINT32 pathCount = 0;
    UINT32 modeCount = 0;

    LONG result = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, &pathCount, &modeCount);
    if (result != ERROR_SUCCESS)
    {
        return -100 - static_cast<int>(result);
    }

    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);

    result = QueryDisplayConfig(QDC_ALL_PATHS, &pathCount, paths.data(), &modeCount, modes.data(), nullptr);
    if (result != ERROR_SUCCESS)
    {
        return -200 - static_cast<int>(result);
    }

    auto candidatesPerWanted = MatchCandidatePaths(paths, pathCount, wantedList);
    auto pathsToEnable = SelectPathsToEnable(candidatesPerWanted, wantedList);

    // Apply using SDC_TOPOLOGY_SUPPLIED — tells Windows which paths to activate.
    // Windows restores full config (positions, resolution, rotation) from its topology database.
    auto topoPaths = BuildTopologyPaths(paths, pathsToEnable);
    auto topoCount = static_cast<UINT32>(topoPaths.size());
    result = SetDisplayConfig(topoCount, topoPaths.data(), 0, nullptr,
                              SDC_APPLY | SDC_TOPOLOGY_SUPPLIED | SDC_ALLOW_PATH_ORDER_CHANGES);

    // SDC_TOPOLOGY_SUPPLIED restores the full configuration (positions, resolution,
    // rotation, refresh rate) from the Windows topology database. No further patching needed.

    if (result != ERROR_SUCCESS)
    {
        return -300 - static_cast<int>(result);
    }

    return 0;
}
