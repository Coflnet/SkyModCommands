using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Displays forge flips you can do based on hotM level",
    "Recognizes your quick forge level and adjusts time accordingly")]
public class ForgeCommand : ReadOnlyListCommand<ForgeFlip>
{
    protected override void Format(MinecraftSocket socket, DialogBuilder db, ForgeFlip elem)
    {
        var hover = "needed items:\n";
        foreach (var item in elem.CraftData.Ingredients)
        {
            hover += $"{item.ItemId} {McColorCodes.AQUA}x{item.Count}";
            if (item.Cost >= 20_000_000_000)
                hover += McColorCodes.RED + " (not purchaseable)";
            else
                hover += $" {McColorCodes.GRAY}cost {McColorCodes.GOLD}{socket.FormatPrice(item.Cost)}";

            hover += "\n";
        }
        var purchaseText = $"{McColorCodes.DARK_GRAY}???";
        if (elem.CraftData.CraftCost < 20_000_000_000)
            purchaseText = $"{McColorCodes.RED}{socket.FormatPrice(elem.CraftData.CraftCost)}";
        var estimatedProfit = elem.CraftData.SellPrice * 98 / 100 - elem.CraftData.CraftCost;
        var profit = $"{McColorCodes.GRAY}({McColorCodes.AQUA}+{socket.FormatPrice(estimatedProfit)} {McColorCodes.GRAY})";
        db.MsgLine($"{elem.CraftData.ItemName} {purchaseText}{McColorCodes.GRAY}->{McColorCodes.GREEN}{socket.FormatPrice(elem.CraftData.SellPrice)} {profit}", null, hover)
            .MsgLine($"{McColorCodes.GRAY}Profit: {McColorCodes.GOLD}{socket.FormatPrice(elem.ProfitPerHour)} {McColorCodes.GRAY}per hour ({elem.CraftData.Volume} volume)")
            .MsgLine($"{McColorCodes.GRAY}Duration: {McColorCodes.AQUA}{socket.formatProvider.FormatTime(TimeSpan.FromSeconds(elem.Duration))}");
    }

    protected override async Task<IEnumerable<ForgeFlip>> GetElements(MinecraftSocket socket, string val)
    {
        return await GetPossibleFlips(socket);
    }

    public static async Task<IEnumerable<ForgeFlip>> GetPossibleFlips(MinecraftSocket socket)
    {
        var forgeApi = socket.GetService<IForgeApi>();
        var stateApi = socket.GetService<IPlayerStateApi>();
        var profileApi = socket.GetService<IProfileClient>();
        var extractedTask = stateApi.PlayerStatePlayerIdExtractedGetAsync(socket.SessionInfo.McName);
        var forgeUnlockedTask = profileApi.GetForgeData(socket.SessionInfo.McUuid, "current");
        var forgeFlips = await forgeApi.GetAllForgeAsync();
        var unlocked = await forgeUnlockedTask;
        var extractedInfo = await extractedTask;
        if (extractedInfo.HeartOfTheMountain?.Tier > 0)
            unlocked.HotMLevel = extractedInfo.HeartOfTheMountain.Tier;
        var result = new List<ForgeFlip>();
        foreach (var item in forgeFlips)
        {
            if (unlocked.HotMLevel < item.RequiredHotMLevel)
                continue;
            if (item.ProfitPerHour <= 0)
                continue;
            if (unlocked.QuickForgeSpeed != 0)
            {
                item.Duration = (int)((float)item.Duration * unlocked.QuickForgeSpeed);
            }
            if (item.ProfitPerHour > 1_000_000_000) // probably a calculation error, use daily volume instead
                item.ProfitPerHour = (item.CraftData.SellPrice - item.CraftData.CraftCost) * item.CraftData.Volume;
            result.Add(item);
        }
        return result.OrderByDescending(r => r.ProfitPerHour);
    }

    protected override string GetId(ForgeFlip elem)
    {
        return elem.CraftData.ItemId + elem.CraftData.ItemName;
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<ForgeFlip> elements, IEnumerable<ForgeFlip> toDisplay)
    {
        db.MsgLine("New Command, looking for feedback :)");
    }

    protected override IEnumerable<ForgeFlip> FilterElementsForProfile(MinecraftSocket socket, IEnumerable<ForgeFlip> elements)
    {
        var filtered = elements.Where(f => f.CraftData.CraftCost < socket.SessionInfo.Purse).ToList();
        if (filtered.Count != elements.Count())
            socket.Dialog(db => db.MsgLine($"Filtered {elements.Count() - filtered.Count} forges that cost more than your purse ({socket.FormatPrice(socket.SessionInfo.Purse)})"));
        return filtered;
    }

    protected override string Title => $"Most profitable Forge Flips you can do";

    public override bool IsPublic => true;
    protected override int PageSize => 5;

    protected override string NoMatchText => "No forge flips found that you can do, did you unlock it?";
}
