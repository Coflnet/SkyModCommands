using System;
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
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace Coflnet.Sky.ModCommands.Services;

public class LowballOfferService
{
    private readonly ISession session;
    private readonly IConfiguration config;
    private readonly ILogger<LowballOfferService> logger;
    private readonly Kafka.KafkaCreator kafkaCreator;
    private readonly MinecraftLoreRenderer loreRenderer;
    private const string KafkaTopic = "sky-lowball-offers";
    private static readonly HttpClient httpClient = new HttpClient();

    public LowballOfferService(ISession session, IConfiguration config, ILogger<LowballOfferService> logger, Kafka.KafkaCreator kafkaCreator, MinecraftLoreRenderer loreRenderer)
        : this(session, config, logger, kafkaCreator, loreRenderer, initializeTables: true)
    {
    }

    protected LowballOfferService(ISession session, IConfiguration config, ILogger<LowballOfferService> logger, Kafka.KafkaCreator kafkaCreator, MinecraftLoreRenderer loreRenderer, bool initializeTables)
    {
        this.session = session;
        this.config = config;
        this.logger = logger;
        this.kafkaCreator = kafkaCreator;
        this.loreRenderer = loreRenderer;
        if (initializeTables)
            InitializeTables();
    }

    protected virtual void InitializeTables()
    {
        try
        {
            var userTable = GetUserTable();
            userTable.CreateIfNotExists();

            var itemTable = GetItemTable();
            itemTable.CreateIfNotExists();

            EnsureLowballOfferColumns();

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

    private void EnsureLowballOfferColumns()
    {
        var userColumns = new Dictionary<string, string>
        {
            { "item_tag", "text" },
            { "minecraft_account", "uuid" },
            { "item_name", "text" },
            { "api_auction_json", "text" },
            { "filters", "text" },
            { "asking_price", "bigint" },
            { "lore", "text" },
            { "item_count", "int" },
        };
        var itemColumns = new Dictionary<string, string>
        {
            { "user_id", "text" },
            { "minecraft_account", "uuid" },
            { "item_name", "text" },
            { "api_auction_json", "text" },
            { "filters", "text" },
            { "asking_price", "bigint" },
            { "lore", "text" },
            { "item_count", "int" },
        };

        EnsureColumnsExist("lowball_offers", userColumns);
        EnsureColumnsExist("lowball_offers_by_item", itemColumns);
    }

    private void EnsureColumnsExist(string tableName, Dictionary<string, string> expectedColumns)
    {
        var keyspace = session.Keyspace;
        if (string.IsNullOrEmpty(keyspace))
            return;

        try
        {
            var rows = session.Execute($"SELECT column_name FROM system_schema.columns WHERE keyspace_name = '{keyspace}' AND table_name = '{tableName}';");
            var existingColumns = rows.Select(row => row.GetValue<string>("column_name")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var column in expectedColumns)
            {
                if (existingColumns.Contains(column.Key))
                    continue;

                session.Execute($"ALTER TABLE {keyspace}.{tableName} ADD {column.Key} {column.Value};");
                logger.LogInformation("Added missing column {Column} to {Table}", column.Key, tableName);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to verify lowball schema for table {Table}", tableName);
        }
    }

    public Table<LowballOffer> GetUserTable()
    {
        return new Table<LowballOffer>(session, new MappingConfiguration().Define(
            new Map<LowballOffer>()
                .PartitionKey(u => u.UserId)
                .ClusteringKey(u => u.CreatedAt, SortOrder.Descending)
                .ClusteringKey(u => u.OfferId)
                .Column(u => u.UserId, cm => cm.WithName("user_id"))
                .Column(u => u.CreatedAt, cm => cm.WithName("created_at"))
                .Column(u => u.OfferId, cm => cm.WithName("offer_id").WithSecondaryIndex())
                .Column(u => u.ItemTag, cm => cm.WithName("item_tag"))
                .Column(u => u.MinecraftAccount, cm => cm.WithName("minecraft_account"))
                .Column(u => u.ItemName, cm => cm.WithName("item_name"))
                .Column(u => u.ApiAuctionJson, cm => cm.WithName("api_auction_json"))
                .Column(u => u.Filters, cm => cm.WithName("filters"))
                .Column(u => u.AskingPrice, cm => cm.WithName("asking_price"))
                .Column(u => u.Lore, cm => cm.WithName("lore"))
                .Column(u => u.ItemCount, cm => cm.WithName("item_count"))
            ));
    }

    public Table<LowballOfferByItem> GetItemTable()
    {
        return new Table<LowballOfferByItem>(session, new MappingConfiguration().Define(
            new Map<LowballOfferByItem>()
                .PartitionKey(u => u.ItemTag)
                .ClusteringKey(u => u.CreatedAt, SortOrder.Descending)
                .ClusteringKey(u => u.OfferId)
                .Column(u => u.ItemTag, cm => cm.WithName("item_tag"))
                .Column(u => u.CreatedAt, cm => cm.WithName("created_at"))
                .Column(u => u.OfferId, cm => cm.WithName("offer_id"))
                .Column(u => u.UserId, cm => cm.WithName("user_id"))
                .Column(u => u.MinecraftAccount, cm => cm.WithName("minecraft_account"))
                .Column(u => u.ItemName, cm => cm.WithName("item_name"))
                .Column(u => u.ApiAuctionJson, cm => cm.WithName("api_auction_json"))
                .Column(u => u.Filters, cm => cm.WithName("filters"))
                .Column(u => u.AskingPrice, cm => cm.WithName("asking_price"))
                .Column(u => u.Lore, cm => cm.WithName("lore"))
                .Column(u => u.ItemCount, cm => cm.WithName("item_count"))
            ));
    }

    public async Task<LowballOffer> CreateOffer(string userId, SaveAuction item, long askingPrice, Sniper.Client.Model.PriceEstimate estimate, string websiteLink, Dictionary<string, string> filters = null)
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
        await InsertOffersAsync(offer, offerByItem);

        // Publish to Kafka
        await PublishToKafka(offer);

        logger.LogInformation($"Created lowball offer {offerId} for user {userId}, item {item.Tag}");

        // Try to post a nicely formatted webhook about the new offer
        try
        {
            await SendWebhookAsync(offer, estimate,websiteLink);
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
                string cleanName = Regex.Replace(itemName, "§.","").TrimStart();
                return (cleanName, hexColor);
            }
        }

        // No color code found, return the original name with default color
        return (itemName, 3066993); // default green-ish
    }


    /// <summary>
    /// Renders Minecraft lore as an image with proper color codes.
    /// Returns a MemoryStream of the rendered PNG image, or null if lore is empty.
    /// </summary>
    private async Task<MemoryStream> RenderLoreAsImageAsync(string lore)
    {
        if (string.IsNullOrWhiteSpace(lore) || loreRenderer == null)
            return null;

        try
        {
            return await loreRenderer.RenderLoreAsync(lore);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to render lore as image");
            return null;
        }
    }

    private async Task SendWebhookAsync(LowballOffer offer, Sniper.Client.Model.PriceEstimate estimate, string websiteLink)
    {
        var webhookUrl = config["LOWBALL_WEBHOOK_URL"];
        if (string.IsNullOrEmpty(webhookUrl))
        {
            logger.LogDebug("LOWBALL_WEBHOOK_URL not configured; skipping webhook post");
            return;
        }

        // Build Discord-style embed payload
        var itemImage = $"https://sky.coflnet.com/static/icon/{offer.ItemTag}";
        var sellerIcon = $"https://crafatar.com/avatars/{offer.MinecraftAccount:N}";
        var name = (await DiHandler.GetService<IPlayerNameApi>().PlayerNameNameUuidGetAsync(offer.MinecraftAccount.ToString("N"))).Trim('"');

        var priceText = offer.AskingPrice.ToString("N0", CultureInfo.InvariantCulture);
        var targetValue = estimate.Median.ToString("N0", CultureInfo.InvariantCulture);
        var itemCountText = offer.ItemCount > 1 ? $" x{offer.ItemCount}" : string.Empty;

        // Extract color code from item name and convert to Discord embed color
        var (cleanItemName, embedColor) = ExtractColorAndCleanItemName(offer.ItemName);

        // Render lore as image
        MemoryStream loreImageStream = null;
        try
        {
            var fullText = offer.ItemName + "\n" + offer.Lore;
            loreImageStream = await RenderLoreAsImageAsync(fullText);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to render lore image");
        }

        // Build embed with lore image if available
        var embed = new
        {
            title = "New Lowball Offer",
            description = $"[**{cleanItemName}**{itemCountText}]({websiteLink})",
            color = embedColor,
            thumbnail = new { url = itemImage },
            image = loreImageStream != null ? new { url = "attachment://lore.png" } : null,
            fields = new[]
            {
                new { name = "Asking Price", value = priceText, inline = true },
                new { name = "Estimated Value", value = targetValue, inline = true },
                new { name = "Visit Command", value = $"/visit {name}", inline = true }
            },
            footer = new { text = "SkyCofl lowball offer" },
            timestamp = offer.CreatedAt.ToString("o")
        };

        // If we have a lore image, use multipart form data
        HttpResponseMessage resp;
        if (loreImageStream != null)
        {
            try
            {
                using var formContent = new MultipartFormDataContent();
                
                // Add the JSON payload
                var payloadJson = JsonConvert.SerializeObject(new
                {
                    username = name,
                    avatar_url = sellerIcon,
                    embeds = new[] { embed }
                });
                formContent.Add(new StringContent(payloadJson, Encoding.UTF8, "application/json"), "payload_json");
                
                // Add the lore image
                loreImageStream.Position = 0;
                var imageContent = new StreamContent(loreImageStream);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                formContent.Add(imageContent, "file", "lore.png");

                resp = await httpClient.PostAsync(webhookUrl, formContent);
            }
            finally
            {
                loreImageStream?.Dispose();
            }
        }
        else
        {
            // No lore image, send regular JSON
            var payload = new
            {
                username = name,
                avatar_url = sellerIcon,
                embeds = new[] { embed }
            };

            var json = JsonConvert.SerializeObject(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            resp = await httpClient.PostAsync(webhookUrl, content);
        }

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

    protected virtual async Task InsertOffersAsync(LowballOffer offer, LowballOfferByItem offerByItem)
    {
        await GetUserTable().Insert(offer).ExecuteAsync();
        await GetItemTable().Insert(offerByItem).ExecuteAsync();
    }

    protected virtual async Task PublishToKafka(LowballOffer offer)
    {
        try
        {
            await kafkaCreator.CreateTopicIfNotExist(KafkaTopic, 1);
            using var producer = kafkaCreator.BuildProducer<string, string>();
            var offerMessage = JsonConvert.SerializeObject(new
            {
                offerId = offer.OfferId,
                userId = offer.UserId,
                itemTag = offer.ItemTag,
                itemName = offer.ItemName,
                askingPrice = offer.AskingPrice,
                minecraftAccount = offer.MinecraftAccount,
                createdAt = offer.CreatedAt,
                apiAuctionJson = offer.ApiAuctionJson,
                itemCount = offer.ItemCount
            });
            await producer.ProduceAsync(KafkaTopic, new Confluent.Kafka.Message<string, string>
            {
                Key = offer.OfferId.ToString(),
                Value = offerMessage
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to publish lowball offer {offer.OfferId} to Kafka");
        }
    }

    public async Task<List<LowballOffer>> GetOffersByUser(string userId, DateTimeOffset? before = null, int limit = 20)
    {
        return await LoadOffersByUserAsync(userId, before, limit);
    }

    protected virtual async Task<List<LowballOffer>> LoadOffersByUserAsync(string userId, DateTimeOffset? before = null, int limit = 20)
    {
        try
        {
            var safeLimit = Math.Max(1, limit);
            var cql = before.HasValue
                ? $"SELECT * FROM lowball_offers WHERE user_id = ? AND created_at < ? LIMIT {safeLimit}"
                : $"SELECT * FROM lowball_offers WHERE user_id = ? LIMIT {safeLimit}";
            var statement = before.HasValue
                ? new SimpleStatement(cql, userId, before.Value)
                : new SimpleStatement(cql, userId);
            var rows = await session.ExecuteAsync(statement);

            return rows.Select(MapUserOffer).ToList();
        }
        catch (InvalidQueryException ex) when (ex.Message.Contains("unconfigured table", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(ex, "Lowball user table is not provisioned yet; returning no user offers");
            return new List<LowballOffer>();
        }
    }

    public async Task<List<LowballOfferByItem>> GetOffersByItem(string itemTag, Dictionary<string, string> filters = null, DateTimeOffset? before = null, int limit = 20)
    {
        var results = await LoadOffersByItemAsync(itemTag, before, limit * 3);

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

    protected virtual async Task<List<LowballOfferByItem>> LoadOffersByItemAsync(string itemTag, DateTimeOffset? before = null, int limit = 20)
    {
        try
        {
            var safeLimit = Math.Max(1, limit);
            var cql = before.HasValue
                ? $"SELECT * FROM lowball_offers_by_item WHERE item_tag = ? AND created_at < ? LIMIT {safeLimit}"
                : $"SELECT * FROM lowball_offers_by_item WHERE item_tag = ? LIMIT {safeLimit}";
            var statement = before.HasValue
                ? new SimpleStatement(cql, itemTag, before.Value)
                : new SimpleStatement(cql, itemTag);
            return (await session.ExecuteAsync(statement)).Select(MapItemOffer).ToList();
        }
        catch (InvalidQueryException ex) when (ex.Message.Contains("unconfigured table", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(ex, "Lowball item table is not provisioned yet; returning no item offers");
            return new List<LowballOfferByItem>();
        }
    }

    public async Task<bool> DeleteOffer(string userId, Guid offerId)
    {
        return await DeleteOfferAsync(userId, offerId);
    }

    protected virtual async Task<bool> DeleteOfferAsync(string userId, Guid offerId)
    {
        try
        {
            var userRows = await session.ExecuteAsync(new SimpleStatement("SELECT * FROM lowball_offers WHERE user_id = ? LIMIT 200", userId));
            var userRow = userRows.Select(MapUserOffer).FirstOrDefault(row => row.OfferId == offerId);
            if (userRow == null)
                return false;

            if (userRow.UserId != userId)
                return false;

            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM lowball_offers WHERE user_id = ? AND created_at = ? AND offer_id = ?",
                userId,
                userRow.CreatedAt,
                offerId));

            await session.ExecuteAsync(new SimpleStatement(
                "DELETE FROM lowball_offers_by_item WHERE item_tag = ? AND created_at = ? AND offer_id = ?",
                userRow.ItemTag,
                userRow.CreatedAt,
                offerId));

            logger.LogInformation($"Deleted lowball offer {offerId} for user {userId}");
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Failed to delete lowball offer {offerId}");
            return false;
        }
    }

    private static LowballOffer MapUserOffer(Row row)
    {
        return new LowballOffer
        {
            UserId = GetValueOrDefault(row, "user_id", string.Empty),
            CreatedAt = GetValueOrDefault(row, "created_at", default(DateTimeOffset)),
            OfferId = GetValueOrDefault(row, "offer_id", Guid.Empty),
            ItemTag = GetValueOrDefault(row, "item_tag", string.Empty),
            MinecraftAccount = GetValueOrDefault(row, "minecraft_account", Guid.Empty),
            ItemName = GetValueOrDefault(row, "item_name", string.Empty),
            ApiAuctionJson = GetValueOrDefault(row, "api_auction_json", string.Empty),
            Filters = GetValueOrDefault(row, "filters", string.Empty),
            AskingPrice = GetValueOrDefault(row, "asking_price", 0L),
            Lore = GetValueOrDefault(row, "lore", string.Empty),
            ItemCount = GetValueOrDefault(row, "item_count", 0),
        };
    }

    private static LowballOfferByItem MapItemOffer(Row row)
    {
        return new LowballOfferByItem
        {
            ItemTag = GetValueOrDefault(row, "item_tag", string.Empty),
            CreatedAt = GetValueOrDefault(row, "created_at", default(DateTimeOffset)),
            OfferId = GetValueOrDefault(row, "offer_id", Guid.Empty),
            UserId = GetValueOrDefault(row, "user_id", string.Empty),
            MinecraftAccount = GetValueOrDefault(row, "minecraft_account", Guid.Empty),
            ItemName = GetValueOrDefault(row, "item_name", string.Empty),
            ApiAuctionJson = GetValueOrDefault(row, "api_auction_json", string.Empty),
            Filters = GetValueOrDefault(row, "filters", string.Empty),
            AskingPrice = GetValueOrDefault(row, "asking_price", 0L),
            Lore = GetValueOrDefault(row, "lore", string.Empty),
            ItemCount = GetValueOrDefault(row, "item_count", 0),
        };
    }

    private static T GetValueOrDefault<T>(Row row, string columnName, T defaultValue)
    {
        try
        {
            return row.IsNull(columnName) ? defaultValue : row.GetValue<T>(columnName);
        }
        catch
        {
            return defaultValue;
        }
    }
}
