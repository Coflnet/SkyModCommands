using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;
using Coflnet.Sky.Items.Client.Api;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

internal static class BazaarInstantBuy
{
    internal static async Task Forward(IMinecraftSocket socket, IEnumerable<string> lines, DateTime receivedAt)
    {
        var index = 0;
        foreach (var line in lines)
        {
            var timestamp = receivedAt.AddTicks(index++);
            var match = Regex.Match(Regex.Replace(line, "§.", ""), @"^\[Bazaar\] Bought ([\d,]+)x (.+) for ([\d,]+(?:\.\d+)?) coins!$");
            if (!match.Success)
                continue;
            var logger = socket.GetService<ILogger<BazaarSignalSubscriptionService>>();
            try
            {
                var name = match.Groups[2].Value;
                var tag = socket.SessionInfo.BazaarDisplayState?.ItemNames.FirstOrDefault(p => p.Value == name).Key;
                if (name.EndsWith(" Shard") && Constants.ShardNames.TryGetValue(name[..^6], out var shard))
                    tag = "SHARD_" + shard.ToUpperInvariant();
                if (tag == null)
                {
                    var items = await socket.GetService<IItemsApi>().ItemsSearchTermGetAsync(name);
                    tag = items.Where(i => string.Equals(i.Text, name, StringComparison.OrdinalIgnoreCase))
                        .Select(i => i.Tag).Distinct().SingleOrDefault();
                }
                if (tag == null)
                {
                    logger?.LogDebug("Instant Bazaar buy has no unambiguous item ID: {ItemName}", name);
                    continue;
                }
                using var response = await socket.GetService<IHttpClientFactory>().CreateClient("BazaarOrders")
                    .PostAsJsonAsync("OrderBook/instant-buy", new {
                        ItemTag = tag, Timestamp = timestamp,
                        Amount = int.Parse(match.Groups[1].Value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture),
                        Coins = double.Parse(match.Groups[3].Value, NumberStyles.Number, CultureInfo.InvariantCulture)
                    });
                // A rolling deployment can still reach an older matching service. Do not replay trades.
                if (response.StatusCode != HttpStatusCode.NotFound)
                    response.EnsureSuccessStatusCode();
                logger?.LogDebug("Forwarded instant Bazaar buy for {UserId}/{ItemTag}: HTTP {Status}, observed {ObservedAt:o}",
                    socket.UserId, tag, (int)response.StatusCode, timestamp);
            }
            catch (Exception e)
            {
                logger?.LogWarning(e, "Instant Bazaar buy forwarding failed for {UserId}; public observations will reconcile", socket.UserId);
            }
        }
    }
}
