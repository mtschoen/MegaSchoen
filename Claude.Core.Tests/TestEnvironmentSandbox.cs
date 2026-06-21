using System.Runtime.CompilerServices;

namespace Claude.Core.Tests;

// Redirects all Paths-derived file I/O (the sharded state store,
// hook-bridge.log, hook-capture.ndjson) into a throwaway temp LOCALAPPDATA for
// the whole test run, so the suite never pollutes the developer's real
// %LOCALAPPDATA%\MegaSchoen - most visibly hook-bridge.log, which Logger.Log
// appends to from StateStore/HookDispatcher code paths the tests exercise.
//
// A module initializer (not [AssemblyInitialize]) is used deliberately: Paths
// exposes its directories as static get-only properties whose initializers
// freeze on first access, so the LOCALAPPDATA override must land before any
// Paths member is touched. Module initializers run at assembly load, strictly
// earlier than any test hook, removing the static-init ordering risk.
[TestClass]
public class TestEnvironmentSandbox
{
    static string? _tempLocalAppData;

    [ModuleInitializer]
    internal static void RedirectLocalAppData()
    {
        _tempLocalAppData = Path.Combine(Path.GetTempPath(), $"megaschoen-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempLocalAppData);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", _tempLocalAppData);
    }

    [AssemblyCleanup]
    public static void Cleanup()
    {
        if (_tempLocalAppData is null)
        {
            return;
        }

        try
        {
            Directory.Delete(_tempLocalAppData, recursive: true);
        }
        catch
        {
            /* Best-effort cleanup: a leftover temp dir under %TEMP% is harmless
               and gets reclaimed by the OS, so swallow any IO race here. */
        }
    }
}
