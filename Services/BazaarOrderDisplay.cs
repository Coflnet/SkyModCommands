using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Prometheus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.ModCommands.Models;
using Coflnet.Sky.ModCommands.Tutorials;

namespace Coflnet.Sky.ModCommands.Services;

public static class BazaarOrderDisplay
{
    internal const string SourceName = "Coflnet.Sky.ModCommands.Bazaar";
    internal static readonly ActivitySource Traces = new(SourceName);
    internal static readonly Counter Snapshots = Metrics.CreateCounter("sky_mod_bazaar_snapshots_total", "HUD snapshot application outcomes", new CounterConfiguration { LabelNames = new[] { "result" } });
    public const string ClientVersion = "2.0.0";
    public const string DisableCommand = "/cofl set modhideBazaarOrderDisplay true";
    public static bool Supports(IMinecraftSocket socket) => socket.Version == ClientVersion || socket.Version == "2.0.0-pre1";

    public static void Send(IMinecraftSocket socket, ILogger logger = null)
    {
        logger ??= socket.GetService<ILogger<BazaarSignalSubscriptionService>>() ?? NullLogger<BazaarSignalSubscriptionService>.Instance;
        if (!Supports(socket))
            return;
        var state = socket.SessionInfo.BazaarDisplayState;
        var orders = state?.Orders.Where(o => string.Equals(o.PlayerName, socket.SessionInfo.McName,
            StringComparison.OrdinalIgnoreCase)).OrderByDescending(o => o.IsSell).ThenBy(o => o.Timestamp).ToList() ?? new();
        var clear = socket.Settings?.ModSettings?.HideBazaarOrderDisplay == true || orders.Count == 0;
        logger.LogDebug("Sending Bazaar HUD for {UserId}, revision {Revision}, player {PlayerName}, matching orders {OrderCount}, hidden {Hidden}, clear {Clear}; TraceId {TraceId}",
            socket.UserId, state?.Revision, socket.SessionInfo.McName, orders.Count, socket.Settings?.ModSettings?.HideBazaarOrderDisplay == true, clear, Activity.Current?.TraceId.ToString());
        socket.Send(Response.Create("infoDisplay", new InfoDisplay
        {
            Id = 2,
            Title = "§6§lBazaar orders",
            Clear = clear,
            Lines = clear ? Array.Empty<ChatPart>() : orders.Take(28).Select(order =>
            {
                var top = !order.IsExpired && order.Filled < order.Amount ? order.IsTopOrder : null;
                var position = top switch { true => " §aTop order", false => order.IsSell ? " §cUndercut" : " §cOutbid", _ => "" };
                var positionHover = top switch {
                    true => "\nAt the best known price (orders at the same price share top status).",
                    false => "\nOrders at a better price are ahead of this order.", _ => "" };
                return new ChatPart(
                    $"{(order.IsSell ? "§6SELL" : "§aBUY")} §f{state.ItemNames.GetValueOrDefault(order.ItemId, order.ItemId)} §7{order.Filled:N0}/{order.Amount:N0}{(order.IsExpired ? " §8Expired" : order.IsEstimate == false && order.Filled == order.Amount ? " §aFilled!" : position)}{(order.Claimed.HasValue ? $" §e{Math.Max(0, order.Filled - order.Claimed.Value):N0} to claim" : "")}",
                    "/managebazaarorders",
                    $"Filled: {order.Filled:N0}/{order.Amount:N0}{(order.Claimed.HasValue ? $"\nClaimed: {order.Claimed:N0}; remaining to claim: {Math.Max(0, order.Filled - order.Claimed.Value):N0}" : "")}{(order.IsExpired ? "\nExpired: no further fills." : "")}\n{(order.IsEstimate != false ? "Estimated from price-level changes." : "Confirmed fill state.")} Updated automatically by SkyBazaar.\nPrice per unit: {order.PricePerUnit:N1} coins{positionHover}");
            })
                .Append(new ChatPart("§c[Disable display]", DisableCommand,
                    "Hide this display. Re-enable with /cofl set modhideBazaarOrderDisplay false"))
                .ToArray()
        }));
    }

    public static async Task Apply(IMinecraftSocket socket, BazaarOrderSnapshot state, bool restore = false, ILogger logger = null)
    {
        logger ??= socket.GetService<ILogger<BazaarSignalSubscriptionService>>() ?? NullLogger<BazaarSignalSubscriptionService>.Instance;
        if (!Supports(socket) || state.UserId != socket.UserId)
        {
            var reason = !Supports(socket) ? "version" : "owner";
            Snapshots.WithLabels(reason).Inc();
            logger.LogDebug("Skipped Bazaar HUD: {Reason}, user {UserId}, snapshot owner {Owner}, version {Version}, revision {Revision}",
                reason, socket.UserId, state.UserId, socket.Version, state.Revision);
            return;
        }
        ActivityContext.TryParse(state.TraceParent, state.TraceState, true, out var parent);
        using var span = Traces.StartActivity("bazaar.hud.apply", ActivityKind.Consumer, parent);
        span?.SetTag("bazaar.user_id", socket.UserId);
        span?.SetTag("bazaar.revision", state.Revision);
        span?.SetTag("bazaar.restore", restore);
        var tutorial = false;
        await socket.SessionInfo.BazaarDisplayLock.WaitAsync();
        try
        {
            if (state.Revision <= (socket.SessionInfo.BazaarDisplayState?.Revision ?? 0))
            {
                Snapshots.WithLabels("stale").Inc();
                span?.SetTag("bazaar.result", "stale");
                logger.LogDebug("Skipped stale Bazaar HUD for {UserId}: revision {Revision} <= {CurrentRevision}, restore {Restore}; TraceId {TraceId}",
                    socket.UserId, state.Revision, socket.SessionInfo.BazaarDisplayState?.Revision, restore, Activity.Current?.TraceId.ToString());
                return;
            }
            socket.SessionInfo.BazaarDisplayState = state;
            Send(socket, logger);
            Snapshots.WithLabels("applied").Inc();
            span?.SetTag("bazaar.result", "applied");
            tutorial = !restore && state.Created && string.Equals(state.PlayerName, socket.SessionInfo.McName,
                StringComparison.OrdinalIgnoreCase) && socket.Settings?.ModSettings?.HideBazaarOrderDisplay != true;
        }
        finally { socket.SessionInfo.BazaarDisplayLock.Release(); }
        if (tutorial)
            await socket.TriggerTutorial<BazaarOrderDisplayTutorial>();
    }
}
