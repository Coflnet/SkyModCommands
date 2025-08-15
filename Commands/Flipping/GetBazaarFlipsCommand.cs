using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Bazaar.Flipper.Client.Api;
namespace Coflnet.Sky.Commands.MC;

/// <summary>
/// Requests to be notified about bazaar flips
/// </summary>
public class GetBazaarFlipsCommand : ArgumentsCommand
{
    protected override string Usage => "<orderCount=3>";

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var buget = socket.SessionInfo.Purse;
        if (buget < 100_000)
        {
            socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}You need at least 100,000 coins in your purse to receive bazaar flips, make sure you use a compatible mod/client"));
            return;
        }
        var count = args["orderCount"];
        if (!int.TryParse(count, out var orderCount) || orderCount < 1 || orderCount > 12)
        {
            socket.Dialog(db => db.MsgLine($"{McColorCodes.RED}The order count has to be a number between 1 and 12"));
            return;
        }
        socket.Dialog(db => db.MsgLine("Alright, you will receive bazaar flips attempted to be placed optimally within the next few minutes"));

        var items = socket.GetService<Items.Client.Api.IItemsApi>();
        var names = (await items.ItemNamesGetAsync()).ToDictionary(i => i.Tag, i => i.Name);
        for (var i = 0; i < orderCount; i++)
        {
            var flipApi = socket.GetService<IBazaarFlipperApi>();
            var bazaarApi = socket.GetService<IBazaarApi>();
            var flips = await flipApi.DemandGetAsync();
            var recommended = flips.OrderByDescending(f => f.CurrentProfitPerHour).Take(3).OrderByDescending(f => Random.Shared.Next()).First();
            var item = await bazaarApi.GetHistoryGraphAsync(recommended.ItemTag);
            var price = item.OrderByDescending(h => h.Timestamp).First().Buy + 0.1;
            var recommend = new OrderRecommend
            {
                ItemName = BazaarCommand.GetSearchValue(recommended.ItemTag, names[recommended.ItemTag]),
                ItemTag = recommended.ItemTag,
                Price = price,
                Amount = price < 100_000 ? 64 : 4
            };
            socket.Send(Response.Create("bzRecommend", recommend));
            socket.Dialog(db => db.MsgLine($"Recommending an order of {McColorCodes.GREEN}{recommend.Amount}x {McColorCodes.YELLOW}{recommend.ItemName} {McColorCodes.GRAY}for {McColorCodes.GREEN}{socket.FormatPrice((long)recommend.Price)}{McColorCodes.GRAY}",
                $"/bz {recommend.ItemName}", "click to open on bazaar"));

            // time to be interupted by better orders (TODO) or just wait for demand to change
            await Task.Delay(TimeSpan.FromMinutes(2));
        }
    }

    public class OrderRecommend
    {
        public string ItemName { get; set; }
        public string ItemTag { get; set; }
        public double Price { get; set; }
        public int Amount { get; set; }
        public bool IsSell { get; set; }
    }

}