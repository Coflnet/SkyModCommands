using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Models;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class BazaarOrderStateHelperTests
{
    [Test]
    public void ParseOpenOrdersOnlyKeepsTrackedOrdersFromTopBazaarRows()
    {
        var parser = new InventoryParser();
        var slots = Enumerable.Repeat<object>(null, 72).ToArray();
        slots[0] = new
        {
            count = 1,
            displayName = string.Empty,
            name = "minecraft:black_stained_glass_pane",
            slot = 0
        };
        slots[19] = CreateAzaleaItem(
            "WHEAT",
            "order-1",
            "§a§lBUY §aWheat",
            "§8Worth 10M coins\n\n§7Order amount: §a400§7x\n\n§7Price per unit: §625,130.6 coins\n\n§eClick to view options!",
            19);
        slots[20] = CreateAzaleaItem(
            "ROTTEN_FLESH",
            "order-2",
            "§6§lSELL §aEnchanted Rotten Flesh",
            "§8Worth 519k coins\n\n§7Offer amount: §a2,640§7x\n§7Filled: §61,640§7/2,640 §a§l62%!\n\n§8Expired!\n\n§7Price per unit: §61,820.9 coins\n\n§7Customers:\n§8- §a1,501§7x §b[MVP§2+§b] Terminator602§f §819d ago\n§8- §a139§7x §a[VIP§6+§a] Luka_Daddy§f §819d ago\n\n§eYou have §6519,466 coins §eto claim!\n\n§eClick to claim!",
            20);
        slots[21] = CreateAzaleaItem(
            "AGATHA_COUPON",
            "order-3",
            "§6§lSELL §aAgatha's Coupon",
            "§8Worth 5M coins\n\n§7Offer amount: §a300§7x\n\n§8Expired!\n\n§7Price per unit: §617,000.0 coins\n\n§7By: §b[MVP§4+§b] Ekwav\n\n§eClick to view options!",
            21);
        slots[63] = CreateAzaleaItem("PET_TURTLE", "menu-item", "§6Turtle", "§6§lLEGENDARY", 63);

        var json = JsonConvert.SerializeObject(new
        {
            slotCount = 72,
            slots
        });

        var result = BazaarOrderStateHelper.ParseOpenOrders(json, parser);

        Assert.That(result.Select(item => item.ItemTag), Is.EqualTo(new[] { "WHEAT", "ROTTEN_FLESH", "AGATHA_COUPON" }));
        Assert.That(result[0].Side, Is.EqualTo(BazaarOrderSide.Buy));
        Assert.That(result[0].Amount, Is.EqualTo(400));
        Assert.That(result[0].PricePerUnit, Is.EqualTo(25130.6));

        Assert.That(result[1].Side, Is.EqualTo(BazaarOrderSide.Sell));
        Assert.That(result[1].ItemName, Is.EqualTo("Enchanted Rotten Flesh"));
        Assert.That(result[1].FilledAmount, Is.EqualTo(1640));
        Assert.That(result[1].Players, Has.Count.EqualTo(2));
        Assert.That(result[1].Players[0].Amount, Is.EqualTo(1501));
        Assert.That(result[1].Players[0].PlayerName, Is.EqualTo("§b[MVP§2+§b] Terminator602"));
        Assert.That(result[1].IsExpired, Is.True);
        Assert.That(result[1].ExpirationText, Is.EqualTo("Expired!"));

        Assert.That(result[2].PlacedBy, Is.EqualTo("§b[MVP§4+§b] Ekwav"));
    }

    [TestCase(19, false)]
    [TestCase(20, true)]
    [TestCase(21, true)]
    public void HasReachedBuyOrderLimitUsesTwentyOrders(int orderCount, bool expected)
    {
        var orders = Enumerable.Range(0, orderCount)
            .Select(index => new BazaarOrderInfo { ItemTag = $"TAG_{index}", PricePerUnit = 1 })
            .ToList();

        Assert.That(BazaarOrderStateHelper.HasReachedBuyOrderLimit(orders), Is.EqualTo(expected));
    }

    private static object CreateAzaleaItem(string tag, string uuid, string name, string lore, int slot)
    {
        return new
        {
            count = 1,
            displayName = name,
            name = "minecraft:paper",
            slot,
            nbt = new Dictionary<string, object>
            {
                ["minecraft:custom_data"] = new Dictionary<string, object>
                {
                    ["id"] = tag,
                    ["uuid"] = uuid
                },
                ["minecraft:custom_name"] = name,
                ["minecraft:lore"] = lore.Split('\n')
            }
        };
    }
}