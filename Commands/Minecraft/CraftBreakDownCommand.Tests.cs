using Coflnet.Sky.Bazaar.Client.Model;
using Coflnet.Sky.Crafts.Client.Model;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace Coflnet.Sky.Commands.MC;

public class CraftBreakDownCommandTests
{
    [Test]
    public void LargeSubcraftIsRejectedWhenNpcLimitedInputsCostMoreThanBuyingTheIntermediate()
    {
        var enchantedObsidian = new Ingredient
        {
            ItemId = "ENCHANTED_OBSIDIAN",
            Count = 6_144,
            Type = "craft",
            BuyOrderCapacity = 6_144,
            BuyOrderUnitPrice = 3_200,
            InstaBuyUnitPrice = 3_300
        };
        var obsidian = new Ingredient
        {
            ItemId = "OBSIDIAN",
            Count = 160,
            NpcCapacity = 640,
            NpcUnitPrice = 14,
            BuyOrderCapacity = 71_000,
            BuyOrderUnitPrice = 17.5,
            InstaBuyUnitPrice = 26.3
        };

        var nodes = BuildTree(enchantedObsidian, obsidian);
        var enchantedNode = nodes[0];

        Assert.That(nodes, Has.Count.EqualTo(1));
        Assert.That(enchantedNode.Method, Is.EqualTo("buy"));
        Assert.That(enchantedNode.Cost, Is.EqualTo(19_660_800));
        Assert.That(enchantedNode.DirectBuyCost, Is.EqualTo(19_660_800));
        Assert.That(enchantedNode.FullSubcraftCost, Is.EqualTo(25_221_280).Within(0.01));
        Assert.That(enchantedNode.CraftedCount, Is.Zero);
        Assert.That(enchantedNode.Enough, Is.True);
    }

    [Test]
    public void CheapDirectSupplyIsBoughtAndTheRemainderIsCrafted()
    {
        var enchantedObsidian = new Ingredient
        {
            ItemId = "ENCHANTED_OBSIDIAN",
            Count = 6_144,
            Type = "craft",
            NpcCapacity = 1_000,
            NpcUnitPrice = 2_000,
            InstaBuyUnitPrice = 5_000
        };
        var obsidian = new Ingredient
        {
            ItemId = "OBSIDIAN",
            Count = 160,
            NpcCapacity = 640,
            NpcUnitPrice = 14,
            BuyOrderCapacity = 71_000,
            BuyOrderUnitPrice = 17.5,
            InstaBuyUnitPrice = 26.3
        };

        var nodes = BuildTree(enchantedObsidian, obsidian);
        var enchantedNode = nodes[0];

        Assert.That(nodes, Has.Count.EqualTo(2));
        Assert.That(enchantedNode.Method, Is.EqualTo("craft"));
        Assert.That(enchantedNode.Acquisition.Npc.Qty, Is.EqualTo(1_000));
        Assert.That(enchantedNode.CraftedCount, Is.EqualTo(5_144));
        Assert.That(enchantedNode.DirectBuyCost, Is.EqualTo(27_720_000));
        Assert.That(enchantedNode.FullSubcraftCost, Is.EqualTo(25_221_280).Within(0.01));
        Assert.That(enchantedNode.Cost, Is.EqualTo(23_013_280).Within(0.01));
        Assert.That(nodes[1].Count, Is.EqualTo(823_040));
    }

    [Test]
    public void InstaBuyWalksActualSellOffersInsteadOfRepeatingTheTopPrice()
    {
        var enderPearls = new Ingredient
        {
            ItemId = "ENDER_PEARL",
            Count = 1_024_000,
            BuyOrderCapacity = 71_000,
            BuyOrderUnitPrice = 2.5,
            InstaBuyUnitPrice = 8.9
        };
        var root = new ProfitableCraft
        {
            ItemId = "ROOT",
            Ingredients = new List<Ingredient> { enderPearls }
        };
        var nodes = new List<CraftBreakDownCommand.CraftNode>();
        var orderBooks = new Dictionary<string, OrderBook>
        {
            ["ENDER_PEARL"] = Book(
                (95_928, 9.8), (46_996, 9.9), (24, 10.9), (240, 11.0),
                (3_618, 11.1), (16_645, 11.2), (65_796, 11.3), (723_753, 20))
        };

        CraftBreakDownCommand.AddIngredients(
            nodes, root, 1, 0, new Dictionary<string, ProfitableCraft>(),
            new HashSet<string> { "ROOT" }, orderBooks);

        var node = nodes[0];
        Assert.That(node.Acquisition.Order.Qty, Is.EqualTo(71_000));
        Assert.That(node.Acquisition.Insta.Qty, Is.EqualTo(953_000));
        Assert.That(node.Acquisition.Insta.UnitPrice, Is.EqualTo(16_853_395d / 953_000).Within(0.0001));
        Assert.That(node.Cost, Is.EqualTo(17_030_895).Within(0.01));
        Assert.That(node.Enough, Is.True);
    }

    private static List<CraftBreakDownCommand.CraftNode> BuildTree(Ingredient enchantedObsidian, Ingredient obsidian)
    {
        var root = new ProfitableCraft
        {
            ItemId = "ROOT",
            Ingredients = new List<Ingredient> { enchantedObsidian }
        };
        var enchantedRecipe = new ProfitableCraft
        {
            ItemId = "ENCHANTED_OBSIDIAN",
            Ingredients = new List<Ingredient> { obsidian }
        };
        var nodes = new List<CraftBreakDownCommand.CraftNode>();
        var orderBooks = new Dictionary<string, OrderBook>
        {
            ["ENCHANTED_OBSIDIAN"] = Book((1_000_000, enchantedObsidian.InstaBuyUnitPrice)),
            ["OBSIDIAN"] = Book((1_000_000, obsidian.InstaBuyUnitPrice))
        };

        CraftBreakDownCommand.AddIngredients(
            nodes,
            root,
            1,
            0,
            new Dictionary<string, ProfitableCraft> { ["ENCHANTED_OBSIDIAN"] = enchantedRecipe },
            new HashSet<string> { "ROOT" },
            orderBooks);
        return nodes;
    }

    private static OrderBook Book(params (int Amount, double Price)[] offers)
        => new()
        {
            Sell = offers.Select(o => new OrderEntry { Amount = o.Amount, PricePerUnit = o.Price }).ToList()
        };
}
