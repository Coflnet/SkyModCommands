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
using Newtonsoft.Json;

namespace Coflnet.Sky.ModCommands.Controllers;

[ApiController]
[Route("[controller]")]
public class FlipController : ControllerBase
{
    private FlipperService flipperService;
    private IBlockedService blockedService;
    private NecImportService necImportService;
    private FlipDryRunService dryRunService;
    private McConnect.Api.IConnectApi connectApi;

    public FlipController(FlipperService flipperService, IBlockedService blockedService, NecImportService necImportService,
        FlipDryRunService dryRunService, McConnect.Api.IConnectApi connectApi)
    {
        this.dryRunService = dryRunService;
        this.connectApi = connectApi;
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

    /// <summary>
    /// Recorded blocked reasons of a user (prem+ sessions only, kept 7 days)
    /// </summary>
    /// <param name="userId">Coflnet account id, see by-mc</param>
    /// <param name="limit">max rows, up to 20000</param>
    [HttpGet("blocked/user/{userId}")]
    public async Task<List<BlockedReasonView>> GetBlockedReasonsOfUser(string userId, int limit = 1000)
    {
        return (await blockedService.GetBlockedReasons(userId, limit)).Select(b => new BlockedReasonView(b)).ToList();
    }

    /// <summary>
    /// Blocked reasons of a user grouped by reason, sorted by the profit they cost
    /// </summary>
    [HttpGet("blocked/summary/{userId}")]
    public async Task<List<BlockedReasonSummary>> GetBlockedSummary(string userId, int limit = 20000)
    {
        return BlockedService.Summarize(await blockedService.GetBlockedReasons(userId, limit));
    }

    /// <summary>
    /// Looks up the Coflnet account id and linked minecraft accounts of a minecraft uuid
    /// </summary>
    [HttpGet("user/by-mc/{mcUuid}")]
    public async Task<ActionResult<UserByMcResponse>> GetUserByMc(string mcUuid)
    {
        McConnect.Model.User user;
        try
        {
            user = await connectApi.ConnectMinecraftMcUuidGetAsync(mcUuid);
        }
        catch (McConnect.Client.ApiException e) when (e.ErrorCode == 404)
        {
            return NotFound();
        }
        if (user == null || string.IsNullOrEmpty(user.ExternalId))
            return NotFound();
        return new UserByMcResponse()
        {
            UserId = user.ExternalId,
            Accounts = user.Accounts?.Select(a => new LinkedAccount()
            {
                AccountUuid = a.AccountUuid,
                Verified = a.Verified,
                LastRequestedAt = a.LastRequestedAt
            }).ToList() ?? new()
        };
    }

    /// <summary>
    /// Replays the settings of a user against a stored auction
    /// </summary>
    /// <param name="userId">Coflnet account id</param>
    /// <param name="auctionUuid">auction to replay</param>
    /// <param name="finder">finder name to test, eg SNIPER, defaults to the first the tracker recorded</param>
    [HttpGet("wouldmatch")]
    public async Task<ActionResult<WouldMatchResult>> WouldMatch(string userId, string auctionUuid, string finder = null)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(auctionUuid))
            return BadRequest("userId and auctionUuid are required");
        LowPricedAuction.FinderType? parsed = null;
        if (!string.IsNullOrEmpty(finder))
        {
            if (!Enum.TryParse<LowPricedAuction.FinderType>(finder, true, out var f))
                return BadRequest($"unknown finder {finder}");
            parsed = f;
        }
        var result = await dryRunService.Evaluate(userId, auctionUuid, parsed);
        if (result == null)
            return NotFound("auction not found");
        return result;
    }

    /// <summary>
    /// Replays the settings of a user against a given flip
    /// </summary>
    [HttpPost("wouldmatch")]
    public async Task<ActionResult<WouldMatchResult>> WouldMatchPosted(string userId, [FromBody] LowPricedAuction flip)
    {
        if (string.IsNullOrEmpty(userId) || flip?.Auction == null)
            return BadRequest("userId and a flip with an auction are required");
        flip.AdditionalProps ??= new();
        return await dryRunService.Evaluate(userId, flip);
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
    public async Task<long?> GetFlipTarget(Guid playerUuid, Guid itemUuid, [FromServices] IPriceStorageService priceStorageService)
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


public class UserByMcResponse
{
    [JsonProperty("userId")]
    public string UserId { get; set; }
    [JsonProperty("accounts")]
    public List<LinkedAccount> Accounts { get; set; }
}

public class LinkedAccount
{
    [JsonProperty("accountUuid")]
    public string AccountUuid { get; set; }
    [JsonProperty("verified")]
    public bool Verified { get; set; }
    [JsonProperty("lastRequestedAt")]
    public DateTime LastRequestedAt { get; set; }
}
