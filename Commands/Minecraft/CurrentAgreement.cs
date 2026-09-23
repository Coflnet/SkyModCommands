using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using IndexerTermsAcceptance = Coflnet.Sky.Indexer.Client.Model.TermsAcceptance;

namespace Coflnet.Sky.Commands.MC;

internal static class CurrentAgreement
{
    private const string CreatorAgreementId = "creatorMarketplace";
    private const string MarketplaceAgreementId = "expertMarketplace";
    internal const string ExpertMarketplaceHash =
        "d477358662b81d464396331ff79511db1623d5ad5c77809e3b92a5f0ce50acfe";
    internal const string CreatorMarketplaceHash =
        "571ab277e36066ac89929fd72304096eb5b15dd324d8cb3718808a0454b76ddb";
    private const string PreviewUserId = "7";
    private static volatile AgreementSnapshot current;
    private static volatile AgreementSnapshot marketplace;
    private static volatile AgreementSnapshot creator;
    private static readonly TaskCompletionSource<AgreementSnapshot> loaded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TaskCompletionSource<AgreementSnapshot> creatorLoaded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TaskCompletionSource<AgreementSnapshot> marketplaceLoaded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static void Initialize(AgreementSnapshot agreement)
    {
        current = agreement ?? throw new ArgumentNullException(nameof(agreement));
        loaded.TrySetResult(agreement);
    }

    internal static void InitializeExpertConfig(
        AgreementSnapshot marketplaceAgreement,
        AgreementSnapshot creatorAgreement)
    {
        if (marketplaceAgreement?.Id != MarketplaceAgreementId
            || marketplaceAgreement.Hash != ExpertMarketplaceHash
            || creatorAgreement?.Id != CreatorAgreementId
            || creatorAgreement.Hash != CreatorMarketplaceHash)
            throw new InvalidOperationException(
                "The Expert Config rollout agreement roots do not match the pinned release.");
        marketplace = marketplaceAgreement;
        creator = creatorAgreement;
        marketplaceLoaded.TrySetResult(marketplaceAgreement);
        creatorLoaded.TrySetResult(creatorAgreement);
    }

    internal static bool ExpertConfigAvailable(string userId, DateTime utcNow)
    {
        var marketplaceAgreement = marketplace;
        var creatorAgreement = creator;
        var effectiveFrom = marketplaceAgreement?.EffectiveFromUtc
            > creatorAgreement?.EffectiveFromUtc
                ? marketplaceAgreement.EffectiveFromUtc
                : creatorAgreement?.EffectiveFromUtc;
        return marketplaceAgreement?.Hash == ExpertMarketplaceHash
            && creatorAgreement?.Hash == CreatorMarketplaceHash
            && (userId == PreviewUserId
                || utcNow >= effectiveFrom);
    }

    private static void RequireExpertConfigAvailable(string userId)
    {
        if (!ExpertConfigAvailable(userId, DateTime.UtcNow))
            throw new CoflnetException(
                "expert_config_rollout_pending",
                "Expert Config publishing and acquisition are not available yet.");
    }

    internal static async Task RequestOnLogin(MinecraftSocket socket)
    {
        var agreement = current;
        if (agreement == null)
            try
            {
                agreement = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                return;
            }
        if (!int.TryParse(socket.UserId, out var userId))
            return;
        if (await UserService.Instance.GetAgreementAcceptance(
                userId,
                agreement.Id,
                agreement.Hash) == null)
            Ask(socket);
    }

    internal static void Ask(IMinecraftSocket socket)
    {
        var agreement = current ?? throw new CoflnetException(
            "legal_manifest_unavailable",
            "The current SkyCofl agreement is temporarily unavailable.");
        Ask(socket, agreement,
            "Please review the updated SkyCofl terms below. Other terms are unchanged. Existing users may continue under previously accepted terms, but new purchases require current acceptance.",
            $"/cofl terms {agreement.Hash} {Language(socket)}",
            "Accept agreement package",
            agreement.Documents.Where(document =>
                document.EffectiveFromUtc == agreement.EffectiveFromUtc));
    }

    internal static async Task<bool> RequireCreator(MinecraftSocket socket)
    {
        RequireExpertConfigAvailable(socket.UserId);
        var agreement = await GetCreator();
        if (await HasCreatorAcceptance(socket.UserId, agreement))
            return true;
        Ask(socket, agreement,
            "Before publishing an Expert Config, review and expressly accept the Creator Marketplace agreement. Acceptance is required even if you sold configs before.",
            $"/cofl sellconfig accept {agreement.Hash} {Language(socket)}",
            "Accept Creator agreement");
        return false;
    }

    internal static async Task<bool> HasCreatorAcceptance(string userId) =>
        await HasCreatorAcceptance(userId, await GetCreator());

    internal static async Task<bool> HasMarketplaceAcceptance(string userId) =>
        await HasCreatorAcceptance(userId, await GetMarketplace());

    internal static async Task<MarketplacePurchaseContext> RequireMarketplace(
        IMinecraftSocket socket,
        string consumerRightsRegime = null)
    {
        RequireExpertConfigAvailable(socket.UserId);
        var agreement = await GetMarketplace();
        if (!await HasCreatorAcceptance(socket.UserId, agreement))
        {
            Ask(socket, agreement,
                "Before acquiring an Expert Config, review and accept the current Expert Marketplace agreement.",
                $"/cofl buyconfig accept {agreement.Hash} {Language(socket)}",
                "Accept Expert Marketplace agreement");
            return null;
        }
        var language = Language(socket);
        var regime = consumerRightsRegime ?? "EU";
        if (!agreement.Purchase.Regimes.TryGetValue(regime, out var purchase))
            throw new CoflnetException(
                "purchase_unavailable",
                "Expert Config purchases are not supported for this country.");
        return new(
            agreement,
            purchase.Locales[language],
            language,
            regime,
            purchase.DeclarationVersion);
    }

    private static async Task<bool> HasCreatorAcceptance(
        string userId,
        AgreementSnapshot agreement) =>
        int.TryParse(userId, out var id)
        && await UserService.Instance.GetAgreementAcceptance(
            id, agreement.Id, agreement.Hash) != null;

    private static void Ask(
        IMinecraftSocket socket,
        AgreementSnapshot agreement,
        string introduction,
        string command,
        string button,
        IEnumerable<AgreementDocumentSnapshot> documents = null)
    {
        var language = Language(socket);
        socket.Dialog(dialog => dialog
            .MsgLine(introduction)
            .ForEach(documents ?? agreement.Documents, (builder, document) => builder.MsgLine(
                $"{McColorCodes.AQUA}[{document.Title} ({document.Version})]",
                DocumentUrl(document, language),
                $"Open {document.Title}"))
            .MsgLine(
                $"{McColorCodes.AQUA}[Agreement descriptor]",
                agreement.Url,
                "Open the immutable agreement descriptor")
            .Button(
                button,
                command,
                "Record acceptance of the current agreement package"));
    }

    private static string DocumentUrl(
        AgreementDocumentSnapshot document,
        string language) => document.Key == "commerceTerms"
            ? $"https://coflnet.com/{(language == "de" ? "de/" : "")}commerce-and-programme-terms"
            : document.Locales[language];

    internal static async Task Accept(
        IMinecraftSocket socket,
        string hash,
        string locale)
    {
        await Accept(socket, current, hash, locale, "minecraft");
    }

    internal static async Task AcceptCreator(
        IMinecraftSocket socket,
        string hash,
        string locale)
    {
        RequireExpertConfigAvailable(socket.UserId);
        await Accept(socket, await GetCreator(), hash, locale, "minecraft-sellconfig");
    }

    internal static async Task AcceptMarketplace(
        IMinecraftSocket socket,
        string hash,
        string locale)
    {
        RequireExpertConfigAvailable(socket.UserId);
        await Accept(socket, await GetMarketplace(), hash, locale, "minecraft-buyconfig");
    }

    private static async Task Accept(
        IMinecraftSocket socket,
        AgreementSnapshot agreement,
        string hash,
        string locale,
        string source)
    {
        if (agreement == null
            || !string.Equals(hash, agreement.Hash, StringComparison.OrdinalIgnoreCase))
            throw new CoflnetException(
                "agreement_changed",
                "The agreement changed. Please review it again.");
        if (!int.TryParse(socket.UserId, out var userId))
            throw new CoflnetException(
                "login_required",
                "Log in before accepting the agreement.");
        var language = NormalizeLanguage(locale);
        await socket.GetService<Coflnet.Sky.Indexer.Client.Api.IUserApi>()
            .UserUserIdAgreementsAgreementPostAsync(
                userId,
                agreement.Id,
                new IndexerTermsAcceptance(
                    agreement.Version,
                    agreement.Hash,
                    DateTime.UtcNow,
                    $"{source}-{language}"));
    }

    internal static async Task<AgreementSnapshot> GetCreator()
    {
        if (creator != null)
            return creator;
        try
        {
            return await creatorLoaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            throw new CoflnetException(
                "legal_manifest_unavailable",
                "The Creator Marketplace agreement is temporarily unavailable.");
        }
    }

    private static async Task<AgreementSnapshot> GetMarketplace()
    {
        if (marketplace != null)
            return marketplace;
        try
        {
            return await marketplaceLoaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        catch (TimeoutException)
        {
            throw new CoflnetException(
                "legal_manifest_unavailable",
                "The Expert Marketplace agreement is temporarily unavailable.");
        }
    }

    private static string Language(IMinecraftSocket socket) =>
        NormalizeLanguage(socket.sessionLifesycle?.AccountInfo?.Value?.Locale);

    private static string NormalizeLanguage(string locale) =>
        locale?.StartsWith("de", StringComparison.OrdinalIgnoreCase) == true
            ? "de"
            : "en";
}

internal sealed record AgreementSnapshot(
    string Id,
    string Version,
    string Hash,
    string Url,
    DateTime EffectiveFromUtc,
    IReadOnlyList<AgreementDocumentSnapshot> Documents,
    MarketplacePurchaseSnapshot Purchase = null);

internal sealed record AgreementDocumentSnapshot(
    string Key,
    string Title,
    string Version,
    DateTime EffectiveFromUtc,
    IReadOnlyDictionary<string, string> Locales);

internal sealed record MarketplacePurchaseSnapshot(
    IReadOnlyDictionary<string, MarketplacePurchaseRegimeSnapshot> Regimes);

internal sealed record MarketplacePurchaseRegimeSnapshot(
    string DeclarationVersion,
    IReadOnlyDictionary<string, MarketplacePurchaseLocaleSnapshot> Locales);

internal sealed record MarketplacePurchaseLocaleSnapshot(
    string DeclarationText,
    string DeclarationSha256,
    string WithdrawalVersion,
    string WithdrawalSha256,
    string WithdrawalUrl);

internal sealed record MarketplacePurchaseContext(
    AgreementSnapshot Agreement,
    MarketplacePurchaseLocaleSnapshot Purchase,
    string Locale,
    string ConsumerRightsRegime,
    string DeclarationVersion);
