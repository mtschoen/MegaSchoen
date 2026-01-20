#include "DisplayManagerNative.h"
#include "DisplayInfo.h"
#include "json.hpp"
#include <windows.h>
#include <SetupAPI.h>
#include <vector>
#include <string>
#include <sstream>
#include <iomanip>

#pragma comment(lib, "SetupAPI.lib")

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
#define EDD_GET_DEVICE_INTERFACE_NAME      0x00000001

// QueryDisplayConfig constants
#define QDC_ALL_PATHS                    0x00000001
#define QDC_ONLY_ACTIVE_PATHS            0x00000002
#define QDC_DATABASE_CURRENT             0x00000004
#define DISPLAYCONFIG_PATH_ACTIVE        0x00000001

using json = nlohmann::json;

// Helper function to extract monitor name from EDID data
std::wstring GetMonitorNameFromEDID(const BYTE* edid, DWORD edidSize) {
    if (!edid || edidSize < 128) {
        return L"";
    }

    // EDID descriptor blocks start at offset 54 (0x36)
    // Each descriptor is 18 bytes, there are 4 descriptors
    for (int i = 0; i < 4; i++) {
        int offset = 54 + (i * 18);

        // Check if this is a monitor name descriptor (type 0xFC)
        if (edid[offset] == 0x00 && edid[offset + 1] == 0x00 &&
            edid[offset + 2] == 0x00 && edid[offset + 3] == 0xFC) {

            // Monitor name starts at offset + 5, up to 13 bytes
            char name[14] = {0};
            int nameLen = 0;
            for (int j = 0; j < 13; j++) {
                char c = edid[offset + 5 + j];
                if (c == 0x0A || c == 0x00) break; // End marker
                name[nameLen++] = c;
            }
            name[nameLen] = '\0';

            // Convert to wide string
            if (nameLen > 0) {
                int wideLen = MultiByteToWideChar(CP_UTF8, 0, name, -1, nullptr, 0);
                if (wideLen > 0) {
                    std::vector<wchar_t> wideName(wideLen);
                    MultiByteToWideChar(CP_UTF8, 0, name, -1, wideName.data(), wideLen);
                    return std::wstring(wideName.data());
                }
            }
        }
    }

    return L"";
}

// Helper function to get EDID data from registry for a monitor device ID
std::wstring GetMonitorNameFromRegistry(const std::wstring& deviceID) {
    // Parse device ID to extract hardware ID
    // Format: \\?\DISPLAY#<manufacturer>#<instance>...
    // We want to look up: HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\<manufacturer>\<instance>\Device Parameters\EDID

    size_t displayPos = deviceID.find(L"DISPLAY#");
    if (displayPos == std::wstring::npos) {
        return L"";
    }

    // Extract manufacturer and instance
    size_t start = displayPos + 8; // Skip "DISPLAY#"
    size_t hash1 = deviceID.find(L'#', start);
    if (hash1 == std::wstring::npos) return L"";

    std::wstring manufacturer = deviceID.substr(start, hash1 - start);

    size_t hash2 = deviceID.find(L'#', hash1 + 1);
    if (hash2 == std::wstring::npos) return L"";

    std::wstring instance = deviceID.substr(hash1 + 1, hash2 - hash1 - 1);

    // Build registry path
    std::wstring regPath = L"SYSTEM\\CurrentControlSet\\Enum\\DISPLAY\\" + manufacturer + L"\\" + instance + L"\\Device Parameters";

    HKEY hKey;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, regPath.c_str(), 0, KEY_READ, &hKey) == ERROR_SUCCESS) {
        BYTE edid[256];
        DWORD edidSize = sizeof(edid);
        DWORD type;

        if (RegQueryValueExW(hKey, L"EDID", nullptr, &type, edid, &edidSize) == ERROR_SUCCESS && type == REG_BINARY) {
            RegCloseKey(hKey);
            return GetMonitorNameFromEDID(edid, edidSize);
        }

        RegCloseKey(hKey);
    }

    return L"";
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

    json jsonResult;
    json legacyDisplays = json::array();
    json queryConfigDisplays = json::array();
    
    DISPLAY_DEVICE d = {};
    d.cb = sizeof(d);

    int deviceIndex = 0;
    while (EnumDisplayDevicesW(nullptr, deviceIndex, &d, 0) != 0)
    {
        // Skip software/virtual display adapters (Remote Desktop, etc)
        // We want the physical display outputs which have DeviceNames like \\.\DISPLAY1, \\.\DISPLAY2, etc.
        bool isPhysicalDisplayOutput = wcsstr(d.DeviceID, L"PCI\\") != nullptr || wcsstr(d.DeviceID, L"USB\\") != nullptr;

        if (!isPhysicalDisplayOutput) {
            deviceIndex++;
            continue;
        }

        // Try to get monitor information
        // Use EDD_GET_DEVICE_INTERFACE_NAME flag to get more info about inactive devices
        DISPLAY_DEVICE monitor = {};
        monitor.cb = sizeof(monitor);
        bool hasMonitor = EnumDisplayDevicesW(d.DeviceName, 0, &monitor, EDD_GET_DEVICE_INTERFACE_NAME) != 0;

        json display;
        
        // Convert wide strings to UTF-8
        std::wstring deviceName(d.DeviceName);
        std::wstring deviceString(d.DeviceString);
        
        display["deviceName"] = std::string(deviceName.begin(), deviceName.end());
        display["deviceString"] = std::string(deviceString.begin(), deviceString.end());
        display["stateFlags"] = (int)d.StateFlags; // Debug info

        // Add DeviceID and DeviceKey for debugging
        std::wstring deviceID(d.DeviceID);
        std::wstring deviceKey(d.DeviceKey);
        display["deviceID"] = std::string(deviceID.begin(), deviceID.end());
        display["deviceKey"] = std::string(deviceKey.begin(), deviceKey.end());

        // Add monitor information if available
        std::wstring monitorName;
        std::wstring monitorID;

        if (hasMonitor) {
            monitorName = monitor.DeviceString;
            monitorID = monitor.DeviceID;
            display["monitorID"] = std::string(monitorID.begin(), monitorID.end());
            display["monitorStateFlags"] = (int)monitor.StateFlags;

            // Try to get a better monitor name from EDID if we only have "Generic PnP Monitor"
            if (monitorName == L"Generic PnP Monitor" && !monitorID.empty()) {
                std::wstring edidName = GetMonitorNameFromRegistry(monitorID);
                if (!edidName.empty()) {
                    monitorName = edidName;
                }
            }

            display["monitorName"] = std::string(monitorName.begin(), monitorName.end());
        } else {
            // No monitor info available (display might be disabled)
            display["monitorName"] = "";
            display["monitorID"] = "";
            display["monitorStateFlags"] = 0;
        }

        // Try to get current settings for enabled displays
        DEVMODEW dm = {};
        dm.dmSize = sizeof(dm);
        bool hasCurrentSettings = false;

        if (EnumDisplaySettingsW(d.DeviceName, ENUM_CURRENT_SETTINGS, &dm))
        {
            // Display is enabled and has current settings
            hasCurrentSettings = true;
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

        // isActive = true if we have current settings (display is enabled)
        display["isActive"] = hasCurrentSettings;

        legacyDisplays.push_back(display);
        deviceIndex++;
    }

    // Now try QueryDisplayConfig method
    // Use QDC_ALL_PATHS to get all possible paths (active and inactive)
    // Note: QDC_ALL_PATHS and QDC_DATABASE_CURRENT are mutually exclusive
    UINT32 pathCount = 0;
    UINT32 modeCount = 0;

    LONG queryResult = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, &pathCount, &modeCount);
    jsonResult["queryConfigError"] = (int)queryResult;
    jsonResult["queryConfigPathCount"] = (int)pathCount;
    jsonResult["queryConfigModeCount"] = (int)modeCount;

    if (queryResult == ERROR_SUCCESS && pathCount > 0) {
        std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
        std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);

        queryResult = QueryDisplayConfig(QDC_ALL_PATHS, &pathCount, paths.data(),
                                   &modeCount, modes.data(), nullptr);
        jsonResult["queryConfigQueryError"] = (int)queryResult;
        if (queryResult == ERROR_SUCCESS) {
            for (UINT32 i = 0; i < pathCount; i++) {
                json display;
                
                DISPLAYCONFIG_PATH_INFO& path = paths[i];
                display["pathIndex"] = (int)i;
                display["isActive"] = (path.flags & DISPLAYCONFIG_PATH_ACTIVE) != 0;
                display["pathFlags"] = (int)path.flags;

                // Include adapter and source/target IDs for matching
                display["sourceAdapterIdHigh"] = (unsigned int)(path.sourceInfo.adapterId.HighPart);
                display["sourceAdapterIdLow"] = (unsigned int)(path.sourceInfo.adapterId.LowPart);
                display["sourceId"] = (int)path.sourceInfo.id;
                display["targetAdapterIdHigh"] = (unsigned int)(path.targetInfo.adapterId.HighPart);
                display["targetAdapterIdLow"] = (unsigned int)(path.targetInfo.adapterId.LowPart);
                display["targetId"] = (int)path.targetInfo.id;
                
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
                
                // Get target device info (monitor) - try even for inactive/unavailable targets
                display["targetAvailable"] = path.targetInfo.targetAvailable ? true : false;
                display["outputTechnology"] = (int)path.targetInfo.outputTechnology;

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
                    display["outputTechnology"] = (int)path.targetInfo.outputTechnology;
                } else {
                    // If we can't get device info, still include the display with whatever we have
                    display["monitorName"] = "";
                    display["monitorDevicePath"] = "";
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

int ApplyDisplayConfiguration(const char* configJson)
{
    if (!configJson) {
        return -1; // Invalid parameter
    }

    try {
        // Parse the JSON configuration
        json config = json::parse(configJson);

        // Get all current displays
        DISPLAY_DEVICE currentDisplay = {};
        currentDisplay.cb = sizeof(currentDisplay);

        std::vector<std::tuple<std::wstring, std::wstring, std::wstring>> currentDisplays; // DeviceName, MonitorID, MonitorName

        int deviceIndex = 0;
        while (EnumDisplayDevicesW(nullptr, deviceIndex, &currentDisplay, 0) != 0)
        {
            // Get monitor information
            DISPLAY_DEVICE monitor = {};
            monitor.cb = sizeof(monitor);
            bool hasMonitor = EnumDisplayDevicesW(currentDisplay.DeviceName, 0, &monitor, 0) != 0;

            bool isPCIDevice = wcsstr(currentDisplay.DeviceID, L"PCI\\") != nullptr;

            if (isPCIDevice && hasMonitor) {
                std::wstring deviceName(currentDisplay.DeviceName);
                std::wstring monitorID(monitor.DeviceID);
                std::wstring monitorName(monitor.DeviceString);
                currentDisplays.push_back(std::make_tuple(deviceName, monitorID, monitorName));
            }

            deviceIndex++;
        }

        // Process each current display
        for (const auto& [deviceName, monitorID, monitorName] : currentDisplays)
        {
            // Convert wide strings to narrow for comparison
            std::string deviceNameStr(deviceName.begin(), deviceName.end());
            std::string monitorIDStr(monitorID.begin(), monitorID.end());
            std::string monitorNameStr(monitorName.begin(), monitorName.end());

            // Try to match this display to one in the config
            bool shouldEnable = false;
            bool matched = false;

            for (const auto& configDisplay : config["displays"]) {
                std::string cfgMonitorID = configDisplay["identifier"]["monitorId"].get<std::string>();
                std::string cfgMonitorName = configDisplay["identifier"]["monitorName"].get<std::string>();
                std::string cfgDeviceName = configDisplay["identifier"]["deviceName"].get<std::string>();

                // Match by MonitorID first (most specific)
                if (!cfgMonitorID.empty() && !monitorIDStr.empty() && cfgMonitorID == monitorIDStr) {
                    matched = true;
                    shouldEnable = configDisplay["enabled"].get<bool>();
                    break;
                }

                // Fall back to MonitorName
                if (!cfgMonitorName.empty() && !monitorNameStr.empty() && cfgMonitorName == monitorNameStr) {
                    matched = true;
                    shouldEnable = configDisplay["enabled"].get<bool>();
                    break;
                }

                // Last resort: DeviceName (least reliable as it can change)
                if (!cfgDeviceName.empty() && !deviceNameStr.empty() && cfgDeviceName == deviceNameStr) {
                    matched = true;
                    shouldEnable = configDisplay["enabled"].get<bool>();
                    break;
                }
            }

            // If we didn't match this display in the config, leave it as-is
            if (!matched) {
                continue;
            }

            // Apply the enable/disable setting
            DEVMODEW dm = {};
            dm.dmSize = sizeof(dm);

            if (shouldEnable) {
                // Enable the display - get current or registry settings
                if (!EnumDisplaySettingsW(deviceName.c_str(), ENUM_CURRENT_SETTINGS, &dm)) {
                    // If no current settings, try registry settings
                    if (!EnumDisplaySettingsW(deviceName.c_str(), ENUM_REGISTRY_SETTINGS, &dm)) {
                        // Can't get settings, skip this display
                        continue;
                    }
                }

                // Apply the settings to enable the display
                dm.dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
                LONG result = ChangeDisplaySettingsExW(deviceName.c_str(), &dm, nullptr, CDS_UPDATEREGISTRY | CDS_NORESET, nullptr);
            } else {
                // Disable the display
                LONG result = ChangeDisplaySettingsExW(deviceName.c_str(), nullptr, nullptr, CDS_UPDATEREGISTRY | CDS_NORESET, nullptr);
            }
        }

        // Apply all changes at once
        ChangeDisplaySettingsExW(nullptr, nullptr, nullptr, 0, nullptr);

        return 0; // Success
    }
    catch (const json::exception& e) {
        // JSON parsing error
        return -3;
    }
    catch (...) {
        // Unknown error
        return -4;
    }
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