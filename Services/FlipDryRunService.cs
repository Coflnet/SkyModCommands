using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.FlipTracker.Client.Api;
using FinderContext = Coflnet.Sky.FlipTracker.Client.Model.FinderContext;
using Flip = Coflnet.Sky.FlipTracker.Client.Model.Flip;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using static Coflnet.Sky.ModCommands.Services.BlockedService;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>
/// Loads the data the dry run needs, separated so <see cref="FlipDryRunService"/> can be tested without network
/// </summary>
public interface IFlipDryRunDataSource
{
    /// <summary>Flip settings of the user or null if none are stored</summary>
    Task<FlipSettings> GetSettings(string userId);
    /// <summary>Auction including enchantments and nbt or null if unknown</summary>
    Task<SaveAuction> GetAuction(string auctionUuid);
    /// <summary>Recorded finder contexts of the tracker</summary>
    Task<List<FinderContext>> GetContexts(SaveAuction auction);
    /// <summary>Recorded per finder target prices of the tracker</summary>
    Task<List<Flip>> GetEstimates(SaveAuction auction);
    /// <summary>Current estimate of the sniper</summary>
    Task<Sniper.Client.Model.PriceEstimate> GetSniperEstimate(SaveAuction auction);
}

public class FlipDryRunDataSource : IFlipDryRunDataSource
{
    private readonly SettingsService settingsService;
    private readonly ITrackerApi trackerApi;
    private readonly ISniperClient sniperClient;

    public FlipDryRunDataSource(SettingsService settingsService, ITrackerApi trackerApi, ISniperClient sniperClient)
    {
        this.settingsService = settingsService;
        this.trackerApi = trackerApi;
        this.sniperClient = sniperClient;
    }

    public Task<FlipSettings> GetSettings(string userId)
        => settingsService.GetCurrentValue<FlipSettings>(userId, "flipSettings", () => null);

    public Task<SaveAuction> GetAuction(string auctionUuid)
        => AuctionService.Instance.GetAuctionAsync(auctionUuid, au => au.Include(a => a.Enchantments).Include(a => a.NbtData));

    public Task<List<FinderContext>> GetContexts(SaveAuction auction) => trackerApi.GetFlipAsync(auction.Uuid);

    public Task<List<Flip>> GetEstimates(SaveAuction auction) => trackerApi.GetFlipsOfAuctionAsync(auction.UId);

    public async Task<Sniper.Client.Model.PriceEstimate> GetSniperEstimate(SaveAuction auction)
        => (await sniperClient.GetPrices([auction])).FirstOrDefault();
}

/// <summary>
/// Replays the flip settings of a user against a (past) flip to find out why it was or would not be sent
/// </summary>
public class FlipDryRunService
{
    private readonly IFlipDryRunDataSource dataSource;
    private readonly IBlockedService blockedService;
    private readonly ILogger<FlipDryRunService> logger;

    public FlipDryRunService(IFlipDryRunDataSource dataSource, IBlockedService blockedService, ILogger<FlipDryRunService> logger)
    {
        this.dataSource = dataSource;
        this.blockedService = blockedService;
        this.logger = logger;
    }

    /// <summary>
    /// The tracker client enum spells some finders without underscores (SNIPERMEDIAN), so names are compared ignoring them
    /// </summary>
    internal static LowPricedAuction.FinderType? MapFinder(object trackerFinder)
    {
        var name = trackerFinder?.ToString()?.Replace("_", "");
        if (string.IsNullOrEmpty(name))
            return null;
        foreach (var value in Enum.GetValues<LowPricedAuction.FinderType>())
            if (string.Equals(value.ToString().Replace("_", ""), name, StringComparison.OrdinalIgnoreCase))
                return value;
        return null;
    }

    /// <summary>
    /// Rebuilds the flip as it was sent to users from the tracker data.
    /// Without any recorded finder context the finder is USER, the target the starting bid and the volume 1.
    /// </summary>
    /// <param name="auction">the auction</param>
    /// <param name="contexts">finder contexts recorded by the tracker, may be null or empty</param>
    /// <param name="estimates">per finder target prices recorded by the tracker, may be null</param>
    /// <param name="preferred">finder to prefer, falls back to the first context</param>
    internal static LowPricedAuction Reconstruct(SaveAuction auction, IEnumerable<FinderContext> contexts, IEnumerable<Flip> estimates, LowPricedAuction.FinderType? preferred = null)
    {
        var contextList = contexts?.Where(c => c != null).ToList() ?? [];
        var toTest = preferred.HasValue
            ? contextList.FirstOrDefault(c => MapFinder(c.Finder) == preferred.Value)
            : null;
        toTest ??= contextList.FirstOrDefault();
        if (toTest == null)
        {
            return new LowPricedAuction()
            {
                Auction = auction,
                TargetPrice = auction.StartingBid,
                DailyVolume = 1,
                AdditionalProps = new(),
                Finder = LowPricedAuction.FinderType.USER
            };
        }
        float volumeEstimate = 1;
        var context = toTest.Context ?? new();
        if (context.TryGetValue("oldRef", out var oldRef) && context.TryGetValue("refCount", out var refCount)
            && int.TryParse(refCount, out var refCountValue) && float.TryParse(oldRef, out var oldRefValue) && oldRefValue != 0)
        {
            volumeEstimate = refCountValue / oldRefValue + 1f;
        }
        var targets = estimates?.Where(e => e != null && e.FinderType == toTest.Finder).Select(e => (double)e.TargetPrice).ToList() ?? [];
        var finder = MapFinder(toTest.Finder) ?? LowPricedAuction.FinderType.USER;
        return new LowPricedAuction()
        {
            Auction = auction,
            TargetPrice = targets.Count > 0 ? (long)targets.Average() : auction.StartingBid,
            DailyVolume = volumeEstimate,
            AdditionalProps = new Dictionary<string, string>(context),
            Finder = finder
        };
    }

    /// <summary>
    /// Evaluates the settings of a user against a stored auction
    /// </summary>
    /// <param name="userId">Coflnet account id</param>
    /// <param name="auctionUuid">auction to replay</param>
    /// <param name="finder">finder to test, defaults to the first one the tracker recorded</param>
    /// <returns>the result or null if the auction doesn't exist</returns>
    public async Task<WouldMatchResult> Evaluate(string userId, string auctionUuid, LowPricedAuction.FinderType? finder = null)
    {
        var auction = await dataSource.GetAuction(auctionUuid);
        if (auction == null)
            return null;
        var warnings = new List<string>();
        List<FinderContext> contexts = null;
        List<Flip> estimates = null;
        try
        {
            contexts = await dataSource.GetContexts(auction);
            estimates = await dataSource.GetEstimates(auction);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load tracker data for {AuctionUuid}", auctionUuid);
            warnings.Add("tracker data unavailable: " + e.Message);
        }

        LowPricedAuction flip;
        string source;
        if (contexts != null && contexts.Count > 0)
        {
            flip = Reconstruct(auction, contexts, estimates, finder);
            source = "finder_context";
            if (finder.HasValue && flip.Finder != finder.Value)
                warnings.Add($"tracker has no record of finder {finder.Value}, used {flip.Finder}");
        }
        else
        {
            flip = await FromSniperEstimate(auction, finder, warnings);
            if (flip != null)
            {
                source = "sniper_estimate_now";
                warnings.Add("the tracker has no finder context for this auction, the sniper estimate is current, not the one at the time of the flip");
            }
            else
            {
                flip = Reconstruct(auction, null, null, finder);
                source = "none";
                warnings.Add("no finder context and no sniper estimate, target is the starting bid");
            }
        }
        return await Evaluate(userId, flip, source, warnings);
    }

    /// <summary>
    /// Evaluates the settings of a user against a given flip
    /// </summary>
    public Task<WouldMatchResult> Evaluate(string userId, LowPricedAuction posted)
        => Evaluate(userId, posted, "posted", new());

    private async Task<LowPricedAuction> FromSniperEstimate(SaveAuction auction, LowPricedAuction.FinderType? finder, List<string> warnings)
    {
        try
        {
            var estimate = await dataSource.GetSniperEstimate(auction);
            if (estimate == null)
                return null;
            var chosen = finder ?? LowPricedAuction.FinderType.SNIPER_MEDIAN;
            var props = new Dictionary<string, string>();
            var target = estimate.Median;
            if (chosen == LowPricedAuction.FinderType.SNIPER)
            {
                // the sniper finder sends the lowest bin as target and keeps the median as mVal
                props["mVal"] = estimate.Median.ToString();
                target = estimate.Lbin?.Price ?? estimate.Median;
            }
            return new LowPricedAuction()
            {
                Auction = auction,
                TargetPrice = target,
                DailyVolume = estimate.Volume,
                AdditionalProps = props,
                Finder = chosen
            };
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load sniper estimate for {AuctionUuid}", auction.Uuid);
            warnings.Add("sniper estimate unavailable: " + e.Message);
            return null;
        }
    }

    private async Task<WouldMatchResult> Evaluate(string userId, LowPricedAuction flip, string source, List<string> warnings)
    {
        var result = new WouldMatchResult
        {
            UserId = userId,
            AuctionUuid = flip.Auction?.Uuid,
            Finder = flip.Finder,
            TargetPrice = flip.TargetPrice,
            StartingBid = flip.Auction?.StartingBid ?? 0,
            Volume = flip.DailyVolume,
            FlipSource = source,
            Warnings = warnings
        };
        try
        {
            if (Guid.TryParse(flip.Auction?.Uuid, out var uuid))
                result.RecordedBlockedReasons = (await blockedService.GetBlockedReasons(userId, uuid))
                    .Select(b => new BlockedReasonView(b)).ToList();
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Could not load recorded blocked reasons for {UserId}", userId);
            warnings.Add("recorded blocked reasons unavailable: " + e.Message);
        }

        var settings = await dataSource.GetSettings(userId);
        if (settings == null)
        {
            warnings.Add("no settings stored");
            return result;
        }
        result.SettingsSummary = SettingsSummary.From(settings);

        // compilation is synchronous and doesn't need an Activity, doing it here makes sure
        // the fresh settings never report "filters currently compiling"
        await Task.Run(settings.RecompileMatchers);
        if (!settings.IsCompiled)
            warnings.Add("filters could not be compiled, the reason may be unreliable");

        var instance = FlipperService.LowPriceToFlip(flip);
        result.EstimatedProfit = instance.Profit;
        result.FinderBlocked = settings.IsFinderBlocked(flip.Finder);
        try
        {
            var match = settings.MatchesSettings(instance);
            result.Matches = match.Item1;
            result.Reason = match.Item2;
        }
        catch (Exception e)
        {
            result.Matches = false;
            result.Reason = "Error " + e.Message;
        }
        if (settings.BasedOnLBin)
            warnings.Add("not evaluated: lbin based profit uses the lowest bin at send time");
        AddNotEvaluated(warnings, settings, flip);
        return result;
    }

    private static void AddNotEvaluated(List<string> warnings, FlipSettings settings, LowPricedAuction flip)
    {
        warnings.Add("not evaluated: purse check (purse at the time is unknown)");
        if (settings.BlockHighCompetitionFlips)
            warnings.Add("not evaluated: high competition (needs the delay exempt list, flips with profit >= 8M are exempt from it, below 3.5M or low volume it doesn't apply)");
        warnings.Add("not evaluated: listing spam and spam limits");
        warnings.Add("not evaluated: already sent");
        warnings.Add("not evaluated: sold (auction state at send time)");
        warnings.Add("not evaluated: delay (the user's delay tier)");
        if (flip.Finder == LowPricedAuction.FinderType.Rust)
            warnings.Add("not evaluated: rust addon ownership");
    }
}

/// <summary>
/// Outcome of replaying the settings of a user against a flip
/// </summary>
public class WouldMatchResult
{
    [JsonProperty("userId")]
    public string UserId { get; set; }
    [JsonProperty("auctionUuid")]
    public string AuctionUuid { get; set; }
    /// <summary>true if the settings would let the flip through</summary>
    [JsonProperty("matches")]
    public bool Matches { get; set; }
    /// <summary>reason given by the settings, same strings as in the blocked reasons. Eg minProfit, finder FLIPPER, blacklist ...</summary>
    [JsonProperty("reason")]
    public string Reason { get; set; }
    [JsonProperty("finderBlocked")]
    public bool FinderBlocked { get; set; }
    [JsonProperty("finder")]
    [JsonConverter(typeof(StringEnumConverter))]
    public LowPricedAuction.FinderType Finder { get; set; }
    [JsonProperty("targetPrice")]
    public long TargetPrice { get; set; }
    [JsonProperty("startingBid")]
    public long StartingBid { get; set; }
    /// <summary>profit after auction house fees, what the min profit setting is compared to</summary>
    [JsonProperty("estimatedProfit")]
    public long EstimatedProfit { get; set; }
    [JsonProperty("volume")]
    public float Volume { get; set; }
    /// <summary>finder_context, sniper_estimate_now, posted or none (nothing available, target is the starting bid)</summary>
    [JsonProperty("flipSource")]
    public string FlipSource { get; set; }
    [JsonProperty("settingsSummary")]
    public SettingsSummary SettingsSummary { get; set; }
    [JsonProperty("warnings")]
    public List<string> Warnings { get; set; } = new();
    [JsonProperty("recordedBlockedReasons")]
    public List<BlockedReasonView> RecordedBlockedReasons { get; set; } = new();
}

/// <summary>
/// Readable form of <see cref="BlockedReason"/> with the finder as name
/// </summary>
public class BlockedReasonView
{
    public BlockedReasonView() { }
    public BlockedReasonView(BlockedReason reason)
    {
        UserId = reason.UserId;
        AuctionUuid = reason.AuctionUuid;
        Finder = reason.FinderType;
        BlockedAt = reason.BlockedAt;
        Reason = reason.Reason;
        TargetPrice = reason.TargetPrice;
        StartingBid = reason.StartingBid;
    }
    [JsonProperty("userId")]
    public string UserId { get; set; }
    [JsonProperty("auctionUuid")]
    public Guid AuctionUuid { get; set; }
    [JsonProperty("finder")]
    [JsonConverter(typeof(StringEnumConverter))]
    public LowPricedAuction.FinderType Finder { get; set; }
    [JsonProperty("blockedAt")]
    public DateTime BlockedAt { get; set; }
    [JsonProperty("reason")]
    public string Reason { get; set; }
    [JsonProperty("targetPrice")]
    public long TargetPrice { get; set; }
    [JsonProperty("startingBid")]
    public long StartingBid { get; set; }
}

public class SettingsSummary
{
    [JsonProperty("minProfit")]
    public long MinProfit { get; set; }
    [JsonProperty("minProfitPercent")]
    public int MinProfitPercent { get; set; }
    [JsonProperty("minVolume")]
    public double MinVolume { get; set; }
    [JsonProperty("maxCost")]
    public long MaxCost { get; set; }
    [JsonProperty("allowedFinders")]
    [JsonConverter(typeof(StringEnumConverter))]
    public LowPricedAuction.FinderType AllowedFinders { get; set; }
    [JsonProperty("onlyBin")]
    public bool OnlyBin { get; set; }
    [JsonProperty("fastMode")]
    public bool FastMode { get; set; }
    [JsonProperty("blockHighCompetition")]
    public bool BlockHighCompetition { get; set; }
    [JsonProperty("blacklistCount")]
    public int BlacklistCount { get; set; }
    [JsonProperty("whitelistCount")]
    public int WhitelistCount { get; set; }
    [JsonProperty("forcedBlacklistCount")]
    public int ForcedBlacklistCount { get; set; }

    public static SettingsSummary From(FlipSettings settings) => new()
    {
        MinProfit = settings.MinProfit,
        MinProfitPercent = settings.MinProfitPercent,
        MinVolume = settings.MinVolume,
        MaxCost = settings.MaxCost,
        AllowedFinders = settings.AllowedFinders,
        OnlyBin = settings.OnlyBin,
        FastMode = settings.FastMode,
        BlockHighCompetition = settings.BlockHighCompetitionFlips,
        BlacklistCount = settings.BlackList?.Count ?? 0,
        WhitelistCount = settings.WhiteList?.Count ?? 0,
        ForcedBlacklistCount = settings.GetForceBlacklist()?.Count ?? 0
    };
}
