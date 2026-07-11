// Part of the DisplayManagerNative component. Included by DisplayManagerNative.cpp; do not compile directly.
#ifndef AISLOP_TU_FRAGMENT
#error "DisplayManagerNative.Edid.cpp is a fragment included by DisplayManagerNative.cpp; do not compile it directly"
#endif

#include "DisplayManagerNative.internal.h"

namespace
{

// Helper to convert wide string to UTF-8
std::string WideToUtf8(const std::wstring &wide)
{
    if (wide.empty())
        return "";
    int utf8Len = WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (utf8Len <= 0)
        return "";
    std::vector<char> utf8(utf8Len);
    WideCharToMultiByte(CP_UTF8, 0, wide.c_str(), -1, utf8.data(), utf8Len, nullptr, nullptr);
    return {utf8.data()};
}

// Read EDID binary data from the registry for a given monitor device path
std::vector<BYTE> ReadEdidFromRegistry(const std::wstring &monitorDevicePath)
{
    std::vector<BYTE> edid;
    if (monitorDevicePath.empty())
        return edid;

    // Parse instance path from device interface path
    // Input:  \\?\DISPLAY#MODEL#INSTANCE#{GUID}
    // Output: DISPLAY\MODEL\INSTANCE
    std::wstring path = monitorDevicePath;

    if (path.size() > 4 && path[0] == L'\\' && path[1] == L'\\' && path[2] == L'?' && path[3] == L'\\')
    {
        path = path.substr(4);
    }

    // Remove GUID suffix (from last #{)
    auto guidPos = path.rfind(L"#{");
    if (guidPos != std::wstring::npos)
    {
        path.resize(guidPos);
    }

    // Replace # with backslash to form instance path
    std::replace(path.begin(), path.end(), L'#', L'\\');

    // Read EDID from registry
    std::wstring regPath = L"SYSTEM\\CurrentControlSet\\Enum\\" + path + L"\\Device Parameters";
    HKEY hKey;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, regPath.c_str(), 0, KEY_READ, &hKey) == ERROR_SUCCESS)
    {
        DWORD dataSize = 0;
        if (RegQueryValueExW(hKey, L"EDID", nullptr, nullptr, nullptr, &dataSize) == ERROR_SUCCESS && dataSize > 0)
        {
            edid.resize(dataSize);
            RegQueryValueExW(hKey, L"EDID", nullptr, nullptr, edid.data(), &dataSize);
        }
        RegCloseKey(hKey);
    }

    return edid;
}

std::string ParseEdidSerial(const std::vector<BYTE> &edid)
{
    if (edid.size() < 128)
        return "";

    for (int i = 54; i <= 108; i += 18)
    {
        // Tag 0xFF = serial number string descriptor
        if (edid[i] == 0 && edid[i + 1] == 0 && edid[i + 2] == 0 && edid[i + 3] == 0xFF)
        {
            std::string serial;
            for (int j = 5; j < 18; j++)
            {
                auto c = static_cast<char>(edid[i + j]);
                if (c == '\n' || c == '\0')
                    break;
                serial += c;
            }
            while (!serial.empty() && serial.back() == ' ')
                serial.pop_back();
            if (!serial.empty())
                return serial;
        }
    }

    // Fall back to numeric serial from EDID bytes 12-15
    uint32_t numericSerial = edid[12] | (edid[13] << 8) | (edid[14] << 16) | (edid[15] << 24);
    if (numericSerial != 0)
    {
        return std::to_string(numericSerial);
    }

    return "";
}

// Parse manufacture week and year from EDID bytes 16-17
// Returns "YYYY-WNN" (e.g. "2019-W23") or empty if unavailable
std::string ParseEdidManufactureDate(const std::vector<BYTE> &edid)
{
    if (edid.size() < 128)
        return "";

    BYTE week = edid[16];
    BYTE yearOffset = edid[17];
    int year = 1990 + yearOffset;

    if (year < 1990 || year > 2100)
        return "";

    std::string result = std::to_string(year);
    if (week >= 1 && week <= 53)
    {
        result += "-W" + (week < 10 ? std::string("0") : "") + std::to_string(week);
    }
    return result;
}

// Scan EDID extension blocks for a DisplayID Container ID (128-bit UUID)
// Returns hex string like "a1b2c3d4..." or empty if not found
std::string ParseEdidContainerId(const std::vector<BYTE> &edid)
{
    if (edid.size() < 128)
        return "";

    int extensionCount = edid[126];
    if (extensionCount == 0 || edid.size() < static_cast<size_t>(128 + extensionCount * 128))
    {
        return "";
    }

    // Scan each 128-byte extension block
    for (int ext = 0; ext < extensionCount; ext++)
    {
        size_t extBase = 128 + ext * 128;
        BYTE tag = edid[extBase];

        // 0x70 = DisplayID extension
        if (tag != 0x70)
            continue;

        // DisplayID structure: byte 0=version, byte 1=data length, byte 2=product type,
        // byte 3=extension count, then data blocks
        // Each data block: byte 0=tag, byte 1=revision, byte 2=payload length, then payload
        size_t dbStart = extBase + 5; // skip ext tag + DisplayID header (4 bytes)
        BYTE dataLen = edid[extBase + 2];
        size_t dbEnd = extBase + 5 + dataLen;
        if (dbEnd > extBase + 127)
            dbEnd = extBase + 127;

        size_t pos = dbStart;
        while (pos + 3 <= dbEnd)
        {
            BYTE dbTag = edid[pos];
            BYTE dbPayloadLen = edid[pos + 2];
            size_t payloadStart = pos + 3;

            // Tag 0x29 = Container ID (16 bytes UUID)
            if (dbTag == 0x29 && dbPayloadLen >= 16 && payloadStart + 16 <= edid.size())
            {
                bool allZero = true;
                for (int k = 0; k < 16; k++)
                {
                    if (edid[payloadStart + k] != 0)
                    {
                        allZero = false;
                        break;
                    }
                }
                if (!allZero)
                {
                    static const char hex[] = "0123456789abcdef";
                    std::string uuid;
                    uuid.reserve(32);
                    for (int k = 0; k < 16; k++)
                    {
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

} // anonymous namespace
