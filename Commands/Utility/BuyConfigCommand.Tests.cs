using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class BuyConfigCommandTests
{
    private const string OwnerUuid = "e7246661de77474f94627fabf9880f60";

    [Test]
    public async Task NumericSellerIdFromBuyLinksIsShownAsTheMinecraftName()
    {
        var socket = new Mock<IMinecraftSocket>();
        socket.Setup(s => s.GetPlayerName(OwnerUuid)).ReturnsAsync("Ekwav");

        var name = await BuyConfigCommand.ResolveSellerName(
            socket.Object, "7106", OwnerUuid);

        Assert.That(name, Is.EqualTo("Ekwav"));
    }

    [Test]
    public async Task TypedMinecraftNameIsKeptWithoutLookup()
    {
        var socket = new Mock<IMinecraftSocket>(MockBehavior.Strict);

        var name = await BuyConfigCommand.ResolveSellerName(
            socket.Object, "Ekwav", OwnerUuid);

        Assert.That(name, Is.EqualTo("Ekwav"));
    }

    [Test]
    public async Task FallsBackToTheIdWhenNoNameIsKnown()
    {
        var socket = new Mock<IMinecraftSocket>();
        socket.Setup(s => s.GetPlayerName(It.IsAny<string>())).ReturnsAsync("unknown");

        var withoutUuid = await BuyConfigCommand.ResolveSellerName(
            socket.Object, "7106", null);
        var unknownName = await BuyConfigCommand.ResolveSellerName(
            socket.Object, "7106", OwnerUuid);

        Assert.Multiple(() =>
        {
            Assert.That(withoutUuid, Is.EqualTo("7106"));
            Assert.That(unknownName, Is.EqualTo("7106"));
        });
    }
}
