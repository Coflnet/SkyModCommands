using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public class TestCommand : McCommand
    {
        public override async Task Execute(MinecraftSocket socket, string arguments)
        {
            socket.SendSound("random.orb");
            socket.SendMessage("The test was successful :)");
            var r = new Random();
            var activeAuction = await ItemPrices.Instance.GetActiveAuctions(new ActiveItemSearchQuery()
            {
                name = "JUNGLE_KEY",
            });

            var targetAuction = activeAuction.OrderBy(x => x.Price + r.Next(10000)).FirstOrDefault();
            await socket.ModAdapter.SendFlip(new Shared.FlipInstance()
            {
                Auction = new SaveAuction()
                {
                    StartingBid = 5,
                    Uuid = targetAuction.Uuid,
                    AuctioneerId = "384a029294fc445e863f2c42fe9709cb",
                    Context = new() { { "lore", "Custom lore\nwhatever" } }
                },
                Finder = LowPricedAuction.FinderType.SNIPER,
                MedianPrice = 10000000,
                LowestBin = 10000000,
                Volume = 5,
                Interesting = new() { "cool flip" },
                Context = new()
            });
        }
    }
}