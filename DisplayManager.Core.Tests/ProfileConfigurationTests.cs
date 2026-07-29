using System.Text.Json;
using DisplayManager.Core.Models;

namespace DisplayManager.Core.Tests;

[TestClass]
public sealed class ProfileConfigurationTests
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [TestMethod]
    public void SerializedConfigurationContainsOnlyPersistedProfiles()
    {
        var json = JsonSerializer.Serialize(new ProfileConfiguration(), JsonOptions);

        Assert.AreEqual("""{"profiles":[]}""", json);
    }

    [TestMethod]
    public void LegacyHypotheticalPropertiesAreIgnoredWhenLoading()
    {
        var json = """
            {
              "schemaVersion": "1.0",
              "profiles": [],
              "settings": {
                "defaultProfileId": null,
                "autoLoadOnStartup": true,
                "showTrayNotifications": true,
                "minimizeToTray": true,
                "startWithWindows": true
              }
            }
            """;

        var configuration = JsonSerializer.Deserialize<ProfileConfiguration>(json, JsonOptions);

        Assert.IsNotNull(configuration);
        Assert.IsEmpty(configuration.Profiles);
    }
}
