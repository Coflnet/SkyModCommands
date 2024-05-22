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
            var lowPriced = new LowPricedAuction()
            {
                Auction = auction,
                TargetPrice = auction.StartingBid + (auction.StartingBid * socket.Settings.MinProfitPercent / 100)
                        + (long)(Math.Max(socket.Settings.MinProfit, 3_000_000) * (1 + Random.Shared.NextDouble())),
                AdditionalProps = new(),
                DailyVolume = (float)(socket.Settings.MinVolume + Random.Shared.NextDouble() + 0.1f),
                Finder = socket.Settings.AllowedFinders.HasFlag(LowPricedAuction.FinderType.SNIPER) ? LowPricedAuction.FinderType.SNIPER : LowPricedAuction.FinderType.SNIPER_MEDIAN
            };
            var flip = FlipperService.LowPriceToFlip(lowPriced);
            var isMatch = socket.Settings.MatchesSettings(flip);
            if (isMatch.Item1)
            {
                lastSeen.TryAdd(socket.UserId, flip);
                return;
            }
            foreach (var item in socket.Settings.BlackList)
            {
                if (!item.MatchesSettings(flip))
                    continue;
                logger.LogError("Testflip doesn't match {UserId} {entry}", socket.UserId, BlacklistCommand.FormatEntry(item));
                break;
            }
            logger.LogError("Testflip doesn't match {UserId} ({socket.SessionInfo.McUuid}) because {reson} {flip}", socket.UserId, socket.SessionInfo.McUuid, isMatch.Item2, JsonConvert.SerializeObject(lowPriced));
            throw new Exception("No matching flip found " + JsonConvert.SerializeObject(lowPriced));
        }, "creating challenge");
    }

    public void Shedule(IMinecraftSocket socket)
    {
        if (!lastSeen.TryRemove(socket.UserId, out var flip))
            return;
        socket.TryAsyncTimes(async () =>
        {
            using var challenge = socket.CreateActivity("challenge", socket.ConSpan);
            challenge.Log($"Choosen auction id {flip.Auction.Uuid}");
            await Task.Delay(TimeSpan.FromSeconds(2 + Random.Shared.NextDouble() * 3));
            await connectApi.ConnectChallengePostAsync(new()
            {
                AuctionUuid = flip.Auction.Uuid,
                MinecraftUuid = socket.SessionInfo.McUuid,
                UserId = socket.UserId
            });
            await socket.SendFlip(flip);
        }, "sheduling challenge");
    }

    private static async Task<SaveAuction> FindAuction(IMinecraftSocket socket)
    {
        foreach (var blocked in socket.TopBlocked.Where(b => b.Flip.Auction.Start < DateTime.UtcNow - TimeSpan.FromMinutes(1)))
        {
            if (blocked.Reason != "minProfit" && blocked.Reason != "minVolume")
                continue;
            return blocked.Flip.Auction;
        }
        using var context = new HypixelContext();
        Activity.Current?.Log("From db");
        return await context.Auctions.OrderByDescending(a => a.Id).Include(a => a.Enchantments).Include(a => a.NbtData)
            .Take(250)
            .Where(a => a.HighestBidAmount == 0).FirstOrDefaultAsync();
    }

    public class State
    {
        public string AuctionId;
    }

}