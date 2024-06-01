using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using Coflnet.Sky.Sniper.Client.Api;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription("Lists the cheapest auctions for a given attribute combination", "cheapattrib <attrib1> <attrib2>")]
public class CheapAttribCommand : McCommand
{
    public override bool IsPublic => true;
    private static Dictionary<string, string> aliases = new() {
        { "vitality", "mending" },
        { "mana_regen", "mana_regeneration" }
    };
    static Dictionary<string, string> map;
    static CheapAttribCommand()
    {
        // validate that values exist
        foreach (var item in aliases)
        {
            if (!Constants.AttributeKeys.Contains(item.Value))
                throw new System.Exception($"The alias {item.Value} for {item.Key} is not a known attribute");
        }
        map = Constants.AttributeKeys.SelectMany(k => AltName(k).Select(a => (Key: a, Value: k)))
            .Concat(aliases.Select(a => (a.Key, a.Value)))
            .ToDictionary(k => k.Key, v => v.Value);
    }
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
       // if (!await socket.ReguirePremPlus())
       //     return;
        var attribNames = arguments.Trim('"').Split(' ');
        if (attribNames.Length != 3)
            throw new CoflnetException("invalid_arguments", "Invalid usage.\nuse <item_tag> <attrib_1> <attrib_2>\nPlease provide two attribute names without spaces (you can use _ or ommit it) eg manapool mana_regeneration");

        var tag = attribNames[0].ToUpper();
        if(ItemDetails.Instance.GetItemIdForTag(tag, false) == 0)
        {
            var itemClient = socket.GetService<IItemsApi>();
            var item = await itemClient.ItemsSearchTermGetAsync(tag.ToLower());
            if (item.Count == 0)
                throw new CoflnetException("invalid_item", $"The item {tag} is not known, please check for typos or report adding an alias");
            tag = item.First().Tag;
        }
        Console.WriteLine($"Using tag {tag}");
        var mapped = attribNames.Skip(1).Select(MapAttribute).ToArray();
        var attribApi = socket.GetService<IAttributeApi>();
        var result = await attribApi.ApiAttributeComboLeftAttribRightAttribGetAsync(mapped[0], mapped[1], tag);
        var grouped = result.GroupBy(r => r.Tag.Split('_').First()).OrderByDescending(g => g.Key);
        foreach (var group in grouped)
        {
            socket.Dialog(db => db.MsgLine($"§6{group.Key}")
                .ForEach(group.OrderBy(r => r.Tag), (db, r) => db
                    .CoflCommand<OpenUidAuctionCommand>(
                        $"§7{FormatName(r.Tag)} §6{(r.Price < 0 ? "none found" : socket.FormatPrice(r.Price))}", r.AuctionUid,
                        $"{McColorCodes.AQUA}try to open {FormatName(r.Tag)} in ah\n{McColorCodes.GRAY}execute command again if expired")
                    .If(() => !r.Tag.EndsWith("LEGGINGS"), db => db.LineBreak())));
        }
    }

    public static string MapAttribute(string a)
    {
        if (!map.ContainsKey(a))
            throw new CoflnetException("invalid_attribute", $"The attribute {a} is not known, please check for typos or report adding an alias");
        return map[a];
    }

    private string FormatName(string attribname)
    {
        var words = attribname.Split('_');
        return string.Join(" ", words.Select(w => w.First().ToString().ToUpper() + w.Substring(1).ToLower()));
    }

    static IEnumerable<string> AltName(string attribname)
    {
        yield return attribname;
        if (attribname.Contains('_'))
            yield return attribname.Replace("_", "");
    }
}

public static class CommonDialogExtension
{
    public static async Task<bool> ReguirePremPlus(this IMinecraftSocket socket)
    {
        if (await socket.UserAccountTier() >= Shared.AccountTier.PREMIUM_PLUS)
        {
            return true;
        }
        socket.Dialog(db => db.CoflCommand<PurchaseCommand>(
            $"{McColorCodes.RED}{McColorCodes.BOLD}ABORTED\n"
            + $"{McColorCodes.RED}You need to be a premium plus user to use this command"
            + $"{McColorCodes.YELLOW}\n[Click to purchase prem+]",
            "premium_plus", $"Click to purchase prem+"));
        return false;
    }
}
