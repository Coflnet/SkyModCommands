using System;
using System.Reflection;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class GiftConfigCommandTests
{
    private static readonly MethodInfo ExecuteWithArgs = typeof(GiftConfigCommand)
        .GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            new[] { typeof(IMinecraftSocket), typeof(ArgumentsCommand.Arguments) },
            null);

    [Test]
    public void RequestsExternalSaleRecognizesTheArgumentInAnyCase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GiftConfigCommand.RequestsExternalSale(
                "\"config ign external\""), Is.True);
            Assert.That(GiftConfigCommand.RequestsExternalSale(
                "\"config ign EXTERNAL\""), Is.True);
            Assert.That(GiftConfigCommand.RequestsExternalSale(
                "\"config ign gift\""), Is.False);
            Assert.That(GiftConfigCommand.RequestsExternalSale(
                "\"config ign\""), Is.False);
        });
    }

    [Test]
    public async Task ExternalSourceArgumentIsRefusedAndCreatesNoEntitlement()
    {
        var socket = new Mock<IMinecraftSocket>();
        string sentMessage = null;
        socket.Setup(s => s.SendMessage(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((msg, _, _) => sentMessage = msg);
        var args = new ArgumentsCommand.Arguments
        {
            ["configName"] = "myconfig",
            ["ign"] = "Steve",
            ["source"] = "external"
        };

        // Invoking the args-parsed Execute directly (bypassing the raw-string
        // pre-check) proves the refusal is enforced here too and returns
        // before any storage access (SelfUpdatingValue.Create would throw
        // without a configured DI container if this path were reached).
        await (Task)ExecuteWithArgs.Invoke(
            new GiftConfigCommand(), new object[] { socket.Object, args });

        Assert.That(sentMessage, Is.EqualTo(
            GiftConfigCommand.ExternalSaleRefusalMessage));
        socket.Verify(s => s.UserId, Times.Never);
    }

    [Test]
    public void BuildGiftAlwaysRecordsACreatorGift()
    {
        var config = new ConfigContainer
        {
            Name = "myconfig",
            Version = 3,
            ChangeNotes = "some notes"
        };

        var gift = GiftConfigCommand.BuildGift(
            "myconfig", config, "owner-1", "OwnerIgn");

        Assert.Multiple(() =>
        {
            Assert.That(gift.CreatorGift, Is.True);
            Assert.That(gift.OwnerId, Is.EqualTo("owner-1"));
            Assert.That(gift.Version, Is.EqualTo(3));
            Assert.That(gift.AccessUntilUtc, Is.EqualTo(
                gift.BoughtAt.AddYears(BuyConfigCommand.UpdateTermYears)));
        });
    }
}
