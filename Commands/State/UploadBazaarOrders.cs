using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Models;
using Coflnet.Sky.ModCommands.Services;
using Coflnet.Sky.Commands.Shared;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class UploadBazaarOrders : McCommand
{
    private readonly InventoryParser parser = new();

    /// <summary>
    /// Buy orders are only placed while below this many total open orders, leaving slots free for
    /// sell orders and higher-priority "fast track" buys (buy placement is rate limited to 2/minute).
    /// The background <see cref="BazaarFlipService"/> cycle fills further, up to
    /// <see cref="BazaarOrderStateHelper.MaxOpenBuyOrders"/>.
    /// </summary>
    public const int BuyOrderSlotCap = 18;

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        if (BazaarOrderStateHelper.IsOrderOptionsSnapshot(arguments))
        {
            const string message = "Wrong bazaar order state uploaded: received a single-order options window instead of the full bazaar order overview. Keeping previous bazaar orders.";
            Activity.Current?.Log(message);
            socket.Dialog(db => db.MsgLine(message));
            return;
        }

        socket.SessionInfo.BazaarOrders = BazaarOrderStateHelper.ParseOpenOrders(arguments, parser);
        var newlyConfirmed = BazaarOrderStateHelper.SyncSentOrdersWithUpload(socket.SessionInfo.SentBazaarOrders, socket.SessionInfo.BazaarOrders);
        RecordActedRecommendations(socket, newlyConfirmed);
        Activity.Current?.Log(JsonConvert.SerializeObject(socket.SessionInfo.BazaarOrders));
        Activity.Current?.Log("Bazaar orders tracked: " + socket.SessionInfo.ActiveBazaarOrderCount);
        if(socket.SessionInfo.ActiveBazaarOrderCount >= 1)
        {
            Activity.Current?.AddTag("orders", "some");
        }

        await TryRefillOrders(socket);
    }

    /// <summary>
    /// Records the "user acted on the recommendation" half of the compliance signal (see
    /// BazaarRecommendationTelemetry) for every recommendation that just got its ConfirmedAt set by this
    /// upload. Gated on <see cref="FullAfVersionAdapter"/> like <see cref="TryRefillOrders"/> below;
    /// only macro-bot clients populate SentBazaarOrders.
    /// </summary>
    private static void RecordActedRecommendations(MinecraftSocket socket, List<SentBazaarOrderInfo> newlyConfirmed)
    {
        if (newlyConfirmed == null || newlyConfirmed.Count == 0 || socket.ModAdapter is not FullAfVersionAdapter)
            return;

        var tier = socket.SessionInfo.SessionTier;
        var isBot = socket.SessionInfo.IsMacroBot;
        foreach (var _ in newlyConfirmed)
            BazaarRecommendationTelemetry.RecordActed(tier, isBot);
    }

    /// <summary>
    /// Reacts to an order-overview upload by topping up the user's open orders. The sell-first /
    /// buy-fallback logic is shared with the background cycle in
    /// <see cref="BazaarFlipService.RefillOrders"/>; uploads stop buying at
    /// <see cref="BuyOrderSlotCap"/> to keep slots free for higher-priority "fast track" buys.
    /// </summary>
    private static Task TryRefillOrders(MinecraftSocket socket)
    {
        // Only macro-bot clients upload order overviews and can place orders; skip everything else
        // (also keeps unit tests with a bare socket from resolving the background service).
        if (socket.ModAdapter is not FullAfVersionAdapter)
            return Task.CompletedTask;

        return socket.GetService<BazaarFlipService>().RefillOrders(socket, BuyOrderSlotCap);
    }
}
