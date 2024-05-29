using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.McConnect.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Handles finding people who circumvent delay
/// </summary>
public class CircumventTracker
{
    private IConnectApi connectApi;
    private ILogger<CircumventTracker> logger;
    private int minNewPlayerId = 0;

    private ConcurrentDictionary<string, FlipInstance> lastSeen = new();

    public CircumventTracker(IConnectApi connectApi, ILogger<CircumventTracker> logger)
    {
        this.connectApi = connectApi;
        this.logger = logger;
    }

    public void Callenge(IMinecraftSocket socket)
    {
        socket.TryAsyncTimes(async () =>
        {
            if (socket.SessionInfo.NotPurchaseRate == 0 || await socket.UserAccountTier() < AccountTier.PREMIUM)
                return;
            using var challenge = socket.CreateActivity("challengeCreate", socket.ConSpan);
            var auction = await FindAuction(socket) ?? throw new CoflnetException("no_auction", "No auction found");
            if (auction.Context.ContainsKey("cname") && !auction.Context["cname"].EndsWith("-us"))
            {
                auction.Context["cname"] = auction.Context["cname"].Replace(McColorCodes.DARK_GRAY + '.', "").Replace(McColorCodes.DARK_GRAY + "!", "") + McColorCodes.GRAY + "-us";
            }
            var lowPriced = new LowPricedAuction()
            {
                Auction = auction,
                TargetPrice = auction.StartingBid + (long)(Math.Max(socket.Settings.MinProfit, 15_000_000) * (0.2 + Random.Shared.NextDouble())),
                AdditionalProps = new() { { "bfcs", "redis" } },
                DailyVolume = (float)(socket.Settings.MinVolume + Random.Shared.NextDouble() + 0.1f),
                Finder = (Random.Shared.NextDouble() < 0.7) ? LowPricedAuction.FinderType.SNIPER : LowPricedAuction.FinderType.SNIPER_MEDIAN
            };
            var flip = FlipperService.LowPriceToFlip(lowPriced);
            var isMatch = socket.Settings.MatchesSettings(flip);
            if (isMatch.Item1)
            {
                lastSeen.TryAdd(socket.UserId, flip);
                return;
            }
            if ((socket.AccountInfo.BadActionCount > 20 || socket.SessionInfo.McName == "Ekwav") && Random.Shared.NextDouble() < 0.4)
            {
                flip.Context["match"] = "whitelist challenge"; // make it match
            };
            foreach (var item in socket.Settings.BlackList)
            {
                if (!item.MatchesSettings(flip))
                    continue;
                logger.LogError("Testflip doesn't match {UserId} {entry}", socket.UserId, BlacklistCommand.FormatEntry(item));
                break;
            }
            if (minNewPlayerId == 0)
            {
                using var context = new HypixelContext();
                minNewPlayerId = (await context.Users.MaxAsync(a => a.Id)) - 800;
            }
            var min = 4;
            if (int.TryParse(socket.UserId, out int id) && id > minNewPlayerId)
            {
                min = 2; // new players are checked earlier
            }
            if (socket.AccountInfo?.ShadinessLevel > 90)
                min /= 2;
            if (socket.SessionInfo.NotPurchaseRate >= min)
            {
                // very sus, make a flip up
                lastSeen.TryAdd(socket.UserId, flip);
                logger.LogInformation("Creating fake flip for {UserId} {uuid} {auctionUuid} rate was at {rate}", socket.UserId, socket.SessionInfo.McUuid, auction.Uuid, socket.SessionInfo.NotPurchaseRate);
            }

            logger.LogError("Testflip doesn't match {UserId} ({socket.SessionInfo.McUuid}) because {reson} {flip}", socket.UserId, socket.SessionInfo.McUuid, isMatch.Item2, JsonConvert.SerializeObject(lowPriced));
            throw new Exception("No matching flip found " + JsonConvert.SerializeObject(lowPriced));
        }, "creating challenge");
    }

    public void Shedule(IMinecraftSocket socket)
    {
        socket.TryAsyncTimes(async () =>
        {
            await Task.Delay(5000); // time to create a challenge
            if (!lastSeen.TryRemove(socket.UserId, out var flip))
                return;
            using var challenge = socket.CreateActivity("challenge", socket.ConSpan);
            challenge.Log($"Choosen auction id {flip.Auction.Uuid}");
            await Task.Delay(TimeSpan.FromSeconds(2 + Random.Shared.NextDouble() * 3));
            await SendChallangeFlip(socket, flip);
        }, "sheduling challenge");
    }

    public async Task SendChallangeFlip(IMinecraftSocket socket, FlipInstance flip)
    {
        var trackTask = connectApi.ConnectChallengePostAsync(new()
        {
            AuctionUuid = flip.Auction.Uuid,
            MinecraftUuid = socket.SessionInfo.McUuid,
            UserId = socket.UserId
        });
        await (socket as MinecraftSocket).ModAdapter.SendFlip(flip);
        await trackTask;
    }

    private static async Task<SaveAuction> FindAuction(IMinecraftSocket socket)
    {
        var oldestStart = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        foreach (var blocked in socket.TopBlocked.Where(b => b.Flip.Auction.Start > oldestStart))
        {
            if (blocked.Reason != "minProfit" && blocked.Reason != "minVolume")
                continue;
            return blocked.Flip.Auction;
        }
        using var context = new HypixelContext();
        Activity.Current?.Log("From db");
        return await context.Auctions.OrderByDescending(a => a.Id).Include(a => a.Enchantments).Include(a => a.NbtData)
            .Take(250)
            .Where(a => a.HighestBidAmount == 0 && a.Start > oldestStart).FirstOrDefaultAsync();
    }

    public class State
    {
        public string AuctionId;
    }

}