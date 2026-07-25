using Coflnet.Sky.Crafts.Client.Model;
using NUnit.Framework;
using System.Collections.Generic;

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

        CraftBreakDownCommand.AddIngredients(
            nodes,
            root,
            1,
            0,
            new Dictionary<string, ProfitableCraft> { ["ENCHANTED_OBSIDIAN"] = enchantedRecipe },
            new HashSet<string> { "ROOT" });
        return nodes;
    }
}
