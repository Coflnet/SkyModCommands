using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using IItemsApi = Coflnet.Sky.Items.Client.Api.IItemsApi;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>Drives the upload-triggered refill path (UploadBazaarOrders -> RefillOrders -> TryRecommendBuy) with a real adapter.</summary>
public class BazaarRefillPathTests
{
    private const string Tag = "ENCHANTED_COAL";
    private RefillSocket socket;
    private BazaarFlipService service;

    [SetUp]
    public void Setup()
    {
        var orderBooks = new Mock<IOrderBookApi>();
        orderBooks.Setup(o => o.GetOrderBookAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderBook { Buy = new List<OrderEntry> { new() { PricePerUnit = 90, UserId = null } } });
        orderBooks.Setup(o => o.GetOrderBooksAsync(It.IsAny<List<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, OrderBook>());
        var items = new Mock<IItemsApi>();
        items.Setup(i => i.ItemsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Coflnet.Sky.Items.Client.Model.Item>());

        socket = new RefillSocket(new Dictionary<Type, object>
        {
            [typeof(IOrderBookApi)] = orderBooks.Object,
            [typeof(IItemsApi)] = items.Object,
        });
        var settings = new FlipSettings { ModSettings = new ModSettings(), AllowedFinders = LowPricedAuction.FinderType.Bazaar };
        var lifecycle = new ModSessionLifesycle(socket)
        {
            UserId = SelfUpdatingValue<string>.CreateNoUpdate("5"),
            FlipSettings = SelfUpdatingValue<FlipSettings>.CreateNoUpdate(settings),
        };
        socket.SetLifecycle(lifecycle);
        socket.SessionInfo.McUuid = "uuid-1";
        socket.ModAdapter = new FullAfVersionAdapter(socket);

        service = new BazaarFlipService(null, Mock.Of<IBazaarFlipperApi>(), orderBooks.Object, Mock.Of<IFleetApi>(), null,
            items.Object, null, NullLogger<BazaarFlipService>.Instance);
        var candidate = new DemandFlip { ItemTag = Tag, BuyPrice = 100, SellPrice = 120, CurrentProfitPerHour = 1000, Volume = 1000 };
        var list = new List<DemandFlip> { candidate };
        var pools = new BazaarFlipService.TierCandidatePools(list, list, list, list, list);
        SetField("latestPools", pools);
        SetField("latestNames", new Dictionary<string, string> { [Tag] = "Enchanted Coal" });
    }

    private void SetField(string name, object value)
        => typeof(BazaarFlipService).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(service, value);

    private IEnumerable<Response> PlaceOrders => socket.Sent.Where(r => r.type == "placeOrder");

    [Test]
    public async Task RefillPath_BuysWhenUserHasNothingForTheItem()
    {
        await service.RefillOrders(socket, 18);

        Assert.That(PlaceOrders.Count(), Is.EqualTo(1), "control: the harness reaches placeOrder for a fresh item");
    }

    [Test]
    public async Task RefillPath_SecondBuyForSameItemAtNewPrice_NeverReachesPlaceOrder()
    {
        // the user already has an open BUY for the item at a different price than the new recommendation (90.1)
        socket.SessionInfo.BazaarOrders = new List<BazaarOrderInfo>
        {
            new() { ItemTag = Tag, ItemName = "Enchanted Coal", Side = BazaarOrderSide.Buy, Amount = 64, PricePerUnit = 80 }
        };

        await service.RefillOrders(socket, 18);

        Assert.That(PlaceOrders, Is.Empty);
    }

    [Test]
    public async Task RefillPath_UnresolvedSentIntentHeldStockAndGoalAllBlockTheBuy()
    {
        socket.SessionInfo.SentBazaarOrders.Add(new SentBazaarOrderInfo { ItemTag = Tag, ItemName = "Enchanted Coal", Side = BazaarOrderSide.Buy, PricePerUnit = 70, Amount = 64 });
        await service.RefillOrders(socket, 18);
        Assert.That(PlaceOrders, Is.Empty, "unconfirmed sent intent at another price");

        socket.SessionInfo.SentBazaarOrders.Clear();
        socket.SessionInfo.LastBazaarRefillAttempt = default;
        socket.SessionInfo.BazaarOrders = new List<BazaarOrderInfo>
        {
            new() { ItemTag = Tag, ItemName = "Enchanted Coal", Side = BazaarOrderSide.Sell, Amount = 64, PricePerUnit = 150 } // sell escrow
        };
        await service.RefillOrders(socket, 18);
        Assert.That(PlaceOrders, Is.Empty, "sell escrow");

        socket.SessionInfo.BazaarOrders = new();
        socket.SessionInfo.ActivePlanGoalTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Tag };
        socket.SessionInfo.LastBazaarRefillAttempt = default;
        await service.RefillOrders(socket, 18);
        Assert.That(PlaceOrders, Is.Empty, "active bazaar flip goal");
    }

    private class RefillSocket(Dictionary<Type, object> services) : MinecraftSocket
    {
        public List<Response> Sent { get; } = new();
        public void SetLifecycle(ModSessionLifesycle l) => sessionLifesycle = l;
        public override T GetService<T>() => services.TryGetValue(typeof(T), out var s) ? (T)s : default;
        public override void Dialog(Func<SocketDialogBuilder, DialogBuilder> creation) { }
        public override bool SendMessage(params ChatPart[] parts) => true;
        public override void Send(Response response) => Sent.Add(response);
    }
}
