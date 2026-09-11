using Coflnet.Sky.Commands.MC;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Models;

public class InfoDisplayTests
{
    [Test]
    public void SerializesClientProtocolEnvelopeAndClickableLines()
    {
        var response = Response.Create("infoDisplay", new InfoDisplay
        {
            Id = 1,
            Title = "§6Test",
            Lines = new[] { new ChatPart("§eClear", "/cofl test display clear", "Clear panel") },
            Ttl = 60
        });
        var envelope = JObject.Parse(JsonConvert.SerializeObject(response));
        Assert.That((string)envelope["type"], Is.EqualTo("infoDisplay"));
        Assert.That(envelope["data"].Type, Is.EqualTo(JTokenType.String));
        var payload = JObject.Parse((string)envelope["data"]);
        Assert.That((int)payload["id"], Is.EqualTo(1));
        Assert.That((string)payload["title"], Is.EqualTo("§6Test"));
        Assert.That((int)payload["ttl"], Is.EqualTo(60));
        Assert.That((bool)payload["clear"], Is.False);
        Assert.That((string)payload["lines"][0]["text"], Is.EqualTo("§eClear"));
        Assert.That((string)payload["lines"][0]["onClick"], Is.EqualTo("/cofl test display clear"));
        Assert.That((string)payload["lines"][0]["hover"], Is.EqualTo("Clear panel"));
    }

    [Test]
    public void SerializesClearWithoutContent()
    {
        var payload = JObject.Parse(Response.Create("infoDisplay", new InfoDisplay { Id = 1, Clear = true }).data);
        Assert.That((int)payload["id"], Is.EqualTo(1));
        Assert.That((bool)payload["clear"], Is.True);
    }
}
