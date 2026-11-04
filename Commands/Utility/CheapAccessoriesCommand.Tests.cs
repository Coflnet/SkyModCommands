using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core.Services;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;
using Coflnet.Sky.PlayerState.Client.Model;
using Coflnet.Sky.Sniper.Client.Model;
using fNbt;
using fNbt.Tags;
using ChestView = Coflnet.Sky.PlayerState.Client.Model.ChestView;
using Ingredient = Coflnet.Sky.Crafts.Client.Model.Ingredient;
using Moq;
using NUnit.Framework;
using Item = Coflnet.Sky.Core.Services.Item;

namespace Coflnet.Sky.Commands.MC;

public class CheapAccessoriesCommandTests
{
    [Test]
    public async Task CheapAccessoriesRanksMissingAccessoriesByPricePerMagicalPower()
    {
        var socket = new CaptureSocket();
        socket.SessionInfo.McUuid = Guid.NewGuid().ToString("N");
        socket.Services.Add(MockItems());
        socket.Services.Add(MockPrices());
        socket.Services.Add(MockCrafts());
        socket.Services.Add(MockProfile("OWNED_RING"));
        socket.Services.Add(MockOpenedBag("SPEED_TALISMAN"));
        socket.Services.Add(Accessories(socket));

        await MinecraftSocket.Commands["mp"].Execute(socket, "\"\"");

        var lines = socket.Messages.Where(m => m.Contains(" coins ")).ToList();
        Assert.That(lines, Has.Count.EqualTo(3));
        // cheap talisman 30/3, speed artifact 200/(8-3) and speed ring 100/(5-3) since the talisman is owned
        Assert.That(lines[0], Does.Contain("Cheap Talisman").And.Contain("10 coins"));
        Assert.That(lines[1], Does.Contain("Speed Artifact").And.Contain("40 coins"));
        Assert.That(lines[2], Does.Contain("Speed Ring").And.Contain("50 coins"));
    }

    [Test]
    public async Task CheapAccessoriesSortsByExactPricePerMagicalPower()
    {
        var socket = new CaptureSocket();
        socket.SessionInfo.McUuid = Guid.NewGuid().ToString("N");
        socket.Services.Add(Store(new() { ["COMMON_ONE"] = Accessory("Common One", "COMMON"), ["RARE_ONE"] = Accessory("Rare One", "RARE") }));
        socket.Services.Add(Prices(new() { ["COMMON_ONE"] = 300_002, ["RARE_ONE"] = 800_005 }));
        socket.Services.Add(Accessories(socket));

        await MinecraftSocket.Commands["mp"].Execute(socket, "\"\"");

        // 800,005 / 8 = 100,000.6 is cheaper than 300,002 / 3 = 100,000.7 even though both truncate to 100,000
        var lines = socket.Messages.Where(m => m.Contains(" coins ")).ToList();
        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[0], Does.Contain("Rare One"));
        Assert.That(lines[1], Does.Contain("Common One"));
    }

    [Test]
    public async Task CheapAccessoriesAsksToOpenRemainingBagPages()
    {
        var socket = new CaptureSocket();
        socket.SessionInfo.McUuid = Guid.NewGuid().ToString("N");
        var service = new Mock<IAccessoryService>();
        service.Setup(s => s.GetOwnedAccessories(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new OwnedAccessories { Tags = ["A"], PageCount = 3, SeenPages = [1] });
        service.Setup(s => s.GetCheapestPerMagicalPower(It.IsAny<OwnedAccessories>(), It.IsAny<int>())).ReturnsAsync([]);
        socket.Services.Add(service.Object);

        await MinecraftSocket.Commands["mp"].Execute(socket, "\"\"");

        Assert.That(socket.Messages, Has.Some.Contain("Seen 1/3 accessory bag pages"));
    }

    private static IAccessoryService Accessories(CaptureSocket socket)
    {
        return new AccessoryService(socket.GetService<IProfileClient>(), socket.GetService<IPlayerStateApi>(),
            socket.GetService<IHypixelItemStore>(), socket.GetService<Sniper.Client.Api.IAuctionApi>(), socket.GetService<ICraftsApi>());
    }

    private static IHypixelItemStore MockItems()
    {
        var items = new Dictionary<string, Item>
        {
            ["SPEED_TALISMAN"] = Accessory("Speed Talisman", "COMMON"),
            ["SPEED_RING"] = Accessory("Speed Ring", "UNCOMMON"),
            ["SPEED_ARTIFACT"] = Accessory("Speed Artifact", "RARE"),
            ["CHEAP_TALISMAN"] = Accessory("Cheap Talisman", "COMMON"),
            ["OWNED_RING"] = Accessory("Owned Ring", "EPIC"),
            ["DIRT"] = System.Text.Json.JsonSerializer.Deserialize<Item>("{\"name\":\"Dirt\",\"tier\":\"COMMON\"}")
        };
        return Store(items);
    }

    private static IHypixelItemStore Store(Dictionary<string, Item> items)
    {
        var store = new Mock<IHypixelItemStore>();
        store.Setup(s => s.GetItemsAsync()).ReturnsAsync(items);
        return store.Object;
    }

    private static Item Accessory(string name, string tier)
    {
        return System.Text.Json.JsonSerializer.Deserialize<Item>($"{{\"name\":\"{name}\",\"category\":\"ACCESSORY\",\"tier\":\"{tier}\"}}");
    }

    private static Sniper.Client.Api.IAuctionApi MockPrices()
    {
        var prices = new Dictionary<string, long>
        {
            ["SPEED_TALISMAN"] = 1,
            ["SPEED_RING"] = 100,
            ["SPEED_ARTIFACT"] = 200,
            ["CHEAP_TALISMAN"] = 30,
            ["OWNED_RING"] = 1,
            ["DIRT"] = 1
        };
        return Prices(prices);
    }

    private static Sniper.Client.Api.IAuctionApi Prices(Dictionary<string, long> prices)
    {
        var api = new Mock<Sniper.Client.Api.IAuctionApi>();
        api.Setup(a => a.ApiAuctionLbinsGetAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(prices.ToDictionary(p => p.Key, p => new ReferencePrice { Price = p.Value }));
        return api.Object;
    }

    private static ICraftsApi MockCrafts()
    {
        var api = new Mock<ICraftsApi>();
        api.Setup(a => a.GetAllAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Craft("SPEED_RING", "SPEED_TALISMAN"), Craft("SPEED_ARTIFACT", "SPEED_RING")]);
        return api.Object;
    }

    private static ProfitableCraft Craft(string result, string ingredient)
    {
        return new ProfitableCraft { ItemId = result, Ingredients = [new Ingredient { ItemId = ingredient, Count = 1 }] };
    }

    private static IProfileClient MockProfile(string tag)
    {
        var item = new NbtCompound { new NbtCompound("tag") { new NbtCompound("ExtraAttributes") { new NbtString("id", tag) } } };
        var file = new NbtFile(new NbtCompound("") { new NbtList("i", [item, new NbtCompound()]) });
        var data = Convert.ToBase64String(file.SaveToBuffer(NbtCompression.GZip));
        var member = new Api.Client.Model.Member
        {
            Inventory = new Api.Client.Model.Inventory { BagContents = new Api.Client.Model.BagContents { TalismanBag = new Api.Client.Model.InventoryElem { Data = data } } }
        };
        var client = new Mock<IProfileClient>();
        client.Setup(c => c.GetProfile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync(member);
        return client.Object;
    }

    private static IPlayerStateApi MockOpenedBag(string tag)
    {
        var api = new Mock<IPlayerStateApi>();
        api.Setup(a => a.PlayerStatePlayerIdAccessoriesGetAsync(It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AccessoryBag { Accessories = [new BagAccessory { Tag = tag }], PageCount = 1, SeenPages = [1] });
        return api.Object;
    }

    private class CaptureSocket : MinecraftSocket
    {
        public List<object> Services { get; } = new();
        public List<string> Messages { get; } = new();
        public override T GetService<T>() => Services.OfType<T>().FirstOrDefault() ?? Mock.Of<T>();
        public override void Dialog(Func<SocketDialogBuilder, DialogBuilder> creation)
        {
            Messages.AddRange(creation(new SocketDialogBuilder(this)).Build().Select(p => p.text));
        }
        public override bool SendMessage(params ChatPart[] parts)
        {
            Messages.AddRange(parts.Select(p => p.text));
            return true;
        }
    }
}
