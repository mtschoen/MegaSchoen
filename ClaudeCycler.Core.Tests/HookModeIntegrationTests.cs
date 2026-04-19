using System.Diagnostics;

namespace ClaudeCycler.Core.Tests;

[TestClass]
public class HookModeIntegrationTests
{
    static string BridgeExePath =>
        Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "ClaudeHookBridge", "bin", "Debug", "net10.0-windows10.0.26100.0",
            "ClaudeHookBridge.exe");

    [TestMethod]
    public void StdinPermissionPrompt_UpsertsStateFile()
    {
        // Point at a temp state file by overriding LOCALAPPDATA for the child process
        var tempLocalAppData = Path.Combine(Path.GetTempPath(), $"megaschoen-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempLocalAppData);
        try
        {
            var payload = """
            {
              "hook_event_name": "Notification",
              "notification_type": "permission_prompt",
              "session_id": "integration-s1",
              "cwd": "C:\\foo",
              "message": "Claude needs your permission"
            }
            """;

            var startInfo = new ProcessStartInfo(BridgeExePath)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            startInfo.EnvironmentVariables["LOCALAPPDATA"] = tempLocalAppData;

            using var process = Process.Start(startInfo)!;
            process.StandardInput.Write(payload);
            process.StandardInput.Close();
            process.WaitForExit(5000);

            Assert.AreEqual(0, process.ExitCode);

            var stateFile = Path.Combine(tempLocalAppData, "MegaSchoen", "needy-sessions.json");
            Assert.IsTrue(File.Exists(stateFile), "state file was not created");
            var contents = File.ReadAllText(stateFile);
            StringAssert.Contains(contents, "integration-s1");
        }
        finally
        {
            if (Directory.Exists(tempLocalAppData)) Directory.Delete(tempLocalAppData, recursive: true);
        }
    }
}
