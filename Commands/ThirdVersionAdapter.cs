using System;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.MC
{
    public class ThirdVersionAdapter : ModVersionAdapter
    {
        public Prometheus.Counter aprilJoke = Prometheus.Metrics.CreateCounter("sky_commands_april_flips", "How many april fools flips were sent");
        private Random rng = new Random();

        private DateTime lastSound = DateTime.Now;

        public ThirdVersionAdapter(MinecraftSocket socket)
        {
            this.socket = socket;
        }

        public override async Task<bool> SendFlip(FlipInstance flip)
        {
            var uuid = flip.Auction.Uuid;
            var bedFlip = flip.Auction.Start + TimeSpan.FromSeconds(20) > DateTime.Now;
            var worth = flip.Profit / 1024;
            if(flip.Context.ContainsKey("priorityOpen"))
                worth *= 64;
            if(bedFlip)
                worth = 0;

            socket.Send(Response.Create("flip", new
            {
                messages = await GetMessageparts(flip),
                id = uuid,
                worth = worth,
                cost = flip.Auction.StartingBid,
                sound = (string)"note.pling"
            }));
            if (DateTime.Now.Month == 4 && DateTime.Now.Day == 1 && rng.Next(200) == 1)
            {
                var msg = await GetMessageparts(Joke);
                msg[0].onClick = "/cofl dialog echo Happy April fools ☺";
                var finder = rng.Next(6) switch
                {
                    1 => "HYAUCTIONS",
                    2 => "STONKS",
                    3 => "AOTF",
                    4 => "Jasmine",
                    5 => "NEC",
                    _ => "TFM",
                };
                msg[0].text = msg[0].text.Replace("FLIP", finder);
                socket.Send(Response.Create("chatMessage", msg));
                aprilJoke.Inc();
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
            LowestBin = 800_000_000,
            MedianPrice = 945_123_456,
            Name = "Hyperion",
            SellerName = "Äkwav",
            Tag = "HYPERION",
            Volume = 420,
            Interesting = new System.Collections.Generic.List<string>() { McColorCodes.LIGHT_PURPLE + "Chimera", "Black Sheep Skin", "Technoblade" },
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
            if(DateTime.Now -lastSound < TimeSpan.FromSeconds(0.1) )
                return; // minecraft can't handle concurrent sounds
            lastSound = DateTime.Now;
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