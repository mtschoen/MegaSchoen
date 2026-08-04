using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using DisplayManager.Core.Models;

namespace DisplayManager.Core.Tests;

[TestClass]
public class DisplayManagerCliCooldownTests
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [TestMethod]
    public async Task Load_WithPreloadedSharedCooldown_DefersBeforeNativeApply()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DisplayManagerCLI requires the Visual Studio native build on Windows.");
        }

        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, $"cli-cooldown-{Guid.NewGuid():N}");
        Directory.CreateDirectory(fixtureRoot);

        try
        {
            var profile = new SavedDisplayProfile
            {
                Name = "Cooldown Fixture",
                Displays = [new SavedDisplayConfig { MonitorName = "Fixture Monitor" }]
            };
            var configuration = new ProfileConfiguration { Profiles = [profile] };
            await File.WriteAllTextAsync(
                Path.Combine(fixtureRoot, "configs.json"),
                JsonSerializer.Serialize(configuration, JsonOptions));
            await File.WriteAllTextAsync(
                Path.Combine(fixtureRoot, "display-resume.timestamp"),
                TimeProvider.System.GetTimestamp().ToString(CultureInfo.InvariantCulture));

            var isolatedCliDirectory = Path.Combine(fixtureRoot, "cli");
            var isolatedCliPath = CopyCliWithNativeLibrarySentinel(isolatedCliDirectory);

            using var process = StartCliProcess(
                isolatedCliPath,
                "load",
                profile.Name,
                fixtureRoot);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            string standardOutput;
            string standardError;
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                standardOutput = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                standardError = await process.StandardError.ReadToEndAsync(timeout.Token);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }

            Assert.AreEqual(0, process.ExitCode, standardError);
            Assert.Contains($"Applying profile '{profile.Name}'", standardOutput);
            Assert.Contains("Failed:", standardOutput);
            Assert.Contains("display apply deferred", standardOutput.ToLowerInvariant());
            Assert.Contains("shared resume timestamp is active", standardError);
            Assert.DoesNotContain("DisplayManagerNative", standardOutput + standardError);
        }
        finally
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }

    static Process StartCliProcess(
        string executablePath,
        string command,
        string profileName,
        string fixtureRoot)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executablePath)
                ?? throw new InvalidOperationException("Could not determine the isolated CLI directory.")
        };
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add(profileName);
        startInfo.Environment[DisplayProfileDataPaths.OverrideEnvironmentVariable] = fixtureRoot;
        startInfo.Environment["PATH"] = startInfo.WorkingDirectory;

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("DisplayManagerCLI did not start.");
    }

    static string CopyCliWithNativeLibrarySentinel(string destinationDirectory)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new InvalidOperationException("Could not determine the test build configuration.");
        var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        var executableName = OperatingSystem.IsWindows() ? "DisplayManagerCLI.exe" : "DisplayManagerCLI";
        var sourceDirectory = Path.Combine(
            repositoryRoot,
            "DisplayManagerCLI",
            "bin",
            configuration,
            "net10.0");
        var executablePath = Path.Combine(sourceDirectory, executableName);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException(
                "Build DisplayManagerCLI in the same configuration before running this end-to-end test.",
                executablePath);
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory))
        {
            if (string.Equals(
                Path.GetFileName(sourcePath),
                "DisplayManagerNative.dll",
                StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            File.Copy(sourcePath, Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)));
        }

        var isolatedExecutablePath = Path.Combine(destinationDirectory, executableName);
        var nativeLibraryPath = Path.Combine(destinationDirectory, "DisplayManagerNative.dll");
        Assert.IsTrue(File.Exists(isolatedExecutablePath));
        Assert.IsFalse(File.Exists(nativeLibraryPath));
        File.WriteAllText(nativeLibraryPath, "End-to-end test sentinel: intentionally not a native library.");
        return isolatedExecutablePath;
    }
}
