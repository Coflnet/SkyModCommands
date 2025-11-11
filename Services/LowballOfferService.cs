using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cassandra;
using Cassandra.Data.Linq;
using Cassandra.Mapping;
using Coflnet.Kafka;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using System.Globalization;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.PlayerName.Client.Api;

namespace Coflnet.Sky.ModCommands.Services;

public class LowballOfferService
{
    private readonly ISession session;
    private readonly IConfiguration config;
    private readonly ILogger<LowballOfferService> logger;
    private readonly Kafka.KafkaCreator kafkaCreator;
    private const string KafkaTopic = "sky-lowball-offers";
    private static readonly HttpClient httpClient = new HttpClient();

    public LowballOfferService(ISession session, IConfiguration config, ILogger<LowballOfferService> logger, Kafka.KafkaCreator kafkaCreator)
    {
        this.session = session;
        this.config = config;
        this.logger = logger;
        this.kafkaCreator = kafkaCreator;
        InitializeTables();
    }

    private void InitializeTables()
    {
        try
        {
            var userTable = GetUserTable();
            userTable.CreateIfNotExists();

            var itemTable = GetItemTable();
            itemTable.CreateIfNotExists();

            // Set 7-day TTL on both tables
            var ks = session.Keyspace;
            if (!string.IsNullOrEmpty(ks))
            {
                try
                {
                    // Check current TTL and only alter if it's different or unset
                    var rs = session.Execute($"SELECT default_time_to_live FROM system_schema.tables WHERE keyspace_name = '{ks}' AND table_name = 'lowball_offers';");
                    var row = rs.FirstOrDefault();
                    if (row != null)
                    {
                        int? currentTtl = null;
                        if (!row.IsNull("default_time_to_live"))
                        {
                            currentTtl = row.GetValue<int>("default_time_to_live");
                        }

                        if (currentTtl.HasValue && currentTtl.Value == 604800)
                        {
                            logger.LogInformation("lowball_offers already has TTL=604800; skipping ALTER TABLE");
                        }
                        else
                        {
                            session.Execute($"ALTER TABLE {ks}.lowball_offers WITH default_time_to_live = 604800;");
                            session.Execute($"ALTER TABLE {ks}.lowball_offers_by_item WITH default_time_to_live = 604800;");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to set TTL on lowball offer tables");
                }
            }

            logger.LogInformation("Lowball offer tables initialized with 7-day TTL");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize lowball offer tables");
        }
    }

    public Table<LowballOffer> GetUserTable()
    {
        return new Table<LowballOffer>(session, new MappingConfiguration().Define(
            new Map<LowballOffer>()
                .PartitionKey(u => u.UserId)
                .ClusteringKey(u => u.CreatedAt, SortOrder.Descending)
                .ClusteringKey(u => u.OfferId)
                .Column(u => u.ItemTag, cm => cm.WithName("item_tag"))
                .Column(u => u.ItemName, cm => cm.WithName("item_name"))
                .Column(u => u.ApiAuctionJson, cm => cm.WithName("api_auction_json"))
                .Column(u => u.Filters, cm => cm.WithName("filters"))
                .Column(u => u.AskingPrice, cm => cm.WithName("asking_price"))
                .Column(u => u.OfferId, cm => cm.WithSecondaryIndex())
                .Column(u => u.ItemCount, cm => cm.WithName("item_count")
        )));
    }

    public Table<LowballOfferByItem> GetItemTable()
    {
        return new Table<LowballOfferByItem>(session, new MappingConfiguration().Define(
            new Map<LowballOfferByItem>()
                .PartitionKey(u => u.ItemTag)
                .ClusteringKey(u => u.CreatedAt, SortOrder.Descending)
                .ClusteringKey(u => u.OfferId)));
    }

    public async Task<LowballOffer> CreateOffer(string userId, SaveAuction item, long askingPrice, Sniper.Client.Model.PriceEstimate estimate, Dictionary<string, string> filters = null)
    {
        var offerId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var json = JsonConvert.SerializeObject(item);

        var offer = new LowballOffer
        {
            UserId = userId,
            CreatedAt = createdAt,
            OfferId = offerId,
            ItemTag = item.Tag,
            ItemName = item.ItemName,
            MinecraftAccount = Guid.Parse(item.AuctioneerId),
            ApiAuctionJson = json,
            Filters = filters != null ? JsonConvert.SerializeObject(filters) : null,
            AskingPrice = askingPrice,
            Lore = item.Context.GetValueOrDefault("lore", ""),
            ItemCount = item.Count
        };

        var offerByItem = new LowballOfferByItem
        {
            ItemTag = item.Tag,
            CreatedAt = createdAt,
            OfferId = offerId,
            UserId = userId,
            MinecraftAccount = Guid.Parse(item.AuctioneerId),
            ItemName = item.ItemName,
            ApiAuctionJson = json,
            Filters = filters != null ? JsonConvert.SerializeObject(filters) : null,
            AskingPrice = askingPrice,
            Lore = item.Context.GetValueOrDefault("lore", ""),
            ItemCount = item.Count
        };

        // Store in both tables. We use a secondary index on offer_id for lookups in development.
        await GetUserTable().Insert(offer).ExecuteAsync();
        await GetItemTable().Insert(offerByItem).ExecuteAsync();

        // Publish to Kafka
        await PublishToKafka(offer);

        logger.LogInformation($"Created lowball offer {offerId} for user {userId}, item {item.Tag}");

        // Try to post a nicely formatted webhook about the new offer
        try
        {
            await SendWebhookAsync(offer, estimate);
        }
        catch (Exception ex)
        {
            // Ensure webhook failures don't block offer creation
            logger.LogWarning(ex, "Failed to send lowball webhook");
        }

        return offer;
    }

    /// <summary>
    /// Extracts the Minecraft color code from the beginning of an item name and converts it to a Discord embed color.
    /// Minecraft color codes use the § character followed by a hex digit.
    /// Returns the cleaned item name (without color codes) and the corresponding Discord embed color integer.
    /// </summary>
    private (string cleanName, int color) ExtractColorAndCleanItemName(string itemName)
    {
        if (string.IsNullOrEmpty(itemName))
            return (itemName, 3066993); // default green-ish

        // Minecraft color code mapping (§ followed by hex digit to RGB hex values)
        var colorMap = new Dictionary<char, int>
        {
            { '0', 0x000000 }, // Black
            { '1', 0x0000AA }, // Dark Blue
            { '2', 0x00AA00 }, // Dark Green
            { '3', 0x00AAAA }, // Dark Aqua
            { '4', 0xAA0000 }, // Dark Red
            { '5', 0xAA00AA }, // Dark Purple
            { '6', 0xFFAA00 }, // Gold
            { '7', 0xAAAAAA }, // Gray
            { '8', 0x555555 }, // Dark Gray
            { '9', 0x5555FF }, // Blue
            { 'a', 0x55FF55 }, // Green
            { 'b', 0x55FFFF }, // Aqua
            { 'c', 0xFF5555 }, // Red
            { 'd', 0xFF55FF }, // Light Purple
            { 'e', 0xFFFF55 }, // Yellow
            { 'f', 0xFFFFFF }  // White
        };

        // Check if the item name starts with a color code (§ followed by a hex digit)
        if (itemName.Length >= 2 && itemName[0] == '§')
        {
            char colorCode = itemName[1];
            if (colorMap.TryGetValue(colorCode, out int hexColor))
            {
                // Remove the color code from the item name
                string cleanName = itemName.Substring(2).TrimStart();
                return (cleanName, hexColor);
            }
        }

        // No color code found, return the original name with default color
        return (itemName, 3066993); // default green-ish
    }

    private async Task SendWebhookAsync(LowballOffer offer, Sniper.Client.Model.PriceEstimate estimate)
    {
        var webhookUrl = config["LOWBALL_WEBHOOK_URL"];
        if (string.IsNullOrEmpty(webhookUrl))
        {
            logger.LogDebug("LOWBALL_WEBHOOK_URL not configured; skipping webhook post");
            return;
        }

        // Build Discord-style embed payload
        var itemImage = $"https://sky.coflnet.com/static/item/{offer.ItemTag}";
        var sellerIcon = $"https://crafatar.com/avatars/{offer.MinecraftAccount:N}";
        var name = await DiHandler.GetService<IPlayerNameApi>().PlayerNameNameUuidGetAsync(offer.MinecraftAccount.ToString("N"));

        var priceText = offer.AskingPrice.ToString("N0", CultureInfo.InvariantCulture);
        var targetValue = estimate.Median.ToString("N0", CultureInfo.InvariantCulture);
        var itemCountText = offer.ItemCount > 1 ? $" x{offer.ItemCount}" : string.Empty;

        // Extract color code from item name and convert to Discord embed color
        var (cleanItemName, embedColor) = ExtractColorAndCleanItemName(offer.ItemName);

        var embed = new
        {
            title = "New Lowball Offer",
            description = $"**{cleanItemName}**{itemCountText}",
            color = embedColor,
            thumbnail = new { url = itemImage },
            author = new { name = name, icon_url = sellerIcon },
            fields = new[]
            {
                new { name = "Asking Price", value = priceText, inline = true },
                new { name = "Estimated Value", value = targetValue, inline = true },
                new { name = "Visit Command", value = $"/visit {name}", inline = true }
            },
            footer = new { text = "SkyCofl lowball offer" },
            timestamp = offer.CreatedAt.ToString("o")
        };

        var payload = new { username = "Lowball Bot", embeds = new[] { embed } };

        var json = JsonConvert.SerializeObject(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var resp = await httpClient.PostAsync(webhookUrl, content);
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync();
            logger.LogWarning("Lowball webhook POST returned {Status} - {Body}", resp.StatusCode, body);
        }
        else
        {
            logger.LogInformation("Posted lowball webhook for offer {OfferId}", offer.OfferId);
        }
    }

    private async Task PublishToKafka(LowballOffer offer)
    {
        try
        {
            await kafkaCreator.CreateTopicIfNotExist(KafkaTopic, 1);
            using var producer = kafkaCreator.BuildProducer<string, string>();
            await producer.ProduceAsync(KafkaTopic, new Confluent.Kafka.Message<string, string>
            {
                Key = offer.OfferId.ToString(),
                Value = offer.ApiAuctionJson
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to publish lowball offer {offer.OfferId} to Kafka");
        }
    }

    public async Task<List<LowballOffer>> GetOffersByUser(string userId, DateTimeOffset? before = null, int limit = 20)
    {
        var query = GetUserTable().Where(x => x.UserId == userId);

        if (before.HasValue)
        {
            query = query.Where(x => x.CreatedAt < before.Value);
        }

        return await query.Take(limit).ExecuteAsync()
            .ContinueWith(t => t.Result.ToList());
    }

    public async Task<List<LowballOfferByItem>> GetOffersByItem(string itemTag, Dictionary<string, string> filters = null, DateTimeOffset? before = null, int limit = 20)
    {
        var query = GetItemTable().Where(x => x.ItemTag == itemTag);

        if (before.HasValue)
        {
            query = query.Where(x => x.CreatedAt < before.Value);
        }

        var results = await query.Take(limit * 3).ExecuteAsync(); // Fetch more to filter

        if (filters == null || filters.Count == 0)
        {
            return results.Take(limit).ToList();
        }

        // Filter by matching filters
        var filtered = results.Where(offer =>
        {
            if (string.IsNullOrEmpty(offer.Filters))
                return false;

            try
            {
                var offerFilters = JsonConvert.DeserializeObject<Dictionary<string, string>>(offer.Filters);
                if (offerFilters == null)
                    return false;

                // All required filters must match
                return filters.All(f =>
                    offerFilters.ContainsKey(f.Key) && offerFilters[f.Key] == f.Value);
            }
            catch
            {
                return false;
            }
        }).Take(limit).ToList();

        return filtered;
    }

    public async Task<bool> DeleteOffer(string userId, Guid offerId)
    {
        try
        {
            // Use secondary index on offer_id to find the rows and then delete by exact primary keys
            var userMatches = await GetUserTable().Where(x => x.OfferId == offerId).ExecuteAsync();
            var userRow = userMatches.FirstOrDefault();
            if (userRow == null)
                return false;

            // ensure the provided userId matches the row's owner
            if (userRow.UserId != userId)
                return false;

            // Delete from user table using full primary key values
            await GetUserTable()
                .Where(x => x.UserId == userId && x.OfferId == offerId && x.CreatedAt == userRow.CreatedAt)
                .Delete()
                .ExecuteAsync();

            await GetItemTable()
                .Where(x => x.ItemTag == userRow.ItemTag && x.OfferId == offerId && x.CreatedAt == userRow.CreatedAt)
                .Delete()
                .ExecuteAsync();

            logger.LogInformation($"Deleted lowball offer {offerId} for user {userId}");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to delete lowball offer {offerId}");
            return false;
        }
    }
}
