using NUnit.Framework;
using System.Collections.Generic;

namespace Coflnet.Sky.Commands.MC;

public class CraftBreakDownCommandTests
{
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
