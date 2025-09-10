#pragma once

struct DisplayInfo {
    wchar_t DeviceName[32];
    wchar_t DeviceString[128];
    int Width;
    int Height;
    int PositionX;
    int PositionY;
    int Frequency;
    int BitsPerPixel;
    int IsActive;
    int IsPrimary;
};