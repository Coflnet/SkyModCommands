using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Core;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;

namespace Coflnet.Sky.Commands.MC
{
    public abstract class ModVersionAdapter : IModVersionAdapter
    {
        public static Prometheus.Counter aprilJoke = Prometheus.Metrics.CreateCounter("sky_commands_april_flips", "How many april fools flips were sent");
        protected IMinecraftSocket socket;
        private DateTime lastSound = DateTime.UtcNow;

        public ModVersionAdapter(IMinecraftSocket socket)
        {
            this.socket = socket;
        }

        public abstract Task<bool> SendFlip(FlipInstance flip);
        public abstract void SendMessage(params ChatPart[] parts);
        public virtual void SendSound(string name, float pitch = 1)
        {
            if (DateTime.UtcNow - lastSound < TimeSpan.FromSeconds(0.1))
                return; // minecraft can't handle concurrent sounds
            lastSound = DateTime.UtcNow;
            socket.Send(Response.Create("playSound", new { name, pitch }));
        }

        protected async Task<List<ChatPart>> GetMessageparts(FlipInstance flip)
        {
            var openCommand = "/viewauction " + flip.Auction.Uuid;
            var message = socket.GetFlipMsg(flip);
            var interesting = flip.Interesting;
            var toTake = socket.Settings.Visibility?.ExtraInfoMax ?? 0;

            var uuid = flip.Auction.Uuid;
            var seller = flip.SellerName;
            if (string.IsNullOrEmpty(seller) && (socket.Settings?.Visibility?.Seller ?? false))
                seller = await socket.GetPlayerName(flip.Auction.AuctioneerId);
            var flipHover = socket.formatProvider.GetHoverText(flip);
            var parts = new List<ChatPart>(){
                new ChatPart(message, openCommand, flipHover),
                //new ChatPart(" ❤", $"/cofl rate {uuid} {flip.Finder} up", "Vote this flip up"),
                //new ChatPart("✖ ", $"/cofl rate {uuid} {flip.Finder} down", "Vote this flip down"),
            };
            if (flip.Context.TryGetValue("match", out var type) && type.StartsWith("whitelist"))
            {
                parts.Add(WhichBLEntryCommand.CreatePart(
                    "\nWhitelisted ",
                    new() { Uuid = flip.Uuid, WL = true },
                    "This flip matched one of your whitelist entries\nClick to calculate which one"));
                if (socket.Settings?.ModSettings?.Format?.Contains("ZYZZ CFG") ?? false)
                    parts.Add(new ChatPart($"{McColorCodes.BOLD}{McColorCodes.OBFUSCATED}!!{McColorCodes.RESET}{McColorCodes.BOLD}THIS IS WHITELISTED", null, null));
            }
            if (toTake > 0)
            {
                var hasLineBreak = parts.Count >= 3;
                var extraText = String.Join(McColorCodes.DARK_GRAY + ", " + McColorCodes.WHITE, interesting.Take(toTake));
                if (hasLineBreak)
                    extraText = "\n" + extraText;
                parts.Add(new ChatPart(extraText, openCommand, null));
            }


            if ((socket.Settings.Visibility?.Seller ?? false) && !NoSeller(seller))
            {
                parts.Insert(1, new ChatPart(McColorCodes.GRAY + " From: " + McColorCodes.AQUA + seller, $"/ah {seller}", $"{McColorCodes.GRAY}Open the ah for {McColorCodes.AQUA} {flip.SellerName}"));
            }
            else if ((socket.Settings.Visibility?.SellerOpenButton ?? false) || (socket.Settings.Visibility?.Seller ?? false))
            {
                var hover = $"{McColorCodes.GRAY}Open the ah for the seller";
                if (seller == "not-found")
                    hover = $"The seller name could not be found. Click to try openening their ah anyways. \nYou can also permanently activate this button instead of the name to improve flip speeds.";
                var buttonPart = new ChatPart(McColorCodes.GRAY + " sellers ah", $"/cofl ahopen {flip.Auction.AuctioneerId}", hover);
                var placeholder = "[sellerbtn]";
                AppendOrInsert(message, parts, buttonPart, placeholder);
            }
            AppendOrInsert(message, parts, new ChatPart(" ✥ ", "/cofl dialog flipoptions " + uuid, "Expand flip options"), "[menu]");

            return parts;

            static bool NoSeller(string seller)
            {
                return seller == "not-found" || string.IsNullOrEmpty(seller);
            }
        }

        private static void AppendOrInsert(string message, List<ChatPart> parts, ChatPart buttonPart, string placeholder)
        {
            if (message.ToLower().Contains(placeholder))
            {
                var match = parts.Where(p => p.text.ToLower().Contains(placeholder)).FirstOrDefault();
                var index = parts.IndexOf(match);
                parts.Remove(match);
                // split first part and replace
                var start = match.text.IndexOf(placeholder);
                var split = new string[] { match.text.Substring(0, start), match.text.Substring(start + placeholder.Length) };
                parts.Insert(0, new ChatPart(split[0], match.onClick, match.hover));
                parts.Insert(1, buttonPart);
                parts.Insert(2, new ChatPart(split[1], match.onClick, match.hover));
            }
            else
                parts.Add(buttonPart);
        }

        protected long GetWorth(FlipInstance flip)
        {
            var bedFlip = flip.Auction.Start + TimeSpan.FromSeconds(20) > DateTime.UtcNow;
            var worth = flip.Profit / 1024;
            if (flip.Context.ContainsKey("priorityOpen"))
                worth *= 64;
            if (bedFlip || flip.Context.ContainsKey("notOpen"))
                worth = 0;
            return worth;
        }

        protected async Task SendAprilFools()
        {
            var msg = await GetMessageparts(Joke);
            msg[0].onClick = "/cofl dialog echo Happy April fools ☺";
            var finder = Random.Shared.Next(6) switch
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

        public virtual void SendLoginPrompt(string loginLink)
        {
            socket.Dialog(db => db.Msg($"Please {McColorCodes.WHITE}§lclick this [LINK] to login{McColorCodes.GRAY} so we can load your settings §8(do '/cofl help login' to get more info)", loginLink, "Click to login"));
        }

        public virtual void OnAuthorize(AccountInfo accountInfo)
        {
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
    }
}