using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.ModCommands.Dialogs;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Shows the most profitable greenhouse crops to buy from bazaar for copper",
    "Filters based on your discovered greenhouse crops")]
public class CopperCommand : ReadOnlyListCommand<CopperFlip>
{
    protected override string Title => "Best Copper per Coin from Bazaar";
    public override bool IsPublic => true;
    protected override int PageSize => 10;
    protected override string NoMatchText => "No copper flips found. Have you discovered any greenhouse crops?";

    public CopperCommand()
    {
        sorters.Add("cost", el => el.OrderByDescending(f => f.TotalCost));
        sorters.Add("yield", el => el.OrderByDescending(f => f.CopperYield));
        sorters.Add("volume", el => el.OrderByDescending(f => f.Volume));
    }

    protected override async Task<IEnumerable<CopperFlip>> GetElements(MinecraftSocket socket, string val)
    {
        using var activity = socket.GetService<ActivitySource>().StartActivity("CopperCommand.GetElements");
        try
        {
            var bazaarApi = socket.GetService<IBazaarFlipperApi>();
            var profileApi = socket.GetService<IProfileClient>();

            var copperTask = bazaarApi.CopperGetAsync();
            var greenhouseTask = profileApi.GetGreenhouseData(socket.SessionInfo.McUuid, "current");

            var allFlips = await copperTask;
            var greenhouse = await greenhouseTask;

            activity?.AddTag("copper.total_flips", allFlips.Count);
            activity?.AddTag("copper.discovered_crops", greenhouse.DiscoveredCrops.Count);

            var discoveredSet = new HashSet<string>(greenhouse.DiscoveredCrops, StringComparer.OrdinalIgnoreCase);
            var filtered = allFlips
                .Where(f => discoveredSet.Contains(f.ItemTag))
                .OrderByDescending(f => f.CopperPerCoin)
                .ToList();

            activity?.AddTag("copper.filtered_flips", filtered.Count);
            return filtered;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}Error loading copper data: {ex.Message}"));
            throw;
        }
    }

    protected override void Format(MinecraftSocket socket, DialogBuilder db, CopperFlip elem)
    {
        var copperPerCoin = elem.CopperPerCoin;
        db.MsgLine(
            $"{McColorCodes.DARK_GREEN}{elem.ItemTag.Replace("_", " ")} "
            + $"{McColorCodes.GOLD}{copperPerCoin:F4} copper/coin",
            null,
            $"\n{McColorCodes.GRAY}Buy price: {McColorCodes.GOLD}{socket.FormatPrice(elem.BuyPrice)}"
            + $"\n{McColorCodes.GRAY}Analyze cost: {McColorCodes.GOLD}{socket.FormatPrice(elem.AnalyzeCost)}"
            + $"\n{McColorCodes.GRAY}Total cost: {McColorCodes.GOLD}{socket.FormatPrice(elem.TotalCost)}"
            + $"\n{McColorCodes.GRAY}Copper yield: {McColorCodes.GREEN}{elem.CopperYield}"
            + $"\n{McColorCodes.GRAY}Volume: {McColorCodes.AQUA}{elem.Volume}");
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<CopperFlip> elements, IEnumerable<CopperFlip> toDisplay)
    {
        var best = elements.FirstOrDefault();
        if (best != null)
        {
            db.MsgLine($"{McColorCodes.GRAY}Best option: {McColorCodes.DARK_GREEN}{best.ItemTag.Replace("_", " ")} "
                + $"{McColorCodes.GRAY}at {McColorCodes.GOLD}{best.CopperPerCoin:F4} copper/coin");
        }
    }

    protected override string GetId(CopperFlip elem) => elem.ItemTag;
}
