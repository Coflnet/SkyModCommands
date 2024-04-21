using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
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
        if (await socket.UserAccountTier() < Shared.AccountTier.PREMIUM_PLUS)
        {
            await socket.PrintRequiresPremPlus();
            return;
        }
        var attribNames = arguments.Trim('"').Split(' ');
        if (attribNames.Length != 2)
            throw new CoflnetException("invalid_arguments", "Please provide two attribute names without spaces (you can use _ or ommit it) eg manapool mana_regeneration");


        var mapped = attribNames.Select(MapAttribute).ToArray();
        var attribApi = socket.GetService<IAttributeApi>();
        var result = await attribApi.ApiAttributeComboLeftAttribRightAttribGetAsync(mapped[0], mapped[1]);
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
    public static async Task PrintRequiresPremPlus(this IMinecraftSocket socket)
    {
        if (await socket.UserAccountTier() >= Shared.AccountTier.PREMIUM_PLUS)
        {
            return;
        }
        socket.Dialog(db => db.CoflCommand<PurchaseCommand>(
            $"{McColorCodes.RED}{McColorCodes.BOLD}ABORTED\n"
            + $"{McColorCodes.RED}You need to be a premium plus user to use this command",
            "premium_plus", $"Click to purchase prem+"));
    }
}
