using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.PlayerState.Client.Model;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

[CommandDescription(
    "Offer items to or register as lowballer",
    "Simplifies lowballing by not requiring",
    "you to advertise anymore as a buyer.",
    "And allows you to compare multiple offers",
    "and be visited by the highest as a seller")]
public class LowballCommand : ItemSelectCommand<LowballCommand>
{
    public override bool IsPublic => true;
    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var args = arguments.Trim('"').Split(' ');
        var service = socket.GetService<LowballSerivce>();
        if (args.Length == 1)
        {
            if (args[0] == "on")
            {
                service.Enable(socket);
                socket.Dialog(db => db.MsgLine("§aLowballing is now enabled, you may receive lowballs matching your filter. To permanently enable lowballing use /cofl lowball always"));
                return;
            }
            else if (args[0] == "off")
            {
                service.Disable(null);
                socket.Dialog(db => db.MsgLine("§cLowballing is now disabled, you will no longer receive lowball offers."));
                return;
            }
            else if (args[0] == "always")
            {
                socket.sessionLifesycle.AccountSettings.Value.BlockLowballs = false;
                await socket.sessionLifesycle.AccountSettings.Update();
                service.Enable(socket);
                socket.Dialog(db => db.MsgLine("§aLowballing is now enabled permanently."));
                return;
            }
            else if (args[0] == "never")
            {
                socket.sessionLifesycle.AccountSettings.Value.BlockLowballs = true;
                await socket.sessionLifesycle.AccountSettings.Update();
                service.Disable(null);
                socket.Dialog(db => db.MsgLine("§cLowballing is now disabled permanently."));
                return;
            }
            else if (args[0] == "offer")
            {
                socket.Dialog(db => db.MsgLine("§cPlease specify a price to offer and a slot, usage: /cofl lowball offer <price> <slot>"));
                return;
            }
            else if (args[0] == "help")
            {
                socket.Dialog(db => db.MsgLine("§7Lowball command help:\n" +
                                                "§a/cofl lowball on - Enable lowballing\n" +
                                                "§c/cofl lowball off - Disable lowballing\n" +
                                                "§a/cofl lowball always - Enable lowballing permanently\n" +
                                                "§c/cofl lowball never - Disable lowballing permanently\n" +
                                                "§6/cofl lowball - Offer an item in your inventory"));
                return;
            }
            socket.Dialog(db => db.MsgLine($"{McColorCodes.GRAY}To register for lowballing, use {McColorCodes.AQUA}/cofl lowball on", "/cofl lowball on")
                .MsgLine($"{McColorCodes.GRAY}full help is available with {McColorCodes.AQUA}/cofl lowball help", "/cofl lowball help"));
            await HandleSelectionOrDisplaySelect(socket, args, "offer", $"Offer this item to lowballers: \n");
            return;
        }
        else if (args[0] == "offer" && args.Length > 1)
        {
            var price = Coflnet.Sky.Core.NumberParser.Long(args[1]);
            await HandleSelectionOrDisplaySelect(socket, args, "offer " + price, $"Offer this item to lowballers: \n");
        }
        else
        {
            socket.Dialog(db => db.MsgLine("§cInvalid arguments for lowball command. Usage: /cofl lowball [offer|on] " + arguments));
            return;
        }
    }

    protected override async Task SelectedItem(MinecraftSocket socket, string context, PlayerState.Client.Model.Item item)
    {
        if (!context.StartsWith("offer "))
        {

            socket.Dialog(db => db.MsgLine($"§cInvalid context for lowball command: {context}"));
            return;
        }

        if (context.Length <= "offer xy".Length)
        {
            var auction = ConvertToAuction(item);
            if (auction.FlatenedNBT.ContainsKey("donated_museum"))
            {
                socket.Dialog(db => db.MsgLine($"§cYou cannot trade museum items, please select another item."));
                return;
            }
            var price = await socket.GetService<ISniperClient>().GetPrices([auction]);
            Console.WriteLine(JsonConvert.SerializeObject(item));
            Console.WriteLine(JsonConvert.SerializeObject(auction));
            Console.WriteLine(JsonConvert.SerializeObject(price));
            var highPrice = price[0].Median * 0.92;
            var mediumPrice = price[0].Median * 0.85;
            var lowPrice = price[0].Median * 0.8;
            var serivce = socket.GetService<LowballSerivce>();
            var index = context.Split(' ').Last();
            socket.Dialog(db => db.MsgLine($"§7[§6§lOffer§7] §r{item.ItemName}", null, $"{item.ItemName}\n{item.Description}")
                .CoflCommand<LowballCommand>($"At: §a{socket.FormatPrice(highPrice)} coins: {McColorCodes.YELLOW}{GetBuyerCount(serivce, auction, highPrice, price)} buyers\n", $"offer {highPrice} {index}", $"offer item for\n{socket.FormatPrice(highPrice)} ")
                .CoflCommand<LowballCommand>($"At: §e{socket.FormatPrice(mediumPrice)} coins: {McColorCodes.YELLOW}{GetBuyerCount(serivce, auction, mediumPrice, price)} buyers\n", $"offer {mediumPrice} {index}", $"offer item for\n{socket.FormatPrice(mediumPrice)} ")
                .CoflCommand<LowballCommand>($"At: §c{socket.FormatPrice(lowPrice)} coins: {McColorCodes.YELLOW}{GetBuyerCount(serivce, auction, lowPrice, price)} buyers\n", $"offer {lowPrice} {index}", $"offer item for\n{socket.FormatPrice(lowPrice)} ")
                .MsgLine($"From ah in ~{socket.FormatPrice(1 / price[0].Volume * 24)} hours: ~{socket.FormatPrice(price[0].Median * 0.95)} coins"));
            // 5% for fees and likelyness of relist fees
            return;
        }
        else
        {
            var price = Coflnet.Sky.Core.NumberParser.Long(context.Substring(6));
            var auction = ConvertToAuction(item);
            var priceEstimate = await socket.GetService<ISniperClient>().GetPrices([auction]);
            if (priceEstimate.Count == 0)
            {
                socket.Dialog(db => db.MsgLine($"§cNo price estimate found for {item.ItemName}"));
                return;
            }
            var serivce = socket.GetService<LowballSerivce>();
            var buyerCount = GetBuyerCount(serivce, auction, price, priceEstimate);
            socket.Dialog(db => db.MsgLine($"§7[§6§lLowball Offer§7]§r\n{item.ItemName}")
                .MsgLine($"You offered {socket.FormatPrice(price)} coins to lowballers, {McColorCodes.YELLOW}{buyerCount} buyers are interested{McColorCodes.GRAY} in this item at this price currently and may visit your island."));
            Console.WriteLine($"received '{context}'");
            serivce.Offer(auction, price, priceEstimate[0], socket);
        }
    }

    private int GetBuyerCount(LowballSerivce serivce, Core.SaveAuction auction, double highPrice, List<Sniper.Client.Model.PriceEstimate> price)
    {
        auction.HighestBidAmount = (long)highPrice;
        return serivce.MatchCount(auction, price[0]);
    }
}

public class LowballSerivce
{
    private Dictionary<string, LowballerInfo> lowballers = new();

    public class LowballerInfo
    {
        public MinecraftSocket Socket { get; set; }
        public DateTime Registered { get; set; }
    }
    public int MatchCount(Core.SaveAuction auction, Sniper.Client.Model.PriceEstimate est)
    {
        var count = 0;
        foreach (var item in lowballers)
        {
            var median = new Core.LowPricedAuction()
            {
                Auction = auction,
                TargetPrice = est.Median,
                DailyVolume = est.Volume,
                Finder = Core.LowPricedAuction.FinderType.SNIPER_MEDIAN
            };
            if (item.Value.Socket.IsClosed)
            {
                lowballers.Remove(item.Key);
                continue;
            }
            if (item.Value.Socket.ModAdapter is not AfVersionAdapter)
                continue; // only non macroers will see this offer
            var matchInfo = item.Value.Socket.Settings.MatchesSettings(FlipperService.LowPriceToFlip(median));
            if (matchInfo.Item1)
            {
                count++;
            }
        }
        return count;
    }

    internal void Enable(MinecraftSocket socket)
    {
        lowballers[socket.SessionInfo.McUuid] = new LowballerInfo()
        {
            Socket = socket,
            Registered = DateTime.Now
        };
    }

    internal void Disable(MinecraftSocket value)
    {
        if (value == null)
        {
            return;
        }
        if (lowballers.ContainsKey(value.SessionInfo.McUuid))
        {
            lowballers.Remove(value.SessionInfo.McUuid);
        }
        else
        {
            value.Dialog(db => db.MsgLine($"§cYou are not registered for lowballing, use {McColorCodes.AQUA}/cofl lowball on{McColorCodes.RESET} to enable it."));
        }
    }

    internal void Offer(Core.SaveAuction auction, long price, Sniper.Client.Model.PriceEstimate priceEstimate, MinecraftSocket socket)
    {
        var lowballOffer = new LowballOffer()
        {
            Auction = auction,
            Price = price,
            PriceEstimate = priceEstimate,
            SellerName = socket.SessionInfo.McName
        };
        NotifyUsers(lowballOffer);
    }

    private void NotifyUsers(LowballOffer lowballOffer)
    {
        foreach (var item in lowballers)
        {
            if (item.Value.Socket.IsClosed || item.Value.Socket.HasFlippingDisabled())
            {
                lowballers.Remove(item.Key);
                continue;
            }
            var median = new Core.LowPricedAuction()
            {
                Auction = lowballOffer.Auction,
                TargetPrice = lowballOffer.PriceEstimate.Median,
                DailyVolume = lowballOffer.PriceEstimate.Volume,
                Finder = Core.LowPricedAuction.FinderType.SNIPER_MEDIAN
            };
            var instance = FlipperService.LowPriceToFlip(median);
            var matchInfo = item.Value.Socket.Settings.MatchesSettings(instance);
            if (matchInfo.Item1)
            {
                var sellerName = lowballOffer.SellerName;
                var flipMessage = item.Value.Socket.formatProvider.FormatFlip(instance);
                item.Value.Socket.Dialog(db => db.Msg($"§7[§6§lLowball Offer§7]§r from {McColorCodes.AQUA}{sellerName}")
                    .MsgLine(flipMessage, "/visit " + sellerName, $"Click to visit {sellerName} to complete the trade"));
            }
            else
                Console.WriteLine($"Lowball offer {lowballOffer.Auction.ItemName} for {lowballOffer.Price} coins did not match for {item.Value.Socket.SessionInfo.McName}, reason: {matchInfo.Item2}");
        }
    }

    public class LowballOffer
    {
        public Core.SaveAuction Auction { get; set; }
        public long Price { get; set; }
        public Sniper.Client.Model.PriceEstimate PriceEstimate { get; set; }
        public string SellerName { get; set; }
    }
}
