using System;
using System.Runtime.InteropServices;

namespace MegaSchoen.Avalonia;

static class WindowsPackagingVerifier
{
    public static string? VerifyCurrent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Native packaging verification is only supported on Windows.";
        }

        return Verify(
            RuntimeInformation.ProcessArchitecture,
            DisplayManager.Core.DisplayManager.GetRawDisplayJson);
    }

    public static string? Verify(Architecture architecture, Func<string> queryDisplays)
    {
        if (architecture != Architecture.X64)
        {
            return $"Expected an X64 process, but the application is running as {architecture}.";
        }

        var nativeResult = queryDisplays();
        // A negative native return still proves that the DLL and entry point
        // loaded; headless CI may have no queryable display topology. Only the
        // managed exception wrapper means the P/Invoke itself did not resolve.
        if (nativeResult.StartsWith("Error getting raw JSON:", StringComparison.OrdinalIgnoreCase))
        {
            return nativeResult;
        }

        return null;
    }
}
