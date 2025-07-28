using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Coflnet.Sky.Core;
using Coflnet.Sky.Commands.Shared;
using System;
using Coflnet.Sky.ModCommands.Services;
using System.Collections.Generic;
using static Coflnet.Sky.ModCommands.Services.BlockedService;
using Coflnet.Sky.Commands.MC;
using System.Linq;

namespace Coflnet.Sky.ModCommands.Controllers;

[ApiController]
[Route("[controller]")]
public class FlipController : ControllerBase
{
    private FlipperService flipperService;
    private IBlockedService blockedService;
    private NecImportService necImportService;

    public FlipController(FlipperService flipperService, IBlockedService blockedService, NecImportService necImportService)
    {
        this.flipperService = flipperService;
        this.blockedService = blockedService;
        this.necImportService = necImportService;
    }

    [HttpGet]
    [Route("blocked")]
    public async Task<IEnumerable<BlockedReason>> GetBlockedReasons(string userId, string auctionUuid)
    {
        return await blockedService.GetBlockedReasons(userId, Guid.Parse(auctionUuid));
    }

    [HttpPost]
    [Route("nec")]
    public async Task AddNecUser([FromBody] NecImportService.NecUser[] users)
    {
        var count = 0;
        foreach (var user in users.GroupBy(u => u.Uuid).Select(g => g.OrderByDescending(u => u.Key == null ? 0 : 1).First()))
        {
            await necImportService.AddUser(user);
            count++;
        }
        Console.WriteLine($"Added {count} users");
    }

    [HttpGet("for/{playerUuid}/item/{itemUuid}")]
    public async Task<long?> GetFlipTarget(Guid playerUuid, Guid itemUuid, [FromServices] PriceStorageService priceStorageService)
    {
        return await priceStorageService.GetPrice(playerUuid, itemUuid);
    }

    /// <summary>
    /// Indicates status of service, should be 200 (OK)
    /// </summary>
    /// <returns></returns>
    [HttpPost]
    [Route("")]
    public async Task TrackFlip([FromBody] LowPricedAuction[] flips)
    {
        foreach (var item in flips)
        {
            item.Auction.Uuid = Guid.NewGuid().ToString().Replace("-", "");
            item.Auction.Context = new() { { "pre-api", "" }, { "cname", item.Auction.ItemName } };
        }
        //Console.WriteLine(JsonConvert.SerializeObject(flips, Formatting.Indented));
        await flipperService.DeliverLowPricedAuctions(flips);
    }
}
