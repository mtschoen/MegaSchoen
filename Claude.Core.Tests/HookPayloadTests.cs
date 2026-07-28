using System.Text.Json;
using Claude.Core.Models;

namespace Claude.Core.Tests;

[TestClass]
public class HookPayloadTests
{
    [TestMethod]
    public void Deserialize_ReadsPermissionMode()
    {
        var payload = JsonSerializer.Deserialize<HookPayload>(
            """{"session_id":"s1","permission_mode":"plan","hook_event_name":"UserPromptSubmit"}""");

        Assert.IsNotNull(payload);
        Assert.AreEqual("plan", payload.PermissionMode);
    }
}
