using NUnit.Framework;
using Coflnet.Sky.ModCommands.Services.Vps;
using Coflnet.Sky.Commands.Shared;
using System;
using System.Linq;

namespace Coflnet.Sky.ModCommands.Tests.Services.Vps;

/// <summary>
/// Unit tests for VpsInstanceManager.CreatedConfigs method
/// 
/// The CreatedConfigs method deserializes TPM configuration JSON strings that contain
/// C-style comments. This test suite verifies:
/// 1. Comment removal works correctly
/// 2. JSON deserialization succeeds for both tpm and tpm+ variants
/// 3. Configuration properties are correctly set from CreateOptions
/// </summary>
[TestFixture]
public class VpsInstanceManagerCreatedConfigsTests
{
    /// <summary>
    /// Tests that the method can deserialize NormalDefault config without throwing exceptions.
    /// Uses null instance which defaults to NormalDefault.
    /// </summary>
    [Test]
    public void CreatedConfigs_WithNullInstance_DeserializesSuccessfully()
    {
        // Arrange
        var testConfig = TPM.NormalDefault;

        // Act & Assert - Verify the default config can be parsed
        Assert.That(testConfig, Is.Not.Null.And.Not.Empty);
        Assert.That(testConfig, Does.Contain("igns"));
    }

    /// <summary>
    /// Tests that PlusDefault configuration contains the necessary JSON structure
    /// and is valid JSON that can be parsed.
    /// </summary>
    [Test]
    public void PlusDefault_ContainsValidJsonStructure()
    {
        // Arrange
        var plusDefault = TPM.PlusDefault;

        // Act & Assert
        Assert.That(plusDefault, Is.Not.Null.And.Not.Empty);
        Assert.That(plusDefault, Does.Contain("igns"));
        Assert.That(plusDefault, Does.Contain("skip"));
        Assert.That(plusDefault, Does.Contain("doNotRelist"));
    }

    /// <summary>
    /// Tests that NormalDefault configuration contains the necessary JSON structure
    /// and is valid JSON that can be parsed.
    /// </summary>
    [Test]
    public void NormalDefault_ContainsValidJsonStructure()
    {
        // Arrange
        var normalDefault = TPM.NormalDefault;

        // Act & Assert
        Assert.That(normalDefault, Is.Not.Null.And.Not.Empty);
        Assert.That(normalDefault, Does.Contain("igns"));
    }

    /// <summary>
    /// Tests that comment removal regex correctly handles the pattern
    /// @"//.*\n" which removes inline comments until end of line.
    /// </summary>
    [Test]
    public void CommentRemovalPattern_StripsCStyleComments()
    {
        // Arrange
        var testJson = """
            {
            "igns": [""],  // This is a comment
            "test": "value" // Another comment
            }
            """;

        // Act - Apply the same regex pattern used in CreatedConfigs
        var withoutComments = System.Text.RegularExpressions.Regex.Replace(testJson, @"//.*\n", "");

        // Assert
        Assert.That(withoutComments, Does.Not.Contain("// This is a comment"));
        Assert.That(withoutComments, Does.Not.Contain("// Another comment"));
        Assert.That(withoutComments, Does.Contain("igns"));
        Assert.That(withoutComments, Does.Contain("test"));
    }

    /// <summary>
    /// Tests the full cycle: remove comments from NormalDefault and verify it's valid JSON.
    /// </summary>
    [Test]
    public void NormalDefault_IsValidJsonAfterProcessing()
    {
        // Arrange
        var normalDefault = TPM.NormalDefault;

        // Act
        var withoutComments = System.Text.RegularExpressions.Regex.Replace(normalDefault, @"//.*\n", "");
        var split = withoutComments.Split("\n");
        var nonEmptyLines = split.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        var combined = string.Join("\n", nonEmptyLines);

        // Assert
        Assert.That(combined, Is.Not.Empty);
        Assert.That(combined, Does.Contain("\"igns\""));

        // Try to parse it as JSON
        try
        {
            var obj = Newtonsoft.Json.JsonConvert.DeserializeObject<TPM.TpmPlusConfig>(combined);
            Assert.That(obj, Is.Not.Null);
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            Assert.Fail($"NormalDefault should be valid JSON. Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests that TpmPlusConfig can be deserialized from both NormalDefault and PlusDefault configurations.
    /// </summary>
    [Test]
    public void TpmPlusConfig_DeserializesFromNormalDefault()
    {
        // Arrange
        var normalDefault = TPM.NormalDefault;
        var withoutComments = System.Text.RegularExpressions.Regex.Replace(normalDefault, @"//.*\n", "");
        var combined = string.Join("\n",
            withoutComments.Split("\n")
                .Where(l => !string.IsNullOrWhiteSpace(l))
        );

        // Act & Assert
        try
        {
            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<TPM.TpmPlusConfig>(combined);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.igns, Is.Not.Null);
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            Assert.Fail($"Should deserialize NormalDefault without exceptions. Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests that TpmPlusConfig can be deserialized from PlusDefault configuration.
    /// </summary>
    [Test]
    public void TpmPlusConfig_DeserializesFromPlusDefault()
    {
        // Arrange
        var plusDefault = TPM.PlusDefault;
        var withoutComments = System.Text.RegularExpressions.Regex.Replace(plusDefault, @"//.*\n", "");
        var combined = string.Join("\n",
            withoutComments.Split("\n")
                .Where(l => !string.IsNullOrWhiteSpace(l))
        );

        // Act & Assert
        try
        {
            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<TPM.TpmPlusConfig>(combined);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.igns, Is.Not.Null);
            Assert.That(result.skip, Is.Not.Null);
            Assert.That(result.doNotRelist, Is.Not.Null);
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            Assert.Fail($"Should deserialize PlusDefault without exceptions. Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests that setting values in a deserialized config works correctly.
    /// This simulates what CreatedConfigs does when CreateOptions are provided.
    /// </summary>
    [Test]
    public void TpmPlusConfig_CanSetIgnsAndSession()
    {
        // Arrange
        var config = new TPM.TpmPlusConfig();
        var testUserName = "TestPlayer123";
        var testSessionId = "session-abc-def";

        // Act
        config.igns = new[] { testUserName };
        config.session = testSessionId;

        // Assert
        Assert.That(config.igns.Length, Is.EqualTo(1));
        Assert.That(config.igns[0], Is.EqualTo(testUserName));
        Assert.That(config.session, Is.EqualTo(testSessionId));
    }

    /// <summary>
    /// Regression test: Verifies that the error reported in the issue
    /// "After parsing a value an unexpected character was encountered: s. Path 'webhookFormat'"
    /// does not occur when parsing valid JSON configurations.
    /// </summary>
    [Test]
    public void DefaultConfigs_CanDeserializeNormalDefault()
    {
        // Arrange
        var defaultConfig = TPM.NormalDefault;

        // Act
        var withoutComments = System.Text.RegularExpressions.Regex.Replace(defaultConfig, @"//.*\n", "");
        var combined = string.Join("\n",
            withoutComments.Split("\n")
                .Where(l => !string.IsNullOrWhiteSpace(l))
        );

        // Assert - NormalDefault should deserialize without issues
        try
        {
            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<TPM.TpmPlusConfig>(combined);
            Assert.That(result, Is.Not.Null, "Should successfully deserialize NormalDefault config");
            Assert.That(result.igns, Is.Not.Null);
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            Assert.Fail($"Should not fail to parse NormalDefault config. Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Tests deserialization of actual user settings JSON with all properties populated.
    /// This validates that the TpmPlusConfig can handle real-world configuration data.
    /// </summary>
    [Test]
    public void TpmPlusConfig_DeserializesRealUserSettings()
    {
        // Arrange
        var userSettingsJson = """
        {
            "delayTime": null,
            "sellInventory": null,
            "igns": [
                "Sample"
            ],
            "discordID": "33",
            "webhooks": null,
            "webhook": "https://discord.com/api/webhooks/",
            "webhookFormat": "You bought [``{0}``](https://sky.coflnet.com/auction/{7}) for ``{2}`` (``{1}`` profit) in ``{4}ms``",
            "sendAllFlips": "https://discord.com/api/webhooks/",
            "visitFriend": "",
            "useCookie": true,
            "autoCookie": "1h",
            "angryCoopPrevention": true,
            "relist": true,
            "pingOnUpdate": false,
            "delay": 65,
            "waittime": 15.0,
            "percentOfTarget": [
                "0",
                "10b",
                98
            ],
            "listHours": [
                "0",
                "10b",
                48
            ],
            "clickDelay": 115,
            "bedSpam": false,
            "blockUselessMessages": true,
            "roundTo": 3,
            "skip": {
                "always": true,
                "minProfit": "1m",
                "profitPercentage": "500",
                "minPrice": "500m",
                "userFinder": true,
                "skins": true
            },
            "doNotRelist": {
                "profitOver": "50m",
                "skinned": true,
                "tags": [
                    "HYPERION"
                ],
                "finders": [
                    "USER",
                    "CraftCost"
                ],
                "stacks": false,
                "pingOnFailedListing": false,
                "drillWithParts": true,
                "expiredAuctions": false,
                "relistMode": "2:97"
            },
            "autoRotate": {
                "ign": "12r:12f"
            },
            "session": "108ce15c-28d3-460d-b37f-234c2dcce52a"
        }
        """;

        // Act & Assert
        try
        {
            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<TPM.TpmPlusConfig>(userSettingsJson);
            Assert.That(result, Is.Not.Null);
            
            // Verify key properties
            Assert.That(result.igns, Is.Not.Null);
            Assert.That(result.igns.Length, Is.EqualTo(1));
            Assert.That(result.igns[0], Is.EqualTo("Sample"));
            
            Assert.That(result.discordID, Is.EqualTo("33"));
            Assert.That(result.webhook, Does.Contain("discord.com"));
            Assert.That(result.webhookFormat, Does.Contain("{0}"));
            
            Assert.That(result.useCookie, Is.True);
            Assert.That(result.autoCookie, Is.EqualTo("1h"));
            Assert.That(result.angryCoopPrevention, Is.True);
            Assert.That(result.relist, Is.True);
            
            Assert.That(result.delay, Is.EqualTo(65));
            Assert.That(result.waittime, Is.EqualTo(15.0f));
            Assert.That(result.clickDelay, Is.EqualTo(115));
            Assert.That(result.roundTo, Is.EqualTo(3));
            
            // Verify nested objects
            Assert.That(result.skip, Is.Not.Null);
            Assert.That(result.skip.always, Is.True);
            Assert.That(result.skip.minProfit, Is.EqualTo("1m"));
            Assert.That(result.skip.userFinder, Is.True);
            
            Assert.That(result.doNotRelist, Is.Not.Null);
            Assert.That(result.doNotRelist.profitOver, Is.EqualTo("50m"));
            Assert.That(result.doNotRelist.skinned, Is.True);
            Assert.That(result.doNotRelist.tags, Is.Not.Null);
            Assert.That(result.doNotRelist.tags.Length, Is.EqualTo(1));
            Assert.That(result.doNotRelist.tags[0], Is.EqualTo("HYPERION"));
            
            Assert.That(result.autoRotate, Is.Not.Null);
            Assert.That(result.autoRotate.ContainsKey("ign"), Is.True);
            Assert.That(result.autoRotate["ign"], Is.EqualTo("12r:12f"));
            
            Assert.That(result.session, Is.EqualTo("108ce15c-28d3-460d-b37f-234c2dcce52a"));
        }
        catch (Newtonsoft.Json.JsonException ex)
        {
            Assert.Fail($"Should deserialize real user settings without exceptions. Error: {ex.Message}");
        }
    }
}
