using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Model;
using Coflnet.Sky.Core;
namespace Coflnet.Sky.Commands.MC;

public class BazaarCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var api = socket.GetService<IBazaarFlipperApi>();
        var items = socket.GetService<Items.Client.Api.IItemsApi>();
        var topFlips = (await api.FlipsGetAsync()).Take(8);
        var names = (await items.ItemNamesGetAsync()).ToDictionary(i => i.Tag, i => i.Name);
        socket.Dialog(db => db.Break.ForEach(topFlips, (db, f) =>
            db.MsgLine($"{McColorCodes.GRAY}>{McColorCodes.YELLOW}{names[f.ItemTag]}{McColorCodes.GRAY}: est {McColorCodes.GREEN}{socket.FormatPrice((long)f.ProfitPerHour)} per hour",
                $"/bz {GetSearchValue(f, names)}", $"{McColorCodes.YELLOW}{socket.FormatPrice((long)f.SellPrice)}->{McColorCodes.GREEN}{socket.FormatPrice((long)f.BuyPrice)}\n Click to view in bazaar\nRequires booster cookie")
        ));
    }

    private static string GetSearchValue(BazaarFlip f, Dictionary<string, string> names)
    {
        var name = names[f.ItemTag];
        if(f.ItemTag.StartsWith("ENCHANTMENT_"))
        {
            // remove enchant from end
            name = name.Substring(0, name.Length - 10);
            var number = Regex.Match(f.ItemTag, @"\d+").Value;
            var converted = Roman.To(int.Parse(number));
            name = $"{name.Trim()} {converted}";
        }
        return name;
    }
}