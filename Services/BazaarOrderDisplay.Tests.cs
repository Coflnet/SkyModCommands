using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Tutorials;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class BazaarOrderDisplayTests
{
    private Mock<IMinecraftSocket> socket;
    private Mock<ITutorialService> tutorials;
    private readonly List<Response> sent = new();

    [SetUp]
    public void Setup()
    {
        sent.Clear();
        socket = new Mock<IMinecraftSocket>();
        socket.SetupGet(s => s.Version).Returns(BazaarOrderDisplay.ClientVersion);
        socket.SetupGet(s => s.SessionInfo).Returns(new SessionInfo());
        socket.SetupGet(s => s.Settings).Returns(new FlipSettings { ModSettings = new ModSettings() });
        socket.Setup(s => s.Send(It.IsAny<Response>())).Callback<Response>(sent.Add);
        tutorials = new Mock<ITutorialService>();
        tutorials.Setup(t => t.Trigger<BazaarOrderDisplayTutorial>(socket.Object)).Returns(Task.CompletedTask);
        socket.Setup(s => s.GetService<ITutorialService>()).Returns(tutorials.Object);
    }

    [TestCase("1.9.3")]
    [TestCase("2.0.0")]
    [TestCase("2.0.0-pre2")]
    [TestCase("af-2.0.0")]
    public async Task OtherVersionsReceiveNoDisplayOrTutorial(string version)
    {
        socket.SetupGet(s => s.Version).Returns(version);
        await BazaarOrderDisplay.HandleChat(socket.Object, "[Bazaar] Sell Offer Setup! 64x Wheat for 640 coins.");
        BazaarOrderDisplay.Send(socket.Object);
        Assert.That(sent, Is.Empty);
        Assert.That(socket.Object.SessionInfo.BazaarOrders, Is.Empty);
        tutorials.Verify(t => t.Trigger<BazaarOrderDisplayTutorial>(It.IsAny<IMinecraftSocket>()), Times.Never);
    }

    [Test]
    public async Task SetupShowsSlotTwoWithDisableActionAndTriggersTutorial()
    {
        await BazaarOrderDisplay.HandleChat(socket.Object, "§6[Bazaar] §aSell Offer Setup! 1,024x Enchanted Wheat for 10,240 coins.");
        Assert.That(sent.Single().type, Is.EqualTo("infoDisplay"));
        var display = JsonConvert.DeserializeObject<InfoDisplay>(sent.Single().data);
        Assert.That(display.Id, Is.EqualTo(2));
        Assert.That(display.Clear, Is.False);
        Assert.That(display.Lines[0].text, Does.Contain("SELL").And.Contain("Enchanted Wheat"));
        Assert.That(display.Lines.Last().onClick, Is.EqualTo(BazaarOrderDisplay.DisableCommand));
        Assert.That(socket.Object.SessionInfo.BazaarOrders.Single().PricePerUnit, Is.EqualTo(10));
        tutorials.Verify(t => t.Trigger<BazaarOrderDisplayTutorial>(socket.Object), Times.Once);
    }

    [Test]
    public async Task FilledMessageUpdatesOnlyTheMatchingSide()
    {
        await BazaarOrderDisplay.HandleChat(socket.Object, "[Bazaar] Buy Order Setup! 64x Wheat for 640 coins.");
        await BazaarOrderDisplay.HandleChat(socket.Object, "[Bazaar] Sell Offer Setup! 64x Wheat for 704 coins.");
        await BazaarOrderDisplay.HandleChat(socket.Object, "[Bazaar] Your Buy Order for 64x Wheat was filled!");
        Assert.That(socket.Object.SessionInfo.BazaarOrders[0].FilledAmount, Is.EqualTo(64));
        Assert.That(socket.Object.SessionInfo.BazaarOrders[1].FilledAmount, Is.Zero);
        var display = JsonConvert.DeserializeObject<InfoDisplay>(sent.Last().data);
        Assert.That(display.Lines[0].text, Does.Contain("Filled!"));
    }

    [Test]
    public async Task DisabledPreferenceClearsAndSuppressesTutorial()
    {
        socket.Object.Settings.ModSettings.HideBazaarOrderDisplay = true;
        await BazaarOrderDisplay.HandleChat(socket.Object, "[Bazaar] Sell Offer Setup! 64x Wheat for 640 coins.");
        Assert.That(JsonConvert.DeserializeObject<InfoDisplay>(sent.Single().data).Clear, Is.True);
        tutorials.Verify(t => t.Trigger<BazaarOrderDisplayTutorial>(It.IsAny<IMinecraftSocket>()), Times.Never);
    }

    [Test]
    public async Task ReenablingShowsRetainedOrdersAndSettingIsPersistable()
    {
        socket.Object.Settings.ModSettings.HideBazaarOrderDisplay = true;
        await BazaarOrderDisplay.HandleChat(socket.Object, "[Bazaar] Sell Offer Setup! 64x Wheat for 640 coins.");
        var saved = JsonConvert.SerializeObject(socket.Object.Settings.ModSettings);
        Assert.That(JsonConvert.DeserializeObject<ModSettings>(saved).HideBazaarOrderDisplay, Is.True);
        Assert.That(new SettingsUpdater().Options(), Does.Contain("modhideBazaarOrderDisplay"));
        socket.Object.Settings.ModSettings.HideBazaarOrderDisplay = false;
        BazaarOrderDisplay.Send(socket.Object);
        Assert.That(JsonConvert.DeserializeObject<InfoDisplay>(sent.Last().data).Clear, Is.False);
    }

    [Test]
    public void EmptyOverviewClearsDisplay()
    {
        BazaarOrderDisplay.Send(socket.Object);
        Assert.That(JsonConvert.DeserializeObject<InfoDisplay>(sent.Single().data).Clear, Is.True);
    }

    [Test]
    public void FabricOverviewParsesPlainLoreAndPartialFills()
    {
        var snapshot = JsonConvert.SerializeObject(new { slotCount = 37, slots = new[] { new {
            displayName = "BUY Wheat", empty = false,
            lore = new[] { "Order amount: 1,024x", "Price per unit: 10.0 coins", "Filled: 256/1,024" }
        } } });
        socket.Object.SessionInfo.BazaarOrders = BazaarOrderStateHelper.ParseOpenOrders(snapshot, new InventoryParser());
        BazaarOrderDisplay.Send(socket.Object);
        var order = socket.Object.SessionInfo.BazaarOrders.Single();
        Assert.That(order.Amount, Is.EqualTo(1024));
        Assert.That(order.FilledAmount, Is.EqualTo(256));
        Assert.That(order.PricePerUnit, Is.EqualTo(10));
        Assert.That(JsonConvert.DeserializeObject<InfoDisplay>(sent.Single().data).Lines[0].onClick,
            Is.EqualTo("/managebazaarorders"));
    }
}
