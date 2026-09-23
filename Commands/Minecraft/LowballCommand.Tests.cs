using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.PlayerState.Client.Model;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.MC;

public class LowballCommandTests
{
    [Test]
    public void CreateLowballAuctionCarriesSellerAndLore()
    {
        var item = new Item()
        {
            Tag = "HYPERION",
            ItemName = "Hyperion",
            Description = "§7Wither Impact",
            Count = 1,
            ExtraAttributes = new Dictionary<string, object>()
            {
                { "tier", 5 },
                { "uuid", "123e4567-e89b-12d3-a456-426614174000" }
            }
        };

        var auction = LowballCommand.CreateLowballAuction(item, "seller-uuid");

        Assert.That(auction.AuctioneerId, Is.EqualTo("seller-uuid"));
        Assert.That(auction.Context, Is.Not.Null);
        Assert.That(auction.Context["lore"], Is.EqualTo(item.Description));
    }

    [Test]
    public async Task PendingMatchCountRequestAggregatesAllResponses()
    {
        var pendingRequest = new LowballSerivce.PendingMatchCountRequest();

        pendingRequest.SetExpectedResponses(2);
        pendingRequest.AddResponse(3);
        pendingRequest.AddResponse(4);

        var totalCount = await pendingRequest.WaitAsync(TimeSpan.FromMilliseconds(50));

        Assert.That(totalCount, Is.EqualTo(7));
    }

    [Test]
    public async Task PendingMatchCountRequestReturnsPartialCountOnTimeout()
    {
        var pendingRequest = new LowballSerivce.PendingMatchCountRequest();

        pendingRequest.SetExpectedResponses(2);
        pendingRequest.AddResponse(5);

        var totalCount = await pendingRequest.WaitAsync(TimeSpan.Zero);

        Assert.That(totalCount, Is.EqualTo(5));
    }
}