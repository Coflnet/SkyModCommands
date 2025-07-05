using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using System.Diagnostics;

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
        socket.Dialog(db => db.MsgLine($"Item received {auction.ItemName}", null, auction.Context["lore"]));

        var sniperService = socket.GetService<ISniperClient>();
        var filterTask = RequestFilters(socket, auction);
        var values = await sniperService.GetPrices([auction]);
        string filterLink = await GetLinkFromFilters(auction, filterTask);
        var price = values.First();
        var instaSell = SniperClient.InstaSellPrice(price);
        var auctionId = await GetAuction(socket, price.Lbin.AuctionId);
        var formattedInstasell = socket.FormatPrice(instaSell.Item1);
        socket.Dialog(db => db.MsgLine($"The value of this item is {McColorCodes.AQUA}{socket.FormatPrice(price.Median)}", null,
                        $"Took into account these modifiers:\n{price.MedianKey}")
            .If(() => price.Lbin.AuctionId != 0, db => db
                .MsgLine($"Lowest bin sits at {McColorCodes.AQUA}{socket.FormatPrice(price.Lbin.Price)}", "/viewauction " + auctionId, "click to open lbin on ah"))
            .Msg($"To sell quickly list at {McColorCodes.AQUA}{formattedInstasell}", $"copy:{formattedInstasell}", "click to copy")
                .MsgLine($"{McColorCodes.GRAY}[put into chat]", $"suggest:{formattedInstasell}", "click to put \nsuggestion into chat")
                .Button($"Open filter on website", filterLink, "Click to view on SkyCofl Website"));
    }

    private static async Task<SaveAuction?> GetAuction(MinecraftSocket socket, long uid)
    {
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
