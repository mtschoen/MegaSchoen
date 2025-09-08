#pragma once

#define DISPLAYMANAGER_API __declspec(dllexport)

extern "C" {
    // Simple function to switch to internal display only (disable secondary monitors)
    DISPLAYMANAGER_API int SwitchToInternalDisplay();
}