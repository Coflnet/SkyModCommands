using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Models;
using Coflnet.Sky.ModCommands.Tutorials;

namespace Coflnet.Sky.ModCommands.Services;

public static class BazaarOrderDisplay
{
    public const string ClientVersion = "2.0.0-pre1";
    public const string DisableCommand = "/cofl set modhideBazaarOrderDisplay true";
    private static readonly Regex Setup = new(@"^\[Bazaar\] (Buy Order|Sell Offer) Setup! ([\d,]+)x (.+?) for ([\d,.]+) coins[!.]?$", RegexOptions.Compiled);
    private static readonly Regex Filled = new(@"^\[Bazaar\] (?:Your )?(Buy Order|Sell Offer) for ([\d,]+)x (.+?) was filled!$", RegexOptions.Compiled);

    public static bool Supports(IMinecraftSocket socket) => socket.Version == ClientVersion;

    public static void Send(IMinecraftSocket socket)
    {
        if (!Supports(socket))
            return;
        var orders = socket.SessionInfo.BazaarOrders;
        var clear = socket.Settings?.ModSettings?.HideBazaarOrderDisplay == true || orders.Count == 0;
        socket.Send(Response.Create("infoDisplay", new InfoDisplay
        {
            Id = 2,
            Title = "§6§lBazaar orders",
            Clear = clear,
            Lines = clear ? Array.Empty<ChatPart>() : orders.Take(28).Select(order => new ChatPart(
                $"{(order.IsSell ? "§6SELL" : "§aBUY")} §f{order.ItemName} §7{order.FilledAmount:N0}/{order.Amount:N0} §a{(order.IsExpired ? "Expired" : order.RemainingAmount == 0 ? "Filled!" : "filled")}",
                "/managebazaarorders",
                $"Last observed fill: {order.FilledAmount:N0}/{order.Amount:N0}\nOpen your Bazaar orders to refresh partial fills.\nPrice per unit: {order.PricePerUnit:N1} coins"))
                .Append(new ChatPart("§7[T: hover/click] §c[Disable display]", DisableCommand,
                    "Hide this display. Re-enable with /cofl set modhideBazaarOrderDisplay false"))
                .ToArray()
        }));
    }

    public static async Task HandleChat(IMinecraftSocket socket, string line)
    {
        if (!Supports(socket))
            return;
        line = Regex.Replace(line, "§.", "");
        var setup = Setup.Match(line);
        if (setup.Success)
        {
            var amount = long.Parse(setup.Groups[2].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
            if (amount <= 0)
                return;
            socket.SessionInfo.BazaarOrders.Add(new BazaarOrderInfo
            {
                Side = setup.Groups[1].Value == "Sell Offer" ? BazaarOrderSide.Sell : BazaarOrderSide.Buy,
                ItemName = setup.Groups[3].Value,
                Amount = amount,
                PricePerUnit = double.Parse(setup.Groups[4].Value, CultureInfo.InvariantCulture) / amount
            });
            Send(socket);
            if (socket.Settings?.ModSettings?.HideBazaarOrderDisplay != true)
                await socket.TriggerTutorial<BazaarOrderDisplayTutorial>();
            return;
        }
        var filled = Filled.Match(line);
        if (!filled.Success)
            return;
        var side = filled.Groups[1].Value == "Sell Offer" ? BazaarOrderSide.Sell : BazaarOrderSide.Buy;
        var total = long.Parse(filled.Groups[2].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture);
        var order = socket.SessionInfo.BazaarOrders.FirstOrDefault(o => o.Side == side
            && o.Amount == total && o.ItemName == filled.Groups[3].Value && o.RemainingAmount > 0);
        if (order != null)
        {
            order.FilledAmount = order.Amount;
            Send(socket);
        }
    }
}
