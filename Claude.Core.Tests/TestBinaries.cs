namespace Claude.Core.Tests;

// Shared locator for the sibling-project executables that the smoke / integration tests shell
// out to. The configuration is derived from THIS test assembly's own output path so the tests
// find executables built in the same configuration: CI builds Release, local MSBuild builds Debug.
static class TestBinaries
{
    const string TargetFramework = "net10.0-windows10.0.26100.0";

    internal static string Configuration { get; } = DeriveConfiguration();

    static string DeriveConfiguration()
    {
        // AppContext.BaseDirectory looks like ...\Claude.Core.Tests\bin\<Configuration>\<tfm>\
        var parts = AppContext.BaseDirectory
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        var binIndex = Array.LastIndexOf(parts, "bin");
        return binIndex >= 0 && binIndex + 1 < parts.Length ? parts[binIndex + 1] : "Debug";
    }

    // Walk up from the test output directory toward the repository root, probing for
    // <projectName>\bin\<Configuration>\<TargetFramework>\<executableFileName>.
    internal static string LocateExecutable(string projectName, string executableFileName)
    {
        var current = AppContext.BaseDirectory;
        for (var depth = 0; depth < 8 && current is not null; depth++)
        {
            var probe = Path.Combine(current, projectName, "bin", Configuration, TargetFramework, executableFileName);
            if (File.Exists(probe)) return probe;
            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException(
            $"{executableFileName} not found under any ancestor of '{AppContext.BaseDirectory}' " +
            $"(configuration '{Configuration}') — build {projectName} first.");
    }
}
