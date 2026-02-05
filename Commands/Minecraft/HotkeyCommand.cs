#nullable enable
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using System.Diagnostics;
using Coflnet.Sky.PlayerState.Client.Api;
using System;

namespace Coflnet.Sky.Commands.MC;

public class HotkeyCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var parts = Convert<string>(arguments).Split('|');
        if (parts[0] == "upload_item" && parts.Length == 1)
        {
            socket.Dialog(db => db.MsgLine("Please hold an item to use this command with"));
            return;
        }
        var auction = new SaveAuction();
        var nbt = NBT.FillDetails(auction, parts[1], true);
        var lore = string.Join("\n", NBT.GetLore(nbt));
        auction.Context["lore"] = lore;
        socket.SessionInfo.SelectedItem = auction;
        if (parts[0] == "openitemurl")
        {
            socket.Send(Response.Create("openurl", "https://sky.coflnet.com/item/" + auction.Tag));
            return;
        }
        if (parts[0] == "openitemmarket")
        {
            var isBazaar = await socket.GetService<Items.Client.Api.IItemsApi>().ItemItemTagGetAsync(auction.Tag) is var item && item.Flags!.Value.HasFlag(Items.Client.Model.ItemFlags.BAZAAR);
            var marketCommand = isBazaar ? $"/bz {auction.ItemName}" : $"/ahs {auction.ItemName}";
            socket.ExecuteCommand(marketCommand);
            return;
        }
        var inventoryTask = socket.GetService<IPlayerStateApi>().PlayerStatePlayerIdLastChestGetAsync(socket.SessionInfo.McName);
        if (parts[0] == "craftbreakdown")
        {
            int itemIndex = await GetItemIndex(auction, inventoryTask);
            socket.ExecuteCommand("/cofl craftbreakdown " + itemIndex);
            return;
        }
        if (socket.Settings.ModSettings.Hotkeys?.TryGetValue(parts[0], out var command) == true)
        {
            socket.ExecuteCommand(command);
            return;
        }
        socket.Dialog(db => db.MsgLine($"Item received {auction.ItemName}", null, auction.Context["lore"]));

        var sniperService = socket.GetService<ISniperClient>();
        var valuesTask = sniperService.GetPrices([auction]);
        Task<string> filterLinkTask = GetLinkWithFilters(socket, auction);
        var price = (await valuesTask).First();
        var instaSell = SniperClient.InstaSellPrice(price);
        var lbinAuction = await GetAuction(socket, price.Lbin.AuctionId);
        int index = await GetItemIndex(auction, inventoryTask);
        var filterLink = await filterLinkTask;

        var isInInventory = index != -1;
        var formattedInstasell = socket.FormatPrice(instaSell.Item1);
        socket.Dialog(db => db.MsgLine($"The value of this item is {McColorCodes.AQUA}{socket.FormatPrice(price.Median)}", null,
                $"Took into account these modifiers:\n{price.MedianKey}")
            .If(() => isInInventory, db => db.CoflCommandButton<LowballCommand>($"{McColorCodes.GREEN}Offer this item to a lowballer", $"offer {index}", "Click to offer this item to lowballers").LineBreak())
            .If(() => price.Lbin.AuctionId != 0, db => db
            .MsgLine($"Lowest bin sits at {McColorCodes.AQUA}{socket.FormatPrice(price.Lbin.Price)}", "/viewauction " + lbinAuction.Uuid, "click to open lbin on ah"))
            .Msg($"To sell quickly list at {McColorCodes.AQUA}{formattedInstasell}", $"copy:{formattedInstasell}", "click to copy")
            .MsgLine($"{McColorCodes.GRAY}[put into chat]", $"suggest:{formattedInstasell}", "click to put \nsuggestion into chat")
            .Button($"Open filter on website", filterLink, "Click to view on SkyCofl Website"));
    }

    private static async Task<int> GetItemIndex(SaveAuction auction, Task<System.Collections.Generic.List<PlayerState.Client.Model.Item>> inventoryTask)
    {
        var inventoryItems = LowballCommand.GetActualInventory(await inventoryTask);
        var auctionItemUUid = auction.FlatenedNBT.TryGetValue("uuid", out var uuidVal) ? uuidVal.Replace("-", "") : null;
        var index = inventoryItems
            .Select((i, idx) => new { i, idx })
            .Where(x => x.i.ExtraAttributes != null
                && x.i.ExtraAttributes.TryGetValue("uuid", out var uuid)
                && (uuid as string)?.Replace("-", "") == auctionItemUUid)
            .Select(x => x.idx)
            .DefaultIfEmpty(-1)
            .First();
        return index;
    }

    public static Task<string> GetLinkWithFilters(MinecraftSocket socket, SaveAuction auction)
    {
        var filterTask = RequestFilters(socket, auction);
        var filterLinkTask = GetLinkFromFilters(auction, filterTask);
        return filterLinkTask;
    }

    private static async Task<SaveAuction?> GetAuction(MinecraftSocket socket, long uid)
    {
        if (uid == 0)
            return null;
        var auctionClient = socket.GetService<Sky.Api.Client.Api.IAuctionsApi>();
        var auction = await auctionClient.ApiAuctionAuctionUuidGetWithHttpInfoAsync(AuctionService.Instance.GetUuid(uid));
        return JsonConvert.DeserializeObject<SaveAuction>(auction.RawContent);
    }

    private static Task<System.Collections.Generic.Dictionary<string, string>> RequestFilters(MinecraftSocket socket, SaveAuction auction)
    {
        var priceApi = socket.GetService<IPricesApi>();
        var represent = new Api.Client.Model.ItemRepresent();
        represent.ItemName = auction.ItemName;
        represent.ExtraAttributes = auction.NbtData.Data;
        represent.Enchantments = auction.Enchantments.GroupBy(t => t.Type).Select(g => g.First()).ToDictionary(e => e.Type.ToString(), e => (int)e.Level);
        represent.Count = auction.Count;
        represent.Description = auction.Context["lore"];
        represent.Tag = auction.Tag;

        represent.ExtraAttributes["modifier"] = auction.Reforge.ToString();
        represent.ExtraAttributes["tier"] = auction.Tier.ToString();
        Activity.Current.Log(JsonConvert.SerializeObject(represent));
        var filterTask = priceApi.ApiItemFiltersPostAsync(represent);
        return filterTask;
    }

    private static async Task<string> GetLinkFromFilters(SaveAuction auction, Task<System.Collections.Generic.Dictionary<string, string>> filterTask)
    {
        var filters = await filterTask;
        var filterString = JsonConvert.SerializeObject(filters);
        var filterStringBase64 = System.Convert.ToBase64String(Encoding.UTF8.GetBytes(filterString));
        var filterLink = $"https://sky.coflnet.com/item/{auction.Tag}?itemFilter={filterStringBase64}";
        return filterLink;
    }
}
