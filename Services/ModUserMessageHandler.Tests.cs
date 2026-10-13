#if DEBUG
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class ModUserMessageHandlerTests
{
    private Mock<IMinecraftSocket> socket;
    private readonly List<Response> sent = new();
    private readonly List<string> executed = new();

    [SetUp]
    public void Setup()
    {
        sent.Clear();
        executed.Clear();
        socket = new Mock<IMinecraftSocket>();
        socket.SetupGet(s => s.Version).Returns("2.0.1");
        socket.SetupGet(s => s.SessionInfo).Returns(new SessionInfo { McUuid = "abc" });
        socket.Setup(s => s.Send(It.IsAny<Response>())).Callback<Response>(sent.Add);
        socket.Setup(s => s.ExecuteCommand(It.IsAny<string>())).Callback<string>(executed.Add);
    }

    private static ModUserChannel.UserMessage Display() => new()
    {
        Kind = "display",
        Display = JObject.Parse("{\"id\":3,\"title\":\"t\",\"lines\":[{\"text\":\"a\",\"onClick\":\"/cofl bzorder 32 Enchanted Diamond\"}]}")
    };

    [Test]
    public void ExecuteRunsCommand()
    {
        ModUserMessageHandler.HandleUserMessage(socket.Object, new() { Kind = "execute", Command = "/bz wheat" }).Should().BeTrue();
        executed.Should().Equal("/bz wheat");
    }

    [Test]
    public void ClosedSocketUnsubscribes()
    {
        socket.SetupGet(s => s.IsClosed).Returns(true);
        ModUserMessageHandler.HandleUserMessage(socket.Object, new() { Kind = "execute", Command = "/bz wheat" }).Should().BeFalse();
        executed.Should().BeEmpty();
    }

    [Test]
    public void UuidMismatchIgnored()
    {
        ModUserMessageHandler.HandleUserMessage(socket.Object, new() { Kind = "execute", Command = "/bz wheat", MinecraftUuid = "other" }).Should().BeTrue();
        executed.Should().BeEmpty();
    }

    [Test]
    public void OverLongExecuteIgnored()
    {
        ModUserMessageHandler.HandleUserMessage(socket.Object, new() { Kind = "execute", Command = "/" + new string('a', 120) });
        executed.Should().BeEmpty();
    }

    [Test]
    public void DisplaySkippedOnUnsupportedVersion()
    {
        socket.SetupGet(s => s.Version).Returns("1.5.0");
        ModUserMessageHandler.HandleUserMessage(socket.Object, Display());
        sent.Should().BeEmpty();
    }

    [Test]
    public void DisplaySentUnchangedOnNewerVersion()
    {
        ModUserMessageHandler.HandleUserMessage(socket.Object, Display());
        var d = JsonConvert.DeserializeObject<InfoDisplay>(sent.Single().data);
        d.Lines[0].onClick.Should().Be("/cofl bzorder 32 Enchanted Diamond");
    }
}
#endif
