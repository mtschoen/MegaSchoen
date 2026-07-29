using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace MegaSchoen.Avalonia.Tests;

[TestClass]
public sealed class WindowsPackagingProjectTests
{
    [TestMethod]
    public void AvaloniaProjectPinsWindowsBuildToX64AndCopiesNativeDll()
    {
        var projectPath = Path.Combine(RepositoryRoot(), "MegaSchoen.Avalonia", "MegaSchoen.Avalonia.csproj");
        var project = XDocument.Load(projectPath);
        Assert.AreEqual("net10.0", project.Descendants("TargetFramework").Single().Value);
        Assert.AreEqual("win-x64", project.Descendants("RuntimeIdentifier").Single().Value);
        Assert.AreEqual("x64", project.Descendants("PlatformTarget").Single().Value);

        var copyTarget = project.Descendants("Target")
            .Single(element => string.Equals(
                element.Attribute("Name")?.Value,
                "CopyNativeDllToOutput",
                StringComparison.Ordinal));
        StringAssert.Contains(copyTarget.ToString(), "DisplayManagerNative.dll");
        StringAssert.Contains(copyTarget.ToString(), "<Error");
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
