#include "DisplayManagerNative.h"
#include "json.hpp"
#include <windows.h>
#include <vector>
#include <string>
#include <set>
#include <tuple>
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

// Read EDID binary data from the registry for a given monitor device path
std::vector<BYTE> ReadEdidFromRegistry(const std::wstring& monitorDevicePath) {
    std::vector<BYTE> edid;
    if (monitorDevicePath.empty()) return edid;

    // Parse instance path from device interface path
    // Input:  \\?\DISPLAY#MODEL#INSTANCE#{GUID}
    // Output: DISPLAY\MODEL\INSTANCE
    std::wstring path = monitorDevicePath;

    // Remove \\?\ prefix
    if (path.size() > 4 && path[0] == L'\\' && path[1] == L'\\' && path[2] == L'?' && path[3] == L'\\') {
        path = path.substr(4);
    }

    // Remove GUID suffix (from last #{)
    auto guidPos = path.rfind(L"#{");
    if (guidPos != std::wstring::npos) {
        path = path.substr(0, guidPos);
    }

    // Replace # with backslash to form instance path
    for (auto& ch : path) {
        if (ch == L'#') ch = L'\\';
    }

    // Read EDID from registry
    std::wstring regPath = L"SYSTEM\\CurrentControlSet\\Enum\\" + path + L"\\Device Parameters";
    HKEY hKey;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, regPath.c_str(), 0, KEY_READ, &hKey) == ERROR_SUCCESS) {
        DWORD dataSize = 0;
        if (RegQueryValueExW(hKey, L"EDID", nullptr, nullptr, nullptr, &dataSize) == ERROR_SUCCESS && dataSize > 0) {
            edid.resize(dataSize);
            RegQueryValueExW(hKey, L"EDID", nullptr, nullptr, edid.data(), &dataSize);
        }
        RegCloseKey(hKey);
    }

    return edid;
}

// Parse the serial number string from EDID descriptor blocks
std::string ParseEdidSerial(const std::vector<BYTE>& edid) {
    if (edid.size() < 128) return "";

    // Check four 18-byte descriptor blocks starting at byte 54
    for (int i = 54; i <= 108; i += 18) {
        // Tag 0xFF = serial number string descriptor
        if (edid[i] == 0 && edid[i + 1] == 0 && edid[i + 2] == 0 && edid[i + 3] == 0xFF) {
            std::string serial;
            for (int j = 5; j < 18; j++) {
                auto c = static_cast<char>(edid[i + j]);
                if (c == '\n' || c == '\0') break;
                serial += c;
            }
            while (!serial.empty() && serial.back() == ' ') serial.pop_back();
            if (!serial.empty()) return serial;
        }
    }

    // Fall back to numeric serial from EDID bytes 12-15
    uint32_t numericSerial = edid[12] | (edid[13] << 8) | (edid[14] << 16) | (edid[15] << 24);
    if (numericSerial != 0) {
        return std::to_string(numericSerial);
    }

    return "";
}

// Parse manufacture week and year from EDID bytes 16-17
// Returns "YYYY-WNN" (e.g. "2019-W23") or empty if unavailable
std::string ParseEdidManufactureDate(const std::vector<BYTE>& edid) {
    if (edid.size() < 128) return "";

    BYTE week = edid[16];
    BYTE yearOffset = edid[17];
    int year = 1990 + yearOffset;

    if (year < 1990 || year > 2100) return "";

    std::string result = std::to_string(year);
    if (week >= 1 && week <= 53) {
        result += "-W" + (week < 10 ? std::string("0") : "") + std::to_string(week);
    }
    return result;
}

// Scan EDID extension blocks for a DisplayID Container ID (128-bit UUID)
// Returns hex string like "a1b2c3d4..." or empty if not found
std::string ParseEdidContainerId(const std::vector<BYTE>& edid) {
    if (edid.size() < 128) return "";

    int extensionCount = edid[126];
    if (extensionCount == 0 || edid.size() < static_cast<size_t>(128 + extensionCount * 128)) {
        return "";
    }

    // Scan each 128-byte extension block
    for (int ext = 0; ext < extensionCount; ext++) {
        size_t extBase = 128 + ext * 128;
        BYTE tag = edid[extBase];

        // 0x70 = DisplayID extension
        if (tag != 0x70) continue;

        // DisplayID structure: byte 0=version, byte 1=data length, byte 2=product type,
        // byte 3=extension count, then data blocks
        // Each data block: byte 0=tag, byte 1=revision, byte 2=payload length, then payload
        size_t dbStart = extBase + 5; // skip ext tag + DisplayID header (4 bytes)
        BYTE dataLen = edid[extBase + 2];
        size_t dbEnd = extBase + 5 + dataLen;
        if (dbEnd > extBase + 127) dbEnd = extBase + 127;

        size_t pos = dbStart;
        while (pos + 3 <= dbEnd) {
            BYTE dbTag = edid[pos];
            BYTE dbPayloadLen = edid[pos + 2];
            size_t payloadStart = pos + 3;

            // Tag 0x29 = Container ID (16 bytes UUID)
            if (dbTag == 0x29 && dbPayloadLen >= 16 && payloadStart + 16 <= edid.size()) {
                // Check it's not all zeros
                bool allZero = true;
                for (int k = 0; k < 16; k++) {
                    if (edid[payloadStart + k] != 0) { allZero = false; break; }
                }
                if (!allZero) {
                    static const char hex[] = "0123456789abcdef";
                    std::string uuid;
                    uuid.reserve(32);
                    for (int k = 0; k < 16; k++) {
                        uuid += hex[(edid[payloadStart + k] >> 4) & 0x0F];
                        uuid += hex[edid[payloadStart + k] & 0x0F];
                    }
                    return uuid;
                }
            }

            pos = payloadStart + dbPayloadLen;
        }
    }

    return "";
}

// Structure to hold parsed display config from JSON
struct DisplayConfigRequest {
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

        std::wstring monitorPath;
        if (DisplayConfigGetDeviceInfo(&targetName.header) == ERROR_SUCCESS) {
            monitorPath = targetName.monitorDevicePath;
            display["monitorName"] = WideToUtf8(targetName.monitorFriendlyDeviceName);
            display["monitorDevicePath"] = WideToUtf8(monitorPath);
            display["edidManufactureId"] = targetName.edidManufactureId;
            display["edidProductCodeId"] = targetName.edidProductCodeId;

            // Read EDID from registry for serial, manufacture date, and container ID
            auto edid = ReadEdidFromRegistry(monitorPath);
            display["edidSerialNumber"] = ParseEdidSerial(edid);
            display["edidManufactureDate"] = ParseEdidManufactureDate(edid);
            display["edidContainerId"] = ParseEdidContainerId(edid);
        } else {
            display["monitorName"] = "";
            display["monitorDevicePath"] = "";
            display["edidManufactureId"] = 0;
            display["edidProductCodeId"] = 0;
            display["edidSerialNumber"] = "";
            display["edidManufactureDate"] = "";
            display["edidContainerId"] = "";
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

int GetSupportedModesJson(int edidManufactureId, int edidProductCodeId,
                          char* buffer, int bufferSize)
{
    if (!buffer || bufferSize <= 0) {
        return -1;
    }

    UINT32 pathCount = 0, modeCount = 0;
    if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &pathCount, &modeCount) != ERROR_SUCCESS) {
        return -2;
    }
    std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);
    if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &pathCount, paths.data(),
            &modeCount, modes.data(), nullptr) != ERROR_SUCCESS) {
        return -3;
    }

    std::wstring gdiName;
    for (UINT32 i = 0; i < pathCount; i++) {
        DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = {};
        targetName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        targetName.header.size = sizeof(targetName);
        targetName.header.adapterId = paths[i].targetInfo.adapterId;
        targetName.header.id = paths[i].targetInfo.id;
        if (DisplayConfigGetDeviceInfo(&targetName.header) != ERROR_SUCCESS) continue;
        if (targetName.edidManufactureId != edidManufactureId
            || targetName.edidProductCodeId != edidProductCodeId) continue;

        DISPLAYCONFIG_SOURCE_DEVICE_NAME sourceName = {};
        sourceName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
        sourceName.header.size = sizeof(sourceName);
        sourceName.header.adapterId = paths[i].sourceInfo.adapterId;
        sourceName.header.id = paths[i].sourceInfo.id;
        if (DisplayConfigGetDeviceInfo(&sourceName.header) == ERROR_SUCCESS) {
            gdiName = sourceName.viewGdiDeviceName;
        }
        break;
    }

    if (gdiName.empty()) {
        return -4;
    }

    std::set<std::tuple<int, int, int>> seen;
    json modesArray = json::array();
    DEVMODEW dm = {};
    dm.dmSize = sizeof(dm);
    for (DWORD m = 0; EnumDisplaySettingsExW(gdiName.c_str(), m, &dm, 0); m++) {
        int w = static_cast<int>(dm.dmPelsWidth);
        int h = static_cast<int>(dm.dmPelsHeight);
        int hz = static_cast<int>(dm.dmDisplayFrequency);
        if (w == 0 || h == 0) continue;
        if (!seen.insert(std::make_tuple(w, h, hz)).second) continue;
        json mode;
        mode["width"] = w;
        mode["height"] = h;
        mode["refreshRate"] = static_cast<double>(hz);
        modesArray.push_back(mode);
    }

    std::string jsonString = modesArray.dump(2);
    int jsonLength = static_cast<int>(jsonString.length());
    if (jsonLength >= bufferSize) {
        return -(jsonLength + 1);
    }
    strcpy_s(buffer, bufferSize, jsonString.c_str());
    return jsonLength;
}

// Apply a full display configuration
// configJson: JSON array of display configs with EDID fields for matching
// All displays in the list will be enabled; all others will be disabled
// Returns: 0 on success, negative error code on failure
int ApplyConfiguration(const char* configJson)
{
    if (!configJson) {
        return -1;
    }

    // Parse the JSON array of display configs
    std::vector<DisplayConfigRequest> wantedList;
    try {
        json configList = json::parse(configJson);
        if (!configList.is_array()) {
            return -2; // Not a JSON array
        }
        for (const auto& item : configList) {
            if (!item.is_object()) continue;
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

    // For each wanted display, collect ALL candidate paths from QDC_ALL_PATHS.
    // Each monitor has multiple path entries with different source IDs per adapter.
    // We must pick paths with non-conflicting (adapter, sourceId) pairs.
    auto luidKey = [](const LUID& id) -> uint64_t {
        return (static_cast<uint64_t>(id.HighPart) << 32) | static_cast<uint64_t>(id.LowPart);
    };

    struct PathCandidate {
        UINT32 pathIdx;
        uint64_t adapterKey;
        UINT32 sourceId;
    };

    // candidatesPerWanted[j] = all path entries that could serve wantedList[j]
    std::vector<std::vector<PathCandidate>> candidatesPerWanted(wantedList.size());

    for (UINT32 i = 0; i < pathCount; i++) {
        DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = {};
        targetName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        targetName.header.size = sizeof(targetName);
        targetName.header.adapterId = paths[i].targetInfo.adapterId;
        targetName.header.id = paths[i].targetInfo.id;

        UINT16 curMfgId = 0, curProdId = 0;
        std::string curSerial;
        if (DisplayConfigGetDeviceInfo(&targetName.header) == ERROR_SUCCESS) {
            curMfgId = targetName.edidManufactureId;
            curProdId = targetName.edidProductCodeId;
            auto edid = ReadEdidFromRegistry(targetName.monitorDevicePath);
            curSerial = ParseEdidSerial(edid);
        }
        if (curMfgId == 0 && curProdId == 0) continue;

        for (size_t j = 0; j < wantedList.size(); j++) {
            auto& w = wantedList[j];
            if (w.edidManufactureId != curMfgId || w.edidProductCodeId != curProdId) continue;
            if (!w.edidSerialNumber.empty() && !curSerial.empty()
                && w.edidSerialNumber != curSerial) continue;

            PathCandidate pc;
            pc.pathIdx = i;
            pc.adapterKey = luidKey(paths[i].sourceInfo.adapterId);
            pc.sourceId = paths[i].sourceInfo.id;
            candidatesPerWanted[j].push_back(pc);
        }
    }

    // Greedily select one path per wanted display with non-conflicting source IDs.
    std::set<std::pair<uint64_t, UINT32>> usedSources;
    std::vector<std::pair<UINT32, DisplayConfigRequest>> pathsToEnable;

    for (size_t j = 0; j < wantedList.size(); j++) {
        for (auto& pc : candidatesPerWanted[j]) {
            auto key = std::make_pair(pc.adapterKey, pc.sourceId);
            if (usedSources.count(key) == 0) {
                usedSources.insert(key);
                pathsToEnable.push_back({pc.pathIdx, wantedList[j]});
                break;
            }
        }
    }

    // ---- Step 1: SDC_TOPOLOGY_SUPPLIED activates the right monitors across adapters.
    // Windows restores resolution/refresh/rotation from its topology database. It also
    // restores a *remembered* position layout — but that DB remembers only one layout per
    // monitor-set, so different profiles sharing the same monitors get the wrong positions.
    // Step 2 (below) overrides position with the profile's explicit values.
    std::vector<DISPLAYCONFIG_PATH_INFO> topoPaths;
    for (auto& [pathIdx, config] : pathsToEnable) {
        auto p = paths[pathIdx];
        p.flags |= DISPLAYCONFIG_PATH_ACTIVE;
        p.sourceInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        p.targetInfo.modeInfoIdx = DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        topoPaths.push_back(p);
    }

    auto topoCount = static_cast<UINT32>(topoPaths.size());
    result = SetDisplayConfig(topoCount, topoPaths.data(), 0, nullptr,
        SDC_APPLY | SDC_TOPOLOGY_SUPPLIED | SDC_ALLOW_PATH_ORDER_CHANGES);

    if (result != ERROR_SUCCESS) {
        return -300 - static_cast<int>(result);
    }

    // ---- Step 2: override topology's remembered positions with the profile's explicit
    // positions. Spiked 2026-05-28: SDC_USE_SUPPLIED_DISPLAY_CONFIG on active paths works
    // cross-adapter and lands exact. Map each active path -> monitor via its TARGET EDID,
    // never the source-mode .id (that's a per-adapter sourceId, not globally unique).
    // Positioning is best-effort: if it fails, the monitors are already activated by Step 1,
    // so we surface a distinct -400 code rather than discarding the successful activation.
    UINT32 activePathCount = 0, activeModeCount = 0;
    if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &activePathCount, &activeModeCount) != ERROR_SUCCESS) {
        return 0;
    }
    std::vector<DISPLAYCONFIG_PATH_INFO> activePaths(activePathCount);
    std::vector<DISPLAYCONFIG_MODE_INFO> activeModes(activeModeCount);
    if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &activePathCount, activePaths.data(),
            &activeModeCount, activeModes.data(), nullptr) != ERROR_SUCCESS) {
        return 0;
    }

    bool patchedAny = false;
    for (UINT32 i = 0; i < activePathCount; i++) {
        // Resolve this active path's target EDID (manufacturer/product + registry serial).
        DISPLAYCONFIG_TARGET_DEVICE_NAME targetName = {};
        targetName.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
        targetName.header.size = sizeof(targetName);
        targetName.header.adapterId = activePaths[i].targetInfo.adapterId;
        targetName.header.id = activePaths[i].targetInfo.id;

        UINT16 mfgId = 0, prodId = 0;
        std::string serial;
        if (DisplayConfigGetDeviceInfo(&targetName.header) == ERROR_SUCCESS) {
            mfgId = targetName.edidManufactureId;
            prodId = targetName.edidProductCodeId;
            serial = ParseEdidSerial(ReadEdidFromRegistry(targetName.monitorDevicePath));
        }
        if (mfgId == 0 && prodId == 0) continue;

        // Match to a wanted config using the same EDID cascade as activation.
        const DisplayConfigRequest* match = nullptr;
        for (const auto& w : wantedList) {
            if (w.edidManufactureId != mfgId || w.edidProductCodeId != prodId) continue;
            if (!w.edidSerialNumber.empty() && !serial.empty() && w.edidSerialNumber != serial) continue;
            match = &w;
            break;
        }
        if (!match) continue;

        // Patch this active path's modes to the wanted config: position + resolution
        // (source mode), rotation (path target info), and refresh (rebuilt target mode).
        // Position is always patched; resolution/refresh/rotation only when the wanted
        // value is non-zero/meaningful, so a position-only profile behaves as before.
        UINT32 srcIdx = activePaths[i].sourceInfo.modeInfoIdx;
        if (srcIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && srcIdx < activeModeCount
            && activeModes[srcIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE) {
            auto& src = activeModes[srcIdx].sourceMode;
            src.position.x = match->positionX;
            src.position.y = match->positionY;
            if (match->width > 0 && match->height > 0) {
                src.width = static_cast<UINT32>(match->width);
                src.height = static_cast<UINT32>(match->height);
            }
            patchedAny = true;
        }

        // Rotation: set the path's target rotation enum from the wanted degrees.
        switch (match->rotation) {
            case 90:  activePaths[i].targetInfo.rotation = DISPLAYCONFIG_ROTATION_ROTATE90;  break;
            case 180: activePaths[i].targetInfo.rotation = DISPLAYCONFIG_ROTATION_ROTATE180; break;
            case 270: activePaths[i].targetInfo.rotation = DISPLAYCONFIG_ROTATION_ROTATE270; break;
            default:  activePaths[i].targetInfo.rotation = DISPLAYCONFIG_ROTATION_IDENTITY;  break;
        }

        // Refresh + resolution: rebuild the target mode's video signal so the requested
        // refresh and active size take effect. Mirrors the pre-cb7da8e recipe.
        UINT32 tgtIdx = activePaths[i].targetInfo.modeInfoIdx;
        if (tgtIdx != DISPLAYCONFIG_PATH_MODE_IDX_INVALID && tgtIdx < activeModeCount
            && activeModes[tgtIdx].infoType == DISPLAYCONFIG_MODE_INFO_TYPE_TARGET
            && match->refreshRate > 0.0 && match->width > 0 && match->height > 0) {
            auto& vsi = activeModes[tgtIdx].targetMode.targetVideoSignalInfo;
            vsi.vSyncFreq.Numerator = static_cast<UINT32>(match->refreshRate * 1000.0);
            vsi.vSyncFreq.Denominator = 1000;
            vsi.hSyncFreq.Numerator = 0;
            vsi.hSyncFreq.Denominator = 0;
            vsi.activeSize.cx = static_cast<UINT32>(match->width);
            vsi.activeSize.cy = static_cast<UINT32>(match->height);
            vsi.totalSize.cx = static_cast<UINT32>(match->width);
            vsi.totalSize.cy = static_cast<UINT32>(match->height);
            vsi.videoStandard = 255; // D3DKMDT_VSS_OTHER
            vsi.scanLineOrdering = DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE;
            patchedAny = true;
        }
    }

    if (patchedAny) {
        LONG posResult = SetDisplayConfig(activePathCount, activePaths.data(),
            activeModeCount, activeModes.data(),
            SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_SAVE_TO_DATABASE | SDC_ALLOW_CHANGES);
        if (posResult != ERROR_SUCCESS) {
            return -400 - static_cast<int>(posResult);
        }
    }

    return 0;
}
