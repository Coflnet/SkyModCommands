using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

public class CraftBreakDownCommandTests
{
    [TestCase(0)]
    [TestCase(2)]
    public void SubcraftShowsSavingsAndActualIngredients(int purchasedCount)
    {
        var nodes = new List<CraftBreakDownCommand.CraftNode>();
        CraftBreakDownCommand.AddPlan(nodes, new()
        {
            ItemId = "ENCHANTED_DIAMOND",
            Quantity = 4,
            CraftedQuantity = 4 - purchasedCount,
            Cost = 400,
            DirectBuyCost = 800,
            DirectBuyEnough = true,
            Enough = true,
            Method = "craft",
            Purchases = purchasedCount == 0 ? new() : new() { new() { Source = "order", Quantity = purchasedCount, Cost = 200 } },
            Ingredients = new()
            {
                new() { ItemId = "DIAMOND", Quantity = 160 * (4 - purchasedCount), Cost = 200, Enough = true, Method = "buy" }
            }
        }, 0);
        var db = DialogBuilder.New;
        CraftBreakDownCommand.RenderCraftTree(new MinecraftSocket(), db, new()
        {
            Cost = 400,
            Nodes = nodes,
            Names = new() { ["DIAMOND"] = new("Diamond", McColorCodes.WHITE, true) }
        });
        var parts = db.Build();
        var subcraft = parts.Single(p => p.onClick == "/cofl recipe ENCHANTED_DIAMOND");
        Assert.That(subcraft.text, Does.Contain("subcraft saves 400"));
        Assert.That(parts.Single(p => p.onClick == "/cofl bazaar Diamond").text,
            Does.Contain($"x{160 * (4 - purchasedCount)}"));
        Assert.That(nodes[1].Depth, Is.EqualTo(1));
        Assert.That(nodes[0].Acquisition?.Order.Qty ?? 0, Is.EqualTo(purchasedCount));
    }

    [Test]
    public void BackendPlanIsFlattenedWithoutRepricingIt()
    {
        var plan = new CraftBreakDownCommand.BackendAcquisitionPlan
        {
            ItemId = "ENCHANTED_OBSIDIAN",
            Quantity = 6_144,
            Cost = 19_660_800,
            DirectBuyCost = 19_660_800,
            DirectBuyEnough = true,
            CraftCost = 25_221_280,
            CraftEnough = true,
            Enough = true,
            Method = "buy",
            Purchases = new()
            {
                new() { Source = "order", Quantity = 6_144, UnitPrice = 3_200, Cost = 19_660_800 }
            }
        };
        var nodes = new List<CraftBreakDownCommand.CraftNode>();

        CraftBreakDownCommand.AddPlan(nodes, plan, 0);

        Assert.That(nodes, Has.Count.EqualTo(1));
        Assert.That(nodes[0].Cost, Is.EqualTo(19_660_800));
        Assert.That(nodes[0].FullSubcraftCost, Is.EqualTo(25_221_280));
        Assert.That(nodes[0].CraftedCount, Is.Zero);
        Assert.That(nodes[0].Acquisition.Order.Qty, Is.EqualTo(6_144));
    }
}
