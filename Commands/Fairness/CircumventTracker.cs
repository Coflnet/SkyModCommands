using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            await CreateChallenge(socket);
        }, "creating challenge");
    }

    public async Task<string?> CreateChallenge(IMinecraftSocket socket, bool forceCreate = false)
    {
        if (socket.SessionInfo.NotPurchaseRate <= 2 || await socket.UserAccountTier() < AccountTier.PREMIUM || socket.sessionLifesycle.CurrentDelay > TimeSpan.FromSeconds(1)
                        || LikelyLegit(socket) && Random.Shared.NextDouble() < 0.9)
        {
            Activity.Current?.Log($"Not creating challenge because probably legit {socket.SessionInfo.NotPurchaseRate}");
            return null;
        }
        using var challenge = socket.CreateActivity("challengeCreate", socket.ConSpan);
        var pastChallenges = await connectApi.ConnectChallengesUserIdGetWithHttpInfoAsync(socket.UserId);
        var matchingChallengeCount = pastChallenges.Data.Count(c => socket.SessionInfo.MinecraftUuids.Contains(c.BoughtBy));
        var notMatchingCount = pastChallenges.Data.Count(c => c.BoughtBy != null && !socket.SessionInfo.MinecraftUuids.Contains(c.BoughtBy));
        if (matchingChallengeCount > 0 && matchingChallengeCount > notMatchingCount)
        {
            Activity.Current?.Log($"Not creating challenge because too many matching challenges {matchingChallengeCount} {notMatchingCount} total {pastChallenges.Data.Count}");
            Activity.Current?.AddTag("reason", "selfbought");
            return null;
        }

        var auction = await FindAuction(socket) ?? throw new Exception("No auction found");
        if (auction.Context.ContainsKey("cname") && !auction.Context["cname"].EndsWith("-us") && Random.Shared.NextDouble() < 0.5)
        {
            auction.Context["cname"] = auction.Context["cname"].Replace(McColorCodes.DARK_GRAY + '.', "").Replace(McColorCodes.DARK_GRAY + "!", "") + McColorCodes.GRAY + "-us";
        }
        var lowPriced = new LowPricedAuction()
        {
            Auction = auction,
            TargetPrice = auction.StartingBid + (long)(Math.Max(socket.Settings.MinProfit, 15_000_000) * (0.2 + Random.Shared.NextDouble())),
            AdditionalProps = new() { { "bfcs", "redis" }, { "challenge", "" } },
            DailyVolume = (float)(socket.Settings.MinVolume + Random.Shared.NextDouble() + 0.1f),
            Finder = (Random.Shared.NextDouble() < 0.7) ? LowPricedAuction.FinderType.SNIPER : LowPricedAuction.FinderType.SNIPER_MEDIAN
        };
        var flip = FlipperService.LowPriceToFlip(lowPriced);
        var isMatch = socket.Settings.MatchesSettings(flip);
        if (isMatch.Item1)
        {
            lastSeen.TryAdd(socket.UserId, flip);
            return flip.Auction.Uuid;
        }
        if ((socket.AccountInfo.BadActionCount > 20 || socket.SessionInfo.McName == "Ekwav") && Random.Shared.NextDouble() < 0.4)
        {
            flip.Context["match"] = "whitelist challenge"; // make it match
        };
        LogMatchingFilter(socket, flip);
        await LoadNewPlayerThreshold();
        var min = 4;
        if (int.TryParse(socket.UserId, out int id) && id > minNewPlayerId)
        {
            min = 2; // new players are checked earlier
        }
        if (socket.AccountInfo?.ShadinessLevel > 90)
            min /= 2;
        if (socket.SessionInfo.NotPurchaseRate >= min || forceCreate)
        {
            // very sus, make a flip up
            lastSeen.TryAdd(socket.UserId, flip);
            logger.LogInformation("Creating fake flip for {UserId} {uuid} {auctionUuid} rate was at {rate}", socket.UserId, socket.SessionInfo.McUuid, auction.Uuid, socket.SessionInfo.NotPurchaseRate);
            return flip.Auction.Uuid;
        }

        logger.LogError("Testflip doesn't match {UserId} ({socket.SessionInfo.McUuid}) because {reson} {flip}", socket.UserId, socket.SessionInfo.McUuid, isMatch.Item2, JsonConvert.SerializeObject(lowPriced));
        throw new Exception("No matching flip found " + JsonConvert.SerializeObject(lowPriced));
    }

    private static bool LikelyLegit(IMinecraftSocket socket)
    {
        return socket.AccountInfo.McIds.Count > 3 || socket.AccountInfo.ExpiresAt > DateTime.UtcNow + TimeSpan.FromDays(20) || socket.UserId.Length < 6 || socket.sessionLifesycle.CurrentDelay > TimeSpan.FromSeconds(0.5);
    }

    private async Task LoadNewPlayerThreshold()
    {
        if (minNewPlayerId <= 0)
        {
            using var context = new HypixelContext();
            minNewPlayerId = (await context.Users.MaxAsync(a => a.Id)) - 800;
        }
    }

    private void LogMatchingFilter(IMinecraftSocket socket, FlipInstance flip)
    {
        foreach (var item in socket.Settings.BlackList)
        {
            if (!item.MatchesSettings(flip, socket.SessionInfo))
                continue;
            logger.LogError("Testflip doesn't match {UserId} {entry}", socket.UserId, BlacklistCommand.FormatEntry(item));
            break;
        }
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
            for (int i = 0; i < 3; i++)
            {
                if (flip.Auction.Start > DateTime.UtcNow - TimeSpan.FromSeconds(40))
                    break;
                flip.Auction.Start += +TimeSpan.FromSeconds(20);
            }
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
        var adapter = (socket as MinecraftSocket).ModAdapter;
        await adapter.SendFlip(flip);
        await trackTask;
        await Task.Delay(5000);
        if (flip.Context.GetValueOrDefault("match")?.Contains("shitflip") ?? false)
        {
            if (Random.Shared.NextDouble() < 0.2)
            {
                socket.Dialog(db => db.MsgLine("Hello there.")
                    .MsgLine($"The auction for {flip.Auction.ItemName} was overvalued on purpose because you were blacklisted as punishment for abuse.")
                    .MsgLine("Abuse is not cool.")
                    .MsgLine("When you matured a bit more you can start over on a fresh account that has no connection to the blacklisted one :)"));
            }
            return;
        }
        var betterEstimate = int.Parse(flip.Auction.Context?.GetValueOrDefault("ogEstimate") ?? "2200") * 9 / 10;
        socket.Dialog(db => db.MsgLine("Hello there,")
            .MsgLine("Sorry to disturb you, but we have noticed you didn't buy any flips in a while.")
            .MsgLine($"The auction for {flip.Auction.ItemName} was overvalued on purpose to check who would buy it.")
            .MsgLine($"It is NOT worth the {socket.FormatPrice(flip.Target)} coins you may want to sell at/under {socket.FormatPrice(betterEstimate)}.")
            .MsgLine("This is done to check if you try to trick our system by buying flips on a different account.")
            .MsgLine("As long as you didn't do any modifications/run non-official client versions you have nothing to worry about.")
            .MsgLine("If you aren't flipping you may want to turn off flips with /cofl flip. If you are trying to flip you may want to check your settings.")
            .MsgLine("We are sorry for the inconvenience and hope you have a great day."));
        await Task.Delay(5000);
        flip.Target = flip.Auction.StartingBid;
        if (adapter is AfVersionAdapter) // send correction
            await adapter.SendFlip(flip);
    }

    private static async Task<SaveAuction> FindAuction(IMinecraftSocket socket)
    {
        var oldestStart = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        foreach (var blocked in socket.TopBlocked.Where(b => b.Flip.Auction.Start > oldestStart)
                                    .OrderBy(b => b.Flip.Auction.StartingBid / b.Flip.DailyVolume))
        {
            if (blocked.Flip.Auction.StartingBid > 90_000_000)
                continue; // bit too pricey
            if (blocked.Reason != "minProfit" && blocked.Reason != "minVolume")
                continue;
            blocked.Flip.Auction.Context ??= new();
            blocked.Flip.Auction.Context["ogEstimate"] = blocked.Flip.TargetPrice.ToString();
            return blocked.Flip.Auction;
        }
        using var context = new HypixelContext();
        Activity.Current?.Log("From db");
        return await context.Auctions.OrderByDescending(a => a.Id).Include(a => a.Enchantments).Include(a => a.NbtData)
            .Take(350)
            .Where(a => a.HighestBidAmount == 0 && a.Start > oldestStart && a.StartingBid < 5_000_000).FirstOrDefaultAsync();
    }

    public class State
    {
        public string AuctionId;
    }

}