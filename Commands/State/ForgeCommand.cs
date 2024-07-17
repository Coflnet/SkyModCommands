using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Coflnet.Sky.ModCommands.Dialogs;

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
            .MsgLine($"{McColorCodes.GRAY}Duration: {McColorCodes.AQUA}{socket.FormatPrice(TimeSpan.FromSeconds(elem.Duration).TotalHours)} hours");
    }

    protected override async Task<IEnumerable<ForgeFlip>> GetElements(MinecraftSocket socket, string val)
    {
        var forgeApi = socket.GetService<IForgeApi>();
        var profileApi = socket.GetService<IProfileClient>();
        var forgeUnlockedTask = profileApi.GetForgeData(socket.UserId, "current");
        var forgeFlips = await forgeApi.ForgeAllGetAsync();
        var unlocked = await forgeUnlockedTask;
        var result = new List<ForgeFlip>();
        foreach (var item in forgeFlips)
        {
            if (unlocked.HotMLevel < item.RequiredHotMLevel)
                continue;
            if (unlocked.QuickForgeSpeed != 0)
            {
                item.Duration = (int)((float)item.Duration * unlocked.QuickForgeSpeed);
            }
            result.Add(item);
        }
        return result;
    }

    protected override string GetId(ForgeFlip elem)
    {
        throw new System.NotImplementedException();
    }

    protected override void PrintSumary(MinecraftSocket socket, DialogBuilder db, IEnumerable<ForgeFlip> elements)
    {
        db.MsgLine("New Command, looking for feedback :)");
    }

    protected override string Title => $"Most profitable Forge Flips you can do";

    public override bool IsPublic => true;

    protected override string NoMatchText => "No forge flips found that you can do, did you unlock it?";
}
