using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public class ThirdVersionAdapter : ModVersionAdapter
    {


        public ThirdVersionAdapter(MinecraftSocket socket)
        {
            this.socket = socket;
        }

        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            var uuid = flip.Auction.Uuid;

            socket.Send(Response.Create("flip", new
            {
                messages = await GetMessageparts(flip),
                id = uuid,
                worth = flip.Profit,
                cost = flip.Auction.StartingBid,
                sound = (string)"note.pling"
            }));
            if (DateTime.Now.Month == 4 && DateTime.Now.Day == 1 && new Random().Next(500) < 2)
            {
                var msg = await GetMessageparts(Joke);
                msg[0].onClick = "/cofl dialog echo Happy April fools ☺";
                msg[0].text.Replace("FLIP", "TFM");
                socket.Send(Response.Create("chatMessage", msg));
            }

            if (socket.Settings?.ModSettings?.PlaySoundOnFlip ?? false && flip.Profit > 1_000_000)
                SendSound("note.pling", (float)(1 / (Math.Sqrt((float)flip.Profit / 1_000_000) + 1)));
            return true;
        }

        private FlipInstance Joke => new FlipInstance()
        {
            Auction = new SaveAuction()
            {
                ItemName = "Hyperion",
                Tier = Tier.DIVINE,
                Bin = true,
                StartingBid = 5000,
                AuctioneerId = "384a029294fc445e863f2c42fe9709cb",
                Enchantments = new System.Collections.Generic.List<Enchantment>()
                {
                    new Enchantment(Enchantment.EnchantmentType.ultimate_chimera, 10)
                }
            },
            Bin = true,
            Finder = LowPricedAuction.FinderType.TFM,
            LastKnownCost = 5000,
            LowestBin = 800_000_000,
            MedianPrice = 945_123_456,
            Name = "Hyperion",
            SellerName = "Ekwav",
            Tag = "HYPERION",
            Volume = 30,
            Interesting = new System.Collections.Generic.List<string>() { McColorCodes.LIGHT_PURPLE + "Chimera" },
            Rarity = Tier.DIVINE,
            Context = new System.Collections.Generic.Dictionary<string, string>()
            {
                {"lore", "Haha lol, April fools :)"}
            }
        };

        public override void SendMessage(params ChatPart[] parts)
        {
            socket.Send(Response.Create("chatMessage", parts));
        }

        public override void SendSound(string name, float pitch = 1)
        {
            socket.Send(Response.Create("playSound", new { name, pitch }));
        }
    }

    public class InventoryVersionAdapter : ThirdVersionAdapter
    {
        public InventoryVersionAdapter(MinecraftSocket socket) : base(socket)
        {
        }
    }
}