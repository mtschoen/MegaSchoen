using System.Diagnostics;
using System.Text.Json;

namespace Claude.Core.Tests;

[TestClass]
public class CliSmokeTests
{
    static string CliPath() => TestBinaries.LocateExecutable("ClaudeSessionsCLI", "ClaudeSessionsCLI.exe");

    [TestMethod]
    public void ListJson_ProducesParseableJsonAndExitsZero()
    {
        var psi = new ProcessStartInfo(CliPath(), "list --json")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(30_000);
        Assert.AreEqual(0, process.ExitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.AreEqual(JsonValueKind.Array, doc.RootElement.ValueKind);
    }

    [TestMethod]
    public void FocusWithoutArguments_ExitsWithFailureCode()
    {
        var psi = new ProcessStartInfo(CliPath(), "focus")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var process = Process.Start(psi)!;
        process.WaitForExit(5_000);
        Assert.AreNotEqual(0, process.ExitCode);
    }
}
