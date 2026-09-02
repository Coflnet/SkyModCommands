using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Payments.Client.Api;
using Coflnet.Payments.Client.Model;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Services;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.MC;

public class BuyConfigCommand : ArgumentsCommand
{
    private const string PurchaseProduct = "config-purchase";
    internal const int UpdateTermYears = 5;
    internal const int UpdateExtensionYears = 2;
    protected override string Usage =>
        "<sellerIgn> <configName> [confirmId=none] [offer=none] [requestId=none]";

    public override async Task Execute(MinecraftSocket socket, string arguments)
    {
        var parts = (arguments ?? "").Trim('"').Split(
            ' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.FirstOrDefault() == "accept")
        {
            if (parts.Length != 3)
            {
                socket.SendMessage("Use the acceptance button shown by /cofl buyconfig.");
                return;
            }
            await CurrentAgreement.AcceptMarketplace(socket, parts[1], parts[2]);
            socket.SendMessage(
                "The Expert Marketplace agreement was accepted. Run buyconfig again.");
            return;
        }
        await base.Execute(socket, arguments);
    }

    protected override async Task Execute(IMinecraftSocket socket, Arguments args)
    {
        var seller = args["sellerIgn"];
        var name = args["configName"];
        using var configs = await SelfUpdatingValue<OwnedConfigs>.Create(socket.UserId, "owned_configs", () => new());
        var key = SellConfigCommand.GetKeyFromname(name);
        var sellerUserId = await GetUserIdFromMcName(socket, seller);
        using var toBebought = await SelfUpdatingValue<ConfigContainer>.Create(sellerUserId, key, () => null);
        if (toBebought.Value == null)
        {
            socket.SendMessage("The config doesn't exist.");
            return;
        }
        var owned = configs.Value.Configs.FirstOrDefault(
            c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                && c.OwnerId == sellerUserId
                && c.RevokedAtUtc == null);
        if (owned?.PurchaseTransactionId > 0
            && (configs.Value.RevertedPurchaseIds.Contains(
                    owned.PurchaseTransactionId)
                || IsReverted(
                    await GetTransactions(socket),
                    owned.PurchaseTransactionId)))
        {
            owned.RevokedAtUtc = DateTime.UtcNow;
            await configs.Update();
            owned = null;
        }
        if (owned != null)
        {
            if (owned.PurchaseTransactionId == 0
                && toBebought.Value.Price > 0
                && FindUnrevertedPurchase(
                    await GetTransactions(socket),
                    PurchaseReferencePrefix(sellerUserId, key)) != null)
                throw new CoflnetException(
                    "purchase_delivery_conflict",
                    "A paid order is awaiting delivery while another access grant exists. Contact support for supply or a refund; you will not be charged again.");
            if (owned.PurchaseTransactionId > 0
                && owned.RewardPendingId.HasValue)
                await socket.GetService<RewardLedgerClient>().RecordAvailable(
                    owned.PurchaseTransactionId,
                    sellerUserId,
                    owned.CreatorFeeEurCents,
                    owned.RewardPendingId.Value);
            socket.Dialog(db => db.CoflCommand<LoadConfigCommand>(
                $"You already own this config. {McColorCodes.YELLOW}[CLICK to load]",
                $"{sellerUserId} {name}",
                $"Click here to load the config\n{McColorCodes.AQUA}/cofl loadconfig {sellerUserId} {name}"));
            return;
        }
        ExternalTransaction resumablePurchase = null;
        if (toBebought.Value.Delisted)
        {
            if (toBebought.Value.Price > 0)
                resumablePurchase = FindUnrevertedPurchase(
                    await GetTransactions(socket),
                    PurchaseReferencePrefix(sellerUserId, key));
            if (resumablePurchase != null
                && long.TryParse(resumablePurchase.Id, out var resumableId)
                && configs.Value.RevertedPurchaseIds.Contains(resumableId))
                resumablePurchase = null;
            if (resumablePurchase == null)
            {
                socket.SendMessage(
                    "This config is delisted and unavailable for new acquisitions.");
                return;
            }
        }
        var sellerUuid = toBebought.Value.OwnerMinecraftUuid;
        var creatorAgreement = await CurrentAgreement.GetCreator();
        var freePublisher = toBebought.Value.Price == 0
            && SellConfigCommand.IsFreePublisher(sellerUuid);
        CreatorEligibility creatorEligibility = null;
        if (!freePublisher)
            creatorEligibility = await socket.GetService<RewardLedgerClient>()
                .GetCreatorEligibility(
                    sellerUserId, sellerUuid, creatorAgreement.Hash);
        if ((!freePublisher && creatorEligibility?.Eligible != true)
            || !await CurrentAgreement.HasCreatorAcceptance(sellerUserId))
        {
            socket.SendMessage(
                "This config is unavailable until its creator is manually approved and accepts the current Creator Marketplace agreement through /cofl sellconfig.");
            return;
        }
        var unitPrice = toBebought.Value.Price > 0
            ? await GetPurchaseUnitPrice(socket)
            : 1;
        if (toBebought.Value.Price % unitPrice != 0)
        {
            socket.SendMessage(
                "This config's price cannot currently be charged exactly. Ask the Expert to republish it.");
            return;
        }
        ExpertConfigQuote quote = null;
        if (toBebought.Value.Price > 0)
        {
            if (creatorEligibility?.PaidPublicationReady != true)
            {
                socket.SendMessage(
                    "This creator is currently approved for free Configs only.");
                return;
            }
            await socket.GetService<RewardLedgerClient>().EnsureReady();
            quote = await socket.GetService<ExpertConfigCheckoutClient>().GetQuote(
                socket.UserId,
                toBebought.Value.Price / unitPrice);
        }
        var marketplace = await CurrentAgreement.RequireMarketplace(
            socket,
            quote?.ConsumerRightsRegime);
        if (marketplace == null)
            return;
        var rewards = socket.GetService<RewardLedgerClient>();
        if (quote != null
            && (quote.CoinAmount <= 0
                || quote.CoinAmount > toBebought.Value.Price
                || quote.CoinAmount != decimal.Truncate(quote.CoinAmount)
                || quote.GrossEurCents != rewards.GrossEurCents(
                    quote.CoinAmount)))
            throw new CoflnetException(
                "purchase_unavailable",
                "The Expert Config valuation is inconsistent between checkout services.");
        var offer = quote == null
            ? $"{toBebought.Value.Version}-{toBebought.Value.Price}-{unitPrice}-{UpdateTermYears}-{UpdateExtensionYears}"
            : $"{toBebought.Value.Version}-{toBebought.Value.Price}-{unitPrice}-{UpdateTermYears}-{UpdateExtensionYears}-{quote.CoinAmount}-{quote.TaxCountry}-{quote.VatRateBasisPoints}-{quote.GrossEurCents}-{quote.VatEurCents}-{quote.ConsumerRightsRegime}";
        if (args["confirmId"] != socket.SessionInfo.SessionId
            || args["offer"] != offer)
        {
            var requestId = toBebought.Value.Price > 0
                ? PurchaseRequestId(
                    socket.UserId,
                    PurchaseReferencePrefix(sellerUserId, key),
                    offer,
                    await GetTransactions(socket))
                : Guid.Empty;
            var summary = $"This config has {toBebought.Value.Settings.WhiteList.Count} whitelist entries and {toBebought.Value.Settings.BlackList.Count} blacklist entries.\n"
                + $"It was last updated {McColorCodes.GREEN}{socket.formatProvider.FormatTime(DateTime.UtcNow - toBebought.Value.LastUpdated)} ago{McColorCodes.RESET}. It is version {McColorCodes.AQUA}{toBebought.Value.Version}{McColorCodes.RESET} and has the following change notes:\n{McColorCodes.GRAY}{toBebought.Value.ChangeNotes}";
            var price = toBebought.Value.Price;
            socket.Dialog(db => db
                .MsgLine($"Coflnet GmbH is the seller. {seller} is the Expert. Purchaser and Recipient: {socket.SessionInfo.McName}.")
                .MsgLine($"You receive a personal, non-transferable licence to this Config version and a {UpdateTermYears}-year managed update facility. Coflnet may extend the update facility by {UpdateExtensionYears} years at no charge, but no extension is promised.")
                .MsgLine("You receive later versions only if the Expert publishes them; the Expert does not promise updates or improvements. Permanent shutdown ends optional Expert updates and makes the latest version available to download for six months.")
                .MsgLine("For paid orders, the Config licence and update facility are supplied immediately after the order confirmation email is delivered. Expert settings cannot otherwise be exported or redistributed; personal overrides can be exported separately.")
                .MsgLine(summary)
                .If(() => price > 0, paid => paid
                    .MsgLine(rewards.Describe(quote, price))
                    .MsgLine(marketplace.Purchase.DeclarationText)
                    .MsgLine($"{McColorCodes.AQUA}[Withdrawal information]",
                        marketplace.Purchase.WithdrawalUrl,
                        "Open the withdrawal information"))
                .CoflCommand<BuyConfigCommand>(
                    price == 0
                        ? $"Confirm adding free config §6{toBebought.Value.Name} §7v{toBebought.Value.Version} {McColorCodes.YELLOW}[CLICK]"
                        : $"Buy §6{toBebought.Value.Name} §7v{toBebought.Value.Version} for §6{quote.CoinAmount:0.##} CoflCoins {McColorCodes.YELLOW}[CLICK]",
                    $"{seller} {name} {socket.SessionInfo.SessionId} {offer}"
                        + (price > 0 ? $" {requestId:D}" : ""),
                    price == 0
                        ? $"§aAdd {toBebought.Value.Name} for managed use in Coflnet?"
                        : $"§aPay {quote.CoinAmount:0.##} CoflCoins, request immediate supply and accept the withdrawal consequences shown above."));
            return;
        }
        var updateStartsAtUtc = DateTime.UtcNow;
        var updateUntilUtc = updateStartsAtUtc.AddYears(UpdateTermYears);
        ExternalTransaction purchase = resumablePurchase;
        Guid? pendingId = null;
        if (toBebought.Value.Price > 0)
        {
            if (!Guid.TryParse(args["requestId"], out var requestId))
                throw new CoflnetException("invalid_purchase", "Restart the Expert Config purchase.");
            var referencePrefix = PurchaseReferencePrefix(sellerUserId, key);
            var transactions = await GetTransactions(socket);
            purchase ??= FindUnrevertedPurchase(transactions, referencePrefix);
            var expectedPrefix = $"{referencePrefix}{toBebought.Value.Version}:{toBebought.Value.Price}:";
            if (purchase != null
                && (!purchase.Reference.StartsWith(expectedPrefix,
                        StringComparison.Ordinal)
                    || System.Convert.ToDecimal(Math.Abs(purchase.Amount))
                        != quote.CoinAmount))
                throw new CoflnetException(
                    "purchase_changed",
                    "A paid version of this config is awaiting delivery, but the offer changed. Contact support for supply or a refund.");
            if (purchase == null)
            {
                var reference = expectedPrefix + requestId.ToString("D");
                var disclosure = marketplace.Purchase;
                var orderDetails = BuildOrderDetails(
                    socket,
                    seller,
                    toBebought.Value,
                    quote,
                    marketplace.Agreement.Id,
                    marketplace.Agreement.Hash,
                    creatorAgreement.Hash,
                    updateStartsAtUtc,
                    updateUntilUtc);
                try
                {
                    await socket.GetService<ExpertConfigCheckoutClient>().Purchase(
                        socket.UserId,
                        new
                        {
                            reference,
                            count = toBebought.Value.Price / unitPrice,
                            immediatePerformanceRequested = true,
                            withdrawalConsequenceAcknowledged = true,
                            locale = marketplace.Locale,
                            declarationVersion = marketplace.DeclarationVersion,
                            marketplace.ConsumerRightsRegime,
                            disclosure.DeclarationText,
                            disclosure.DeclarationSha256,
                            agreementId = marketplace.Agreement.Id,
                            agreementHash = marketplace.Agreement.Hash,
                            disclosure.WithdrawalVersion,
                            disclosure.WithdrawalSha256,
                            quote.TaxCountry,
                            quote.VatRateBasisPoints,
                            quote.GrossEurCents,
                            quote.VatEurCents,
                            orderDetailsJson = orderDetails,
                            requestId = requestId.ToString("D")
                        });
                }
                catch (HttpRequestException exception)
                {
                    socket.Dialog(db => db.MsgLine(McColorCodes.RED + "Purchase failed")
                        .Msg(exception.Message)
                        .If(() => exception.Message.Contains("insuficcient balance"), db =>
                            db.CoflCommand<TopUpCommand>(
                                McColorCodes.AQUA + "Click here to top up coins",
                                "",
                                "Click here to buy coins")));
                    return;
                }
                purchase = (await GetTransactions(socket)).FirstOrDefault(transaction =>
                    transaction.ProductId == PurchaseProduct
                    && transaction.Reference == reference
                    && transaction.Amount < 0);
            }
            if (purchase == null)
                throw new CoflnetException(
                    "purchase_incomplete",
                    "The payment could not be confirmed yet. Run buyconfig again to resume safely.");
            if (!long.TryParse(purchase.Id, out var transactionId))
                throw new CoflnetException(
                    "purchase_incomplete",
                    "The Expert Config transaction ID is invalid.");
            await socket.GetService<ExpertConfigCheckoutClient>()
                .WaitForConfirmation(transactionId);
            if (IsReverted(await GetTransactions(socket), transactionId))
                throw new CoflnetException(
                    "purchase_reverted",
                    "This purchase was reverted, so the Config licence was not granted.");
            if (configs.Value.RevertedPurchaseIds.Contains(transactionId))
                throw new CoflnetException(
                    "purchase_reverted",
                    "This purchase was reverted, so the Config licence was not granted.");
            pendingId = await RecordFee(socket, purchase, sellerUserId, toBebought.Value,
                creatorAgreement.Hash, quote);
        }
        await FinishPurchase(
            socket, seller, name, sellerUserId, toBebought, purchase,
            pendingId, quote, updateStartsAtUtc, updateUntilUtc);

    }

    internal static Task<List<ExternalTransaction>> GetTransactions(
        IMinecraftSocket socket) => socket.GetService<ITransactionApi>()
            .TransactionUUserIdGetAsync(socket.UserId, 0, 2000);

    private static ExternalTransaction FindUnrevertedPurchase(
        IEnumerable<ExternalTransaction> transactions,
        string referencePrefix) => transactions.FirstOrDefault(transaction =>
            transaction.ProductId == PurchaseProduct
            && transaction.Reference?.StartsWith(
                referencePrefix,
                StringComparison.Ordinal) == true
            && transaction.Amount < 0
            && long.TryParse(transaction.Id, out var id)
            && !IsReverted(transactions, id));

    internal static bool IsReverted(
        IEnumerable<ExternalTransaction> transactions,
        long transactionId) => transactions.Any(transaction =>
            transaction.ProductId == "revert"
            && transaction.Reference == $"revert transaction {transactionId}");

    private static string PurchaseReferencePrefix(
        string sellerUserId,
        string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{sellerUserId}\0{key}"));
        return $"ec3:{System.Convert.ToHexString(hash)[..16].ToLowerInvariant()}:";
    }

    private static Guid PurchaseRequestId(
        string buyerUserId,
        string referencePrefix,
        string offer,
        IReadOnlyCollection<ExternalTransaction> transactions)
    {
        var completedAttempts = transactions.Count(transaction =>
            transaction.ProductId == PurchaseProduct
            && transaction.Reference?.StartsWith(
                referencePrefix, StringComparison.Ordinal) == true
            && long.TryParse(transaction.Id, out var id)
            && IsReverted(transactions, id));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{buyerUserId}\0{referencePrefix}\0{offer}\0{completedAttempts}"));
        return new Guid(hash[..16]);
    }

    private static string BuildOrderDetails(
        IMinecraftSocket socket,
        string seller,
        ConfigContainer config,
        ExpertConfigQuote quote,
        string agreementId,
        string agreementHash,
        string creatorAgreementHash,
        DateTime updateStartsAtUtc,
        DateTime updateUntilUtc) => JsonConvert.SerializeObject(new
        {
            schemaVersion = 3,
            orderType = "expert-config-license-and-updates",
            seller = "Coflnet GmbH, Dorfstraße 27a, 84163 Marklkofen, Germany",
            expert = seller,
            config = new
            {
                config.Name,
                config.Version,
                config.ChangeNotes,
                config.LastUpdated,
                whitelistEntries = config.Settings.WhiteList.Count,
                blacklistEntries = config.Settings.BlackList.Count,
                licence = "Personal, non-exclusive and non-transferable licence to the identified Config version and later versions actually supplied.",
                updateInformation = "The managed update facility supplies later versions only if the Expert publishes them. The Expert does not promise updates or improvements. Legally required conformity or security fixes and remedies remain unaffected."
            },
            purchaser = socket.SessionInfo.McName,
            recipient = socket.SessionInfo.McName,
            totalPriceCoflCoins = quote.CoinAmount,
            listedPriceCoflCoins = config.Price,
            coflnetFundedPromotionCoflCoins = config.Price - quote.CoinAmount,
            quote.GrossEurCents,
            quote.VatEurCents,
            quote.TaxCountry,
            quote.VatRateBasisPoints,
            quote.ConsumerRightsRegime,
            creatorFeeEurCents = socket.GetService<RewardLedgerClient>()
                .CreatorFeeEurCents(config.Price),
            creatorFeeRule = "eur-0.70-per-300-listed-coflcoins-rounded-down",
            managedUseRestriction = "Expert settings cannot be exported or redistributed except for the personal, non-transferable shutdown copy; personal overrides may be exported separately.",
            initialUpdateTermYears = UpdateTermYears,
            optionalUpdateExtensionYears = UpdateExtensionYears,
            updateStartsAtUtc,
            updateUntilUtc,
            paidUpdateRenewal = false,
            freeUpdateExtension = $"Coflnet may extend the managed update facility by {UpdateExtensionYears} years without charge, but no extension is promised.",
            shutdownExportMonths = 6,
            shutdownEffect = "Permanent shutdown ends optional Expert updates. The latest version lawfully available to the Recipient may be downloaded for six months and kept for personal use.",
            supplyTime = "The Config licence and managed update facility are supplied immediately after this confirmation email is delivered.",
            refundDestination = "The purchaser's CoflCoin balance.",
            acceptedAgreement = new { id = agreementId, hash = agreementHash },
            creatorAgreementHash
        });

    internal static async Task<int> GetPurchaseUnitPrice(IMinecraftSocket socket)
    {
        var cost = (await socket.GetService<IProductsApi>()
            .ProductsPProductSlugGetAsync(PurchaseProduct)).Cost;
        if (cost <= 0 || cost != Math.Truncate(cost) || cost > int.MaxValue)
            throw new CoflnetException(
                "purchase_unavailable",
                "The Expert Config checkout unit is invalid.");
        return System.Convert.ToInt32(cost);
    }

    private static async Task<Guid> RecordFee(
        IMinecraftSocket socket,
        ExternalTransaction purchase,
        string sellerUserId,
        ConfigContainer config,
        string creatorAgreementHash,
        ExpertConfigQuote quote)
    {
        if (!long.TryParse(purchase.Id, out var transactionId))
            throw new CoflnetException(
                "purchase_incomplete", "The Expert Config transaction ID is invalid.");
        var rewards = socket.GetService<RewardLedgerClient>();
        return await rewards.RecordPending(
            transactionId,
            sellerUserId,
            creatorAgreementHash,
            socket.UserId,
            config.Name,
            config.Version,
            config.Price,
            quote);
    }

    private static async Task FinishPurchase(
        IMinecraftSocket socket,
        string seller,
        string name,
        string sellerUserId,
        SelfUpdatingValue<ConfigContainer> toBebought,
        ExternalTransaction purchase,
        Guid? pendingId,
        ExpertConfigQuote quote,
        DateTime updateStartsAtUtc,
        DateTime updateUntilUtc)
    {
        await using var ownedLock = await OwnedConfigLock.Acquire(
            socket.GetService<SettingsService>(), socket.UserId);
        using var configs = await SelfUpdatingValue<OwnedConfigs>.Create(
            socket.UserId, "owned_configs", () => new());
        var owned = new OwnedConfigs.OwnedConfig()
        {
            Name = name,
            Version = toBebought.Value.Version,
            ChangeNotes = toBebought.Value.ChangeNotes,
            OwnerId = sellerUserId,
            PricePaid = quote == null ? 0 : decimal.ToInt32(quote.CoinAmount),
            OwnerName = seller,
            BoughtAt = updateStartsAtUtc,
            AccessUntilUtc = updateUntilUtc,
            PurchaseTransactionId = purchase == null ? 0 : long.Parse(purchase.Id),
            RewardPendingId = pendingId,
            CreatorFeeEurCents = quote == null
                ? 0
                : socket.GetService<RewardLedgerClient>()
                    .CreatorFeeEurCents(toBebought.Value.Price)
        };
        if (owned.PurchaseTransactionId > 0
            && configs.Value.RevertedPurchaseIds.Contains(
                owned.PurchaseTransactionId))
            throw new CoflnetException(
                "purchase_reverted",
                "This purchase was reverted, so the Config licence was not granted.");
        var existing = configs.Value.Configs.FirstOrDefault(config =>
            config.RevokedAtUtc == null
            && config.OwnerId == sellerUserId
            && config.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null
            && existing.PurchaseTransactionId != owned.PurchaseTransactionId)
            throw new CoflnetException(
                "purchase_delivery_conflict",
                "Another access grant exists for this Config. Contact support for supply or a refund; you will not be charged again.");
        if (existing == null)
        {
            configs.Value.Configs.Add(owned);
            await configs.Update();
        }
        else
            owned = existing;
        if (purchase != null)
        {
            if (configs.Value.RevertedPurchaseIds.Contains(
                    owned.PurchaseTransactionId)
                || IsReverted(await GetTransactions(socket),
                    owned.PurchaseTransactionId))
            {
                owned.RevokedAtUtc = DateTime.UtcNow;
                await configs.Update();
                throw new CoflnetException(
                    "purchase_reverted",
                    "This purchase was reverted, so the Config licence was not granted.");
            }
            await socket.GetService<RewardLedgerClient>().RecordAvailable(
                long.Parse(purchase.Id),
                sellerUserId,
                owned.CreatorFeeEurCents,
                owned.RewardPendingId ?? pendingId.Value);
            socket.Dialog(db => db.MsgLine(
                $"§6{toBebought.Value.Name} §7v{toBebought.Value.Version} §6bought"));
        }
        else
            socket.Dialog(db => db.MsgLine(
                $"Free config §6{toBebought.Value.Name} §7v{toBebought.Value.Version} §fadded"));
        socket.ExecuteCommand($"/cofl loadconfig {sellerUserId} {name}");
    }

    internal static bool HasManagedUpdates(OwnedConfigs.OwnedConfig config) =>
        config?.RevokedAtUtc == null
        && (config.AccessUntilUtc == null
            || config.AccessUntilUtc > DateTime.UtcNow);
}
