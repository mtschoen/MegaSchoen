using System.Diagnostics;
using System.Text.Json;
using Claude.Core.Models;

namespace Claude.Core.Tests;

[TestClass]
public class CliSmokeTests
{
    static string CliPath() =>
        TestBinaries.LocateExecutable("AgentSessionsCLI", TestBinaries.ExecutableName("AgentSessionsCLI"));

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

    [TestMethod]
    public void ListJson_WithPlanSession_EmitsMode()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"cli-mode-{Guid.NewGuid():N}");
        try
        {
            const string cwd = "/tmp/cli-plan-session";
            var store = new StateStore(tempDir);
            store.Upsert("plan-session", new SessionEntry
            {
                Cwd = cwd,
                NotifiedAt = DateTimeOffset.UtcNow,
                Reason = WaitingReason.Working,
                Mode = SessionMode.Plan
            });

            var psi = new ProcessStartInfo(CliPath(), "list --json")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            psi.Environment["MEGASCHOEN_STATE_DIR"] = tempDir;
            psi.Environment[EnvironmentProcessLocator.EnvironmentVariable] = """[{"cwd":"/tmp/cli-plan-session","count":1}]""";

            using var process = Process.Start(psi)!;
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30_000);

            Assert.AreEqual(0, process.ExitCode);
            using var doc = JsonDocument.Parse(stdout);
            Assert.HasCount(1, doc.RootElement.EnumerateArray());
            Assert.AreEqual("Plan", doc.RootElement[0].GetProperty("Mode").GetString());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
