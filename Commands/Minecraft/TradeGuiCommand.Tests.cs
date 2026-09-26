using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class TradeGuiCommandTests
{
    [TestCase("on", true)]
    [TestCase("off", false)]
    [TestCase(" ON ", true)]
    [TestCase("Off", false)]
    public async Task SendsTypedClientResponse(string arguments, bool enabled)
    {
        var socket = new Mock<IMinecraftSocket>();
        Response response = null;
        socket.Setup(s => s.Send(It.IsAny<Response>())).Callback<Response>(r => response = r);
        await new TradeGuiCommand().Execute(socket.Object, JsonConvert.SerializeObject(arguments));
        Assert.That(response.type, Is.EqualTo("tradeGui"));
        Assert.That(JObject.Parse(response.data)["enabled"].Value<bool>(), Is.EqualTo(enabled));
        var wire = JObject.Parse(JsonConvert.SerializeObject(response));
        Assert.That(wire["data"].Type, Is.EqualTo(JTokenType.String));
    }

    [Test]
    public async Task StatusQueriesClientStateWithoutChangingIt()
    {
        var socket = new Mock<IMinecraftSocket>();
        Response response = null;
        socket.Setup(s => s.Send(It.IsAny<Response>())).Callback<Response>(r => response = r);
        await new TradeGuiCommand().Execute(socket.Object, JsonConvert.SerializeObject(""));
        Assert.That(JObject.Parse(response.data)["enabled"].Type, Is.EqualTo(JTokenType.Null));
    }

    [TestCase("invalid")]
    [TestCase("on extra")]
    public async Task InvalidArgumentsNeverChangeClientState(string arguments)
    {
        var socket = new Mock<IMinecraftSocket>();
        await new TradeGuiCommand().Execute(socket.Object, JsonConvert.SerializeObject(arguments));
        socket.Verify(s => s.Send(It.IsAny<Response>()), Times.Never);
        socket.Verify(s => s.SendMessage(It.Is<string>(m => m.Contains("/cofl tradegui <on/off>")), null, null), Times.Once);
    }

    [Test]
    public void RegistryAndCommandUpdateOwnCompletion()
    {
        Assert.That(MinecraftSocket.Commands["tradegui"], Is.TypeOf<TradeGuiCommand>());
        var commands = HelpCommand.BuildCommandList(new McCommand[] { new TradeGuiCommand(), new HelpCommand() });
        Assert.That(commands.Keys, Does.Contain("tradegui").And.Contain("tradegui on").And.Contain("tradegui off").And.Contain("help"));
        var response = Response.Create("commandUpdate", commands);
        response.type = "commandUpdate";
        var decoded = JsonConvert.DeserializeObject<Dictionary<string, string>>(response.data);
        Assert.That(decoded["tradegui off"], Is.EqualTo("Disable the trade overlay"));
    }
}
