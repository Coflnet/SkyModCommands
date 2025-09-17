using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.PlayerState.Client.Api;
using FluentAssertions.Extensions;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Displays forge flips you can do based on hotM level",
    "Recognizes your quick forge level and adjusts time accordingly")]
public class ForgeCommand : ReadOnlyListCommand<ForgeFlip>
{
    public ForgeCommand()
    {
        sorters.Add("profit", (el) => el.OrderByDescending(f => f.ProfitPerHour * f.Duration));
    }
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
        var forgeService = socket.GetService<ForgeFlipService>();
        return await forgeService.GetForgeFlips(socket.SessionInfo.McName, socket.SessionInfo.McUuid);
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
