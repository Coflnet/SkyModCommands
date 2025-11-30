using NUnit.Framework;
using System.Linq;

namespace Coflnet.Sky.ModCommands.Services.Vps;

public class TpmConfigDocParserTests
{
    [Test]
    public void ParseDocumentation_SimpleProperty_ExtractsComment()
    {
        var json = """
            {
            //This is a comment
            "testProperty": "value"
            }
            """;

        var result = TpmConfigDocParser.ParseDocumentation(json);

        Assert.That(result.ContainsKey("testProperty"), Is.True);
        Assert.That(result["testProperty"], Is.EqualTo("This is a comment"));
    }

    [Test]
    public void ParseDocumentation_MultiLineComment_JoinsComments()
    {
        var json = """
            {
            //First line of comment
            //Second line of comment
            "testProperty": "value"
            }
            """;

        var result = TpmConfigDocParser.ParseDocumentation(json);

        Assert.That(result["testProperty"], Is.EqualTo("First line of comment Second line of comment"));
    }

    [Test]
    public void ParseDocumentation_PropertyWithNoComment_NotIncluded()
    {
        var json = """
            {
            "noCommentProperty": "value"
            }
            """;

        var result = TpmConfigDocParser.ParseDocumentation(json);

        Assert.That(result.ContainsKey("noCommentProperty"), Is.False);
    }

    [Test]
    public void ParseDocumentation_RealTpmConfig_ExtractsIgnsComment()
    {
        var result = TpmConfigDocParser.ParseDocumentation(TPM.PlusDefault);

        Assert.That(result.ContainsKey("igns"), Is.True);
        Assert.That(result["igns"], Does.Contain("minecraft IGN"));
    }

    [Test]
    public void ParseDocumentation_RealTpmConfig_ExtractsUseCookieComment()
    {
        var result = TpmConfigDocParser.ParseDocumentation(TPM.PlusDefault);

        Assert.That(result.ContainsKey("useCookie"), Is.True);
        Assert.That(result["useCookie"], Does.Contain("Required to use relist"));
    }

    [Test]
    public void ParseDocumentationWithPrefixes_NestedSkipProperty_HasCorrectPrefix()
    {
        var result = TpmConfigDocParser.ParseDocumentationWithPrefixes(TPM.PlusDefault);

        Assert.That(result.ContainsKey("skipminProfit"), Is.True);
        Assert.That(result["skipminProfit"], Does.Contain("profit over"));
    }

    [Test]
    public void ParseDocumentationWithPrefixes_NestedNoRelistProperty_HasCorrectPrefix()
    {
        var result = TpmConfigDocParser.ParseDocumentationWithPrefixes(TPM.PlusDefault);

        Assert.That(result.ContainsKey("norelistprofitOver"), Is.True);
        Assert.That(result["norelistprofitOver"], Does.Contain("profit"));
    }

    [Test]
    public void ParseDocumentationWithPrefixes_TopLevelProperty_NoPrefix()
    {
        var result = TpmConfigDocParser.ParseDocumentationWithPrefixes(TPM.PlusDefault);

        Assert.That(result.ContainsKey("relist"), Is.True);
        Assert.That(result["relist"], Does.Contain("Automatically list auctions"));
    }

    [Test]
    public void ParseDocumentationWithPrefixes_ContainsAllExpectedKeys()
    {
        var result = TpmConfigDocParser.ParseDocumentationWithPrefixes(TPM.PlusDefault);

        // Top level keys
        Assert.That(result.ContainsKey("igns"), Is.True, "Should contain 'igns'");
        Assert.That(result.ContainsKey("discordID"), Is.True, "Should contain 'discordID'");
        Assert.That(result.ContainsKey("useCookie"), Is.True, "Should contain 'useCookie'");
        Assert.That(result.ContainsKey("autoCookie"), Is.True, "Should contain 'autoCookie'");
        Assert.That(result.ContainsKey("delay"), Is.True, "Should contain 'delay'");
        Assert.That(result.ContainsKey("waittime"), Is.True, "Should contain 'waittime'");

        // Skip nested keys
        Assert.That(result.ContainsKey("skipalways"), Is.True, "Should contain 'skipalways'");
        Assert.That(result.ContainsKey("skipminProfit"), Is.True, "Should contain 'skipminProfit'");

        // DoNotRelist nested keys
        Assert.That(result.ContainsKey("norelistskinned"), Is.True, "Should contain 'norelistskinned'");
        Assert.That(result.ContainsKey("norelisttags"), Is.True, "Should contain 'norelisttags'");
    }

    [Test]
    public void GetTpmPlusDocumentation_ReturnsNonEmptyDictionary()
    {
        var result = TpmConfigDocParser.GetTpmPlusDocumentation();

        Assert.That(result.Count, Is.GreaterThan(20), "Should have many documented settings");
    }

    [Test]
    public void ParseDocumentation_CaseInsensitiveKeyLookup()
    {
        var json = """
            {
            //Test comment
            "TestProperty": "value"
            }
            """;

        var result = TpmConfigDocParser.ParseDocumentation(json);

        Assert.That(result.ContainsKey("testproperty"), Is.True);
        Assert.That(result.ContainsKey("TESTPROPERTY"), Is.True);
        Assert.That(result.ContainsKey("TestProperty"), Is.True);
    }

    [Test]
    public void ParseDocumentationWithPrefixes_WebhookFormat_HasPlaceholderDocumentation()
    {
        var result = TpmConfigDocParser.ParseDocumentationWithPrefixes(TPM.PlusDefault);

        Assert.That(result.ContainsKey("webhookFormat"), Is.True);
        var doc = result["webhookFormat"];
        // Should contain info about placeholders {0}, {1}, etc.
        Assert.That(doc, Does.Contain("{0}"));
        Assert.That(doc, Does.Contain("item"));
        Assert.That(doc, Does.Contain("profit"));
    }

    [Test]
    public void ParseDocumentationWithPrefixes_AutoCookie_HasTimeFormatDocumentation()
    {
        var result = TpmConfigDocParser.ParseDocumentationWithPrefixes(TPM.PlusDefault);

        Assert.That(result.ContainsKey("autoCookie"), Is.True);
        var doc = result["autoCookie"];
        // Should mention time format options
        Assert.That(doc, Does.Contain("y").Or.Contain("d").Or.Contain("h"));
    }
}
