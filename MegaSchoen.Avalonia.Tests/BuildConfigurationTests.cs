using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace MegaSchoen.Avalonia.Tests;

[TestClass]
public sealed class BuildConfigurationTests
{
    [TestMethod]
    public void NativeDisplayProjectRequiresFullMsBuildOnWindows()
    {
        var projectPath = Path.Combine(
            RepositoryRoot(), "DisplayManager.Core", "DisplayManager.Core.csproj");
        var project = XDocument.Load(projectPath);
        var nativeReference = project
            .Descendants("ProjectReference")
            .Single(reference => reference.Attribute("Include")?.Value.EndsWith(
                "DisplayManagerNative.vcxproj",
                StringComparison.Ordinal) is true);
        var condition = nativeReference.Parent?.Attribute("Condition")?.Value;

        Assert.AreEqual(
            "$([MSBuild]::IsOSPlatform('Windows')) and '$(MSBuildRuntimeType)' == 'Full'",
            condition);
    }

    static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MegaSchoen.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the MegaSchoen repository root.");
    }
}
