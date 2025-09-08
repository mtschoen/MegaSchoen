#include "DisplayManagerNative.h"
#include <windows.h>
#include <vector>

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
    
    return (result == ERROR_SUCCESS) ? 0 : (int)result;
}