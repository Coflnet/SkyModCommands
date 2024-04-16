using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.Sniper.Client.Api;

namespace Coflnet.Sky.Commands.MC;

public class CheapAttribCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var map = Constants.AttributeKeys.SelectMany(k => AltName(k).Select(a => (a, k))).ToDictionary(k => k.a, v => v.k);
        var attribNames = arguments.Trim('"').Split(' ');
        if (attribNames.Length != 2)
            throw new CoflnetException("invalid_arguments", "Please provide two attribute names without spaces (you can use _ or ommit it) eg manapool mana_regeneration");

        // error if not found 
        foreach (var item in attribNames)
        {
            if (!map.ContainsKey(item))
                throw new CoflnetException("invalid_attribute", $"The attribute {item} is not known, please check for typos or report adding an alias");
        }
        var mapped = attribNames.Select(a => map[a]).ToArray();
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

    private string FormatName(string attribname)
    {
        var words = attribname.Split('_');
        return string.Join(" ", words.Select(w => w.First().ToString().ToUpper() + w.Substring(1).ToLower()));
    }

    IEnumerable<string> AltName(string attribname)
    {
        yield return attribname;
        if (attribname.Contains('_'))
            yield return attribname.Replace("_", "");

    }
}

public class OpenUidAuctionCommand : McCommand
{
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var uid = long.Parse(arguments.Trim('"'));
        using (var db = new HypixelContext())
        {
            var auction = db.Auctions.Where(a => a.UId == uid).FirstOrDefault();
            if (auction == null)
                throw new CoflnetException("not_found", "The auction with the given uid was not found");
            if (auction.End < DateTime.UtcNow)
                throw new CoflnetException("expired", "The auction has already ended");
            socket.ExecuteCommand("/viewauction " + auction.Uuid);
        }
    }
}