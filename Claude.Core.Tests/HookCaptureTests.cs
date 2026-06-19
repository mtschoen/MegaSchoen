using Claude.Core;

namespace Claude.Core.Tests;

[TestClass]
public class HookCaptureTests
{
    const string EnableVariable = "MEGASCHOEN_HOOK_CAPTURE";

    string? _originalValue;
    readonly List<string> _tempPaths = new();

    [TestInitialize]
    public void Setup() => _originalValue = Environment.GetEnvironmentVariable(EnableVariable);

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(EnableVariable, _originalValue);
        foreach (var path in _tempPaths)
        {
            if (File.Exists(path)) File.Delete(path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && directory != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                && Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
    }

    string TempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hookcap-{Guid.NewGuid():N}.ndjson");
        _tempPaths.Add(path);
        return path;
    }

    [TestMethod]
    public void IsEnabled_WhenVariableUnset_IsFalse()
    {
        Environment.SetEnvironmentVariable(EnableVariable, null);
        Assert.IsFalse(HookCapture.IsEnabled);
    }

    [TestMethod]
    public void IsEnabled_WhenVariableWhitespace_IsFalse()
    {
        Environment.SetEnvironmentVariable(EnableVariable, "   ");
        Assert.IsFalse(HookCapture.IsEnabled);
    }

    [TestMethod]
    [DataRow("1")]
    [DataRow("true")]
    [DataRow("TRUE")]
    [DataRow("on")]
    public void IsEnabled_WhenVariableTruthy_IsTrue(string value)
    {
        Environment.SetEnvironmentVariable(EnableVariable, value);
        Assert.IsTrue(HookCapture.IsEnabled);
    }

    [TestMethod]
    public void IsEnabled_WhenVariableIsExplicitPath_IsTrue()
    {
        Environment.SetEnvironmentVariable(EnableVariable, TempFile());
        Assert.IsTrue(HookCapture.IsEnabled);
    }

    [TestMethod]
    public void Capture_WhenDisabled_DoesNotThrowOrWrite()
    {
        Environment.SetEnvironmentVariable(EnableVariable, null);
        var target = TempFile();
        HookCapture.Capture("""{"hook":"x"}""");
        Assert.IsFalse(File.Exists(target));
    }

    [TestMethod]
    public void Capture_ValidJson_EmbedsPayloadAsNestedObject()
    {
        var target = TempFile();
        Environment.SetEnvironmentVariable(EnableVariable, target);

        HookCapture.Capture("""{"hook_event_name":"Stop","session_id":"abc"}""");

        var lines = File.ReadAllLines(target);
        Assert.HasCount(1, lines);
        using var doc = System.Text.Json.JsonDocument.Parse(lines[0]);
        var root = doc.RootElement;
        Assert.IsTrue(root.TryGetProperty("capturedAtUtc", out _));
        Assert.IsTrue(root.TryGetProperty("payload", out var payload));
        Assert.AreEqual("Stop", payload.GetProperty("hook_event_name").GetString());
        Assert.IsFalse(root.TryGetProperty("rawText", out _));
    }

    [TestMethod]
    public void Capture_InvalidJson_FallsBackToRawText()
    {
        var target = TempFile();
        Environment.SetEnvironmentVariable(EnableVariable, target);

        HookCapture.Capture("this is not json {");

        var lines = File.ReadAllLines(target);
        Assert.HasCount(1, lines);
        using var doc = System.Text.Json.JsonDocument.Parse(lines[0]);
        var root = doc.RootElement;
        Assert.AreEqual("this is not json {", root.GetProperty("rawText").GetString());
        Assert.IsFalse(root.TryGetProperty("payload", out _));
    }

    [TestMethod]
    public void Capture_CreatesMissingParentDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hookcap-{Guid.NewGuid():N}");
        var target = Path.Combine(directory, "nested", "out.ndjson");
        _tempPaths.Add(target);
        Environment.SetEnvironmentVariable(EnableVariable, target);

        HookCapture.Capture("""{"a":1}""");

        Assert.IsTrue(File.Exists(target));
        Directory.Delete(directory, recursive: true);
    }

    [TestMethod]
    public void Capture_AppendsOneLinePerCall()
    {
        var target = TempFile();
        Environment.SetEnvironmentVariable(EnableVariable, target);

        HookCapture.Capture("""{"n":1}""");
        HookCapture.Capture("""{"n":2}""");

        Assert.HasCount(2, File.ReadAllLines(target));
    }
}
