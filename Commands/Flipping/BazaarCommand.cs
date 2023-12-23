using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
namespace Coflnet.Sky.Commands.MC;

public class BazaarCommand : McCommand
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var api = socket.GetService<IBazaarFlipperApi>();
        var items = socket.GetService<Items.Client.Api.IItemsApi>();
        var topFlips = (await api.FlipsGetAsync()).Take(8);
        var names = (await items.ItemNamesGetAsync()).ToDictionary(i=>i.Tag, i=>i.Name);
        socket.Dialog(db => db.Break.ForEach(topFlips, (db, f) =>
            db.MsgLine($"{McColorCodes.GRAY}>{McColorCodes.YELLOW}{names[f.ItemTag]}{McColorCodes.GRAY}: est {McColorCodes.GREEN}{socket.FormatPrice((long)f.ProfitPerHour)} per hour", 
                $"/bz {f.ItemTag}", $"{McColorCodes.YELLOW}{socket.FormatPrice((long)f.SellPrice)}->{McColorCodes.GREEN}{socket.FormatPrice((long)f.BuyPrice)}\n Click to view in bazaar\nRequires booster cookie")
        ));
    }
}