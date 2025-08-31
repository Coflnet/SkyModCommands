using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.ModCommands.Dialogs;
using Coflnet.Sky.ModCommands.Services;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Lists the cheapest items per exp for museum donations",
    "Honors tierd donations and tries to suggest the biggest",
    "exp donation first so you don't double spend.",
    "Also respects armor set requirements")]
public class CheapMuseumCommand : ReadOnlyListCommand<MuseumService.Cheapest>
{
    public override bool IsPublic => true;

    protected override void Format(MinecraftSocket socket, DialogBuilder db, MuseumService.Cheapest item)
    {
        if (item.Options == null)
        {
            db.MsgLine($" {item.ItemName} for {McColorCodes.AQUA}{item.PricePerExp} coins {McColorCodes.GRAY}per exp",
                        "/viewauction " + item.AuctuinUuid,
                            $"{McColorCodes.YELLOW}Click to view the auction\n" +
                            $"{McColorCodes.GRAY}That is {socket.FormatPrice(item.TotalPrice)} total for {item.TotalPrice/item.PricePerExp} exp");
            return;
        }
        if (item.AuctuinUuid.StartsWith("/cofl recipe "))
        {
            db.MsgLine($" {item.ItemName} for {McColorCodes.AQUA}{item.PricePerExp} coins {McColorCodes.GRAY}per exp",
                        item.AuctuinUuid,
                            $"{McColorCodes.YELLOW}Click to view the crafting recipe\n" +
                            $"{McColorCodes.GRAY}That is {socket.FormatPrice(item.TotalPrice)} total for {item.TotalPrice/item.PricePerExp} exp");
            return;
        }
        // armor sets
            db.MsgLine($" {item.ItemName} Set {McColorCodes.GRAY}for {McColorCodes.AQUA}{item.PricePerExp} coins {McColorCodes.GRAY}per exp",
                    null, "Buy all of the ones below to donate")
            .ForEach(item.Options, (db, option, i) => db.MsgLine($" {McColorCodes.AQUA}Item {i + 1}{McColorCodes.GRAY}: {McColorCodes.RESET}{option.name}", "/viewauction " + option.uuid, "Click to view the auction"));

    }

    protected override async Task<IEnumerable<MuseumService.Cheapest>> GetElements(MinecraftSocket socket, string val)
    {
        var service = socket.GetService<MuseumService>();
        var profileClient = socket.GetService<IProfileClient>();
        var tier = socket.SessionInfo.SessionTier;
        var amount = tier switch
        {
            AccountTier.PREMIUM_PLUS => 1000,
            AccountTier.PREMIUM => 500,
            _ => 100
        };
        var age = tier switch
        {
            > AccountTier.STARTER_PREMIUM => DateTime.UtcNow - TimeSpan.FromMinutes(2),
            AccountTier.STARTER_PREMIUM => DateTime.UtcNow - TimeSpan.FromMinutes(5),
            _ => DateTime.UtcNow - TimeSpan.FromHours(2)
        };
        socket.Dialog(db => db.MsgLine("Fetching what you already donated...", null, "This updates every 5 minutes on premium or higher, otherwise every ~3 hours")
            .If(() => tier == AccountTier.NONE, db => db.CoflCommand<PurchaseCommand>(
                    $"\nThese are cached for 2 hours because you don't use a paid tier. "
                + $"\n{McColorCodes.GRAY}For financial supporters the cache time is reduced to 2-5 minutes.", "", "Click to upgrade")));
        if (socket.SessionInfo.ProfileId != null && socket.SessionInfo.ProfileId.Length < 32)
        {
            var name = socket.SessionInfo.ProfileId;
            // get the profile id from name
            var profile = await profileClient.GetProfiles(socket.SessionInfo.McUuid);
            if (profile.TryGetValue(name, out var profileId))
            {
                socket.SessionInfo.ProfileId = profileId;
                socket.SendMessage($"Profile id found: {profileId} for {name}");
            }
        }
        var alreadDonated = await profileClient.GetAlreadyDonatedToMuseum(socket.SessionInfo.McUuid, socket.SessionInfo.ProfileId ?? "current", age);
        
        // Get recent donations from chat messages
        var museumDonationService = socket.GetService<IMuseumDonationService>();
        var recentDonations = museumDonationService.GetRecentDonations(socket.SessionInfo.McName);
        
        // Combine API donations with recent chat-based donations
        var combinedDonations = new HashSet<string>(alreadDonated);
        foreach (var recentDonation in recentDonations)
        {
            combinedDonations.Add(recentDonation);
        }
        
        if (combinedDonations.Count > 0)
            socket.Dialog(db => db.MsgLine($"Skipping {combinedDonations.Count} items you already donated (including {recentDonations.Count} recent donations from chat)"));
        else
        {
            socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}No donated items found, do you have museum api on?"));
            await Task.Delay(2000);
        }
        Activity.Current.Log("Already donated: " + combinedDonations.Count)
            .Log($"Age: {age}")
            .Log($"Amount: {amount}")
            .Log($"Recent donations: {recentDonations.Count}")
            .Log(string.Join(", ", combinedDonations));
        if (val == "craft")
        {
            socket.Dialog(db => db.MsgLine("Showing only craftable items"));
            var namesTask = socket.GetService<IItemsApi>().ItemNamesGetAsync();
            var craftable = await service.GetBestCraftableMuseumItems(combinedDonations, socket.SessionInfo.McUuid, socket.SessionInfo.ProfileId ?? "current", amount);
            var namesDictionary = (await namesTask).ToDictionary(i => i.Tag, i => i.Name);
            return craftable.Select(c => new MuseumService.Cheapest
            {
                ItemName = c.ItemName,
                PricePerExp = c.PricePerExp,
                TotalPrice = c.CraftCost,
                AuctuinUuid = $"/cofl recipe {c.ItemId}",
                Options = c.Ingredients.Select(i => (i.ItemId, namesDictionary.GetValueOrDefault(i.ItemId) + $" x{i.Count}")).ToArray()
            });
        }

        return await service.GetBestMuseumPrices(combinedDonations, amount);
    }

    protected override DialogBuilder PrintResult(MinecraftSocket socket, string title, int page, IEnumerable<MuseumService.Cheapest> toDisplay, int totalPages)
    {
        return base.PrintResult(socket, title, page, toDisplay, totalPages)
            .If(() => socket.SessionInfo.SessionTier < AccountTier.PREMIUM_PLUS && page > 1,
                db => db.CoflCommand<PurchaseCommand>($"With {McColorCodes.GOLD}prem+{McColorCodes.RESET} you can see the {McColorCodes.AQUA}top 1000", "premium_plus", "Click to upgrade"));
    }

    protected override string GetId(MuseumService.Cheapest elem)
    {
        return elem.ItemName;
    }
}