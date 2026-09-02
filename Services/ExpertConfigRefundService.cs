using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Coflnet.Sky.ModCommands.Services;

public sealed class ExpertConfigRefundService : BackgroundService
{
    private const string RevertPrefix = "revert transaction ";
    private readonly IConfiguration configuration;
    private readonly ILogger<ExpertConfigRefundService> logger;
    private readonly SettingsService settings;

    public ExpertConfigRefundService(
        IConfiguration configuration,
        ILogger<ExpertConfigRefundService> logger,
        SettingsService settings)
    {
        this.configuration = configuration;
        this.logger = logger;
        this.settings = settings;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Kafka.KafkaConsumer.Consume(
            configuration,
            configuration["TOPICS:TRANSACTIONS"],
            Revoke,
            stoppingToken,
            "sky-modcommands-expert-config-refunds",
            AutoOffsetReset.Earliest,
            new TransactionDeserializer());

    private async Task Revoke(TransactionEvent transaction)
    {
        if (!TryGetRefundedPurchaseId(
                transaction.ProductSlug,
                transaction.RevertedProductSlug,
                transaction.Reference,
                out var purchaseId))
            return;
        await using var ownedLock = await OwnedConfigLock.Acquire(
            settings, transaction.UserId);
        using var configs = await SelfUpdatingValue<OwnedConfigs>.Create(
            transaction.UserId,
            "owned_configs",
            () => new());
        var matching = configs.Value.Configs.Where(config =>
            config.PurchaseTransactionId == purchaseId).ToList();
        var recorded = configs.Value.RevertedPurchaseIds.Add(purchaseId);
        var revoked = RevokeRefundedAccess(
            configs.Value, purchaseId, DateTime.UtcNow);
        if (recorded || revoked.Count > 0)
            await configs.Update();
        if (!matching.Any(revokedConfig => configs.Value.Configs.Any(active =>
                active.RevokedAtUtc == null
                && active.OwnerId == revokedConfig.OwnerId
                && active.Name.Equals(revokedConfig.Name,
                    StringComparison.OrdinalIgnoreCase))))
            await ResetLoaded(settings, transaction.UserId, matching);
        if (!recorded && revoked.Count == 0)
            return;
        logger.LogInformation(
            "Revoked Expert Config licence and managed copy for reverted transaction {TransactionId} from {UserId}",
            purchaseId,
            transaction.UserId);
    }

    internal static bool TryGetRefundedPurchaseId(
        string productSlug,
        string revertedProductSlug,
        string reference,
        out long purchaseId)
    {
        purchaseId = 0;
        return productSlug == "revert"
            && revertedProductSlug == "config-purchase"
            && reference?.StartsWith(RevertPrefix,
                StringComparison.Ordinal) == true
            && long.TryParse(reference[RevertPrefix.Length..], out purchaseId);
    }

    internal static List<OwnedConfigs.OwnedConfig> RevokeRefundedAccess(
        OwnedConfigs configs,
        long purchaseId,
        DateTime revokedAtUtc)
    {
        var revoked = configs.Configs.Where(config =>
            config.PurchaseTransactionId == purchaseId
            && config.RevokedAtUtc == null).ToList();
        foreach (var config in revoked)
            config.RevokedAtUtc = revokedAtUtc;
        return revoked;
    }

    internal static async Task<FlipSettings> ResetLoaded(
        SettingsService settings,
        string userId,
        IReadOnlyCollection<OwnedConfigs.OwnedConfig> revoked,
        bool persist = true)
    {
        if (revoked.Count == 0)
            return null;
        var account = await settings.GetCurrentValue(
            userId, "accountSettings", () => new AccountSettings());
        if (account?.LoadedConfig == null)
            return null;
        var loaded = await GetLoadedManagedSettings(settings, userId, account);
        if (!revoked.Any(config => Matches(config, account.LoadedConfig)
                || Matches(config, loaded.BaseAccess)))
            return null;
        var current = await settings.GetCurrentValue(
            userId, "flipSettings", () => ModSessionLifesycle.DefaultSettings);
        var userSettings = SettingsDiffer.GetUserDifferenceConfig(
            current, loaded.Settings);
        if (persist)
        {
            await settings.UpdateSetting(userId, "flipSettings", userSettings);
            account.LoadedConfig = null;
            account.BaseConfigVersion = 0;
            await settings.UpdateSetting(userId, "accountSettings", account);
        }
        return userSettings;

        static bool Matches(
            OwnedConfigs.OwnedConfig left,
            OwnedConfigs.OwnedConfig right) => right != null
                && left.OwnerId == right.OwnerId
                && left.Name.Equals(right.Name,
                    StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task<(FlipSettings Settings,
        OwnedConfigs.OwnedConfig BaseAccess)> GetLoadedManagedSettings(
        SettingsService settings,
        string userId,
        AccountSettings account)
    {
        var primary = await SellConfigCommand.GetArchived(
            settings,
            account.LoadedConfig.OwnerId,
            account.LoadedConfig.Name,
            account.LoadedConfig.Version)
            ?? throw new InvalidOperationException(
                "The loaded Config version is missing from the publication archive");
        var owned = await settings.GetCurrentValue(
            userId, "owned_configs", () => new OwnedConfigs());
        var baseAccess = FindBaseAccess(owned, primary.Settings.BasedConfig);
        ConfigContainer baseConfig = null;
        if (baseAccess != null)
            baseConfig = await SellConfigCommand.GetArchived(
                settings, baseAccess.OwnerId, baseAccess.Name,
                account.BaseConfigVersion)
                ?? throw new InvalidOperationException(
                    "The loaded base Config version is missing from the publication archive");
        return new(
            LoadConfigCommand.BuildManagedSettings(primary, baseConfig, userId),
            baseAccess);

        static OwnedConfigs.OwnedConfig FindBaseAccess(
            OwnedConfigs owned,
            string reference)
        {
            var parts = reference?.Split(':');
            if (parts?.Length != 2)
                return null;
            return owned.Configs.FirstOrDefault(config =>
                config.Name.Equals(parts[1], StringComparison.OrdinalIgnoreCase)
                && (config.OwnerId == parts[0]
                    || config.OwnerName?.Equals(
                        parts[0], StringComparison.OrdinalIgnoreCase) == true));
        }
    }

    private sealed class TransactionDeserializer : IDeserializer<TransactionEvent>
    {
        public TransactionEvent Deserialize(
            ReadOnlySpan<byte> data,
            bool isNull,
            SerializationContext context) => JsonConvert.DeserializeObject<TransactionEvent>(
                System.Text.Encoding.UTF8.GetString(data));
    }

    private sealed class TransactionEvent
    {
        public string UserId { get; set; }
        public string ProductSlug { get; set; }
        public string RevertedProductSlug { get; set; }
        public string Reference { get; set; }
    }
}
