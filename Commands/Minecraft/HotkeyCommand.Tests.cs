using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using ItemRepresent = Coflnet.Sky.Api.Client.Model.ItemRepresent;
using IBazaarApi = Coflnet.Sky.Bazaar.Client.Api.IBazaarApi;
using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Core;
using Moq;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class HotkeyCommandTests
{
    [Test]
    public async Task ItemLinkIgnoresCheapAndUnpricedEnchantments()
    {
        var socket = new LinkSocket();
        socket.Bazaar.Setup(b => b.GetAllPricesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<ItemPrice>()
        {
            new() { ProductId = "ENCHANTMENT_ULTIMATE_LEGION_5", BuyPrice = 30_000_000 },
            new() { ProductId = "ENCHANTMENT_GROWTH_6", BuyPrice = 49_999 }
        });
        ItemRepresent requested = null;
        socket.Prices.Setup(p => p.ApiItemFiltersPostAsync(It.IsAny<ItemRepresent>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<ItemRepresent, int, CancellationToken>((r, _, _) => requested = r)
            .ReturnsAsync(new Dictionary<string, string>());
        var auction = new SaveAuction()
        {
            Tag = "HYPERION",
            ItemName = "Hyperion",
            NbtData = new NbtData() { data = NBT.Bytes(new fNbt.Tags.NbtCompound("")) },
            Context = new Dictionary<string, string>() { { "lore", "" } },
            Enchantments = new List<Enchantment>()
            {
                new(Enchantment.EnchantmentType.ultimate_legion, 5),
                new(Enchantment.EnchantmentType.growth, 6),
                new(Enchantment.EnchantmentType.sharpness, 5)
            }
        };

        await HotkeyCommand.GetLinkWithFilters(socket, auction);

        Assert.That(requested.Enchantments, Is.EqualTo(new Dictionary<string, int>() { { "ultimate_legion", 5 } }));
    }

    private sealed class LinkSocket : MinecraftSocket
    {
        public Mock<IBazaarApi> Bazaar = new();
        public Mock<IPricesApi> Prices = new();

        public override T GetService<T>()
        {
            if (typeof(T) == typeof(IBazaarApi))
                return (T)Bazaar.Object;
            if (typeof(T) == typeof(IPricesApi))
                return (T)Prices.Object;
            return Mock.Of<T>();
        }
    }
}
