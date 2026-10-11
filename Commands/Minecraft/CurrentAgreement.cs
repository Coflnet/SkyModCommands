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
    /// <summary>
    /// Creator Marketplace roots a seller may be bound by, newest first. The
    /// manifest may publish any of them and an acceptance of any of them keeps
    /// publishing and the seller's listings available, so a compatible
    /// re-version neither pauses the marketplace nor forces a re-acceptance.
    /// New acceptances are always recorded against the published root.
    /// Remove an entry only when a later version is incompatible with it.
    /// </summary>
    internal static readonly string[] CreatorMarketplaceHashes =
    [
        // Creator License 2026-10-11: adds paid publication for individual
        // Creators resident in New Caledonia or another country without a
        // tax treaty with Germany that meets Annex A.6 (1.3, 7.1, A.6, A.7).
        // Nothing changes for Creators of the 2026-09-04 version.
        "56555b769bca24d8abb3e0617326e93f3b1484045ca2ebecceb28c69925efe4e",
        // Creator License 2026-09-04: initial Creator Marketplace agreement.
        "571ab277e36066ac89929fd72304096eb5b15dd324d8cb3718808a0454b76ddb",
    ];
    internal static string CreatorMarketplaceHash => CreatorMarketplaceHashes[0];
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
            || !CreatorMarketplaceHashes.Contains(creatorAgreement.Hash))
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
        // A published creator root that still has an accepted predecessor does
        // not pause the marketplace: sellers continue under the predecessor.
        var creatorEffectiveFrom = creatorAgreement == null
            || creatorAgreement.Hash != CreatorMarketplaceHashes[^1]
                ? marketplaceAgreement?.EffectiveFromUtc
                : creatorAgreement.EffectiveFromUtc;
        var effectiveFrom = marketplaceAgreement?.EffectiveFromUtc
            > creatorEffectiveFrom
                ? marketplaceAgreement.EffectiveFromUtc
                : creatorEffectiveFrom;
        return marketplaceAgreement?.Hash == ExpertMarketplaceHash
            && CreatorMarketplaceHashes.Contains(creatorAgreement?.Hash)
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
        if (await HasCreatorAcceptance(socket.UserId))
            return true;
        Ask(socket, agreement,
            "Before publishing an Expert Config, review and expressly accept the Creator Marketplace agreement. Acceptance is required even if you sold configs before.",
            $"/cofl sellconfig accept {agreement.Hash} {Language(socket)}",
            "Accept Creator agreement");
        return false;
    }

    internal static async Task<bool> HasCreatorAcceptance(string userId)
    {
        var agreement = await GetCreator();
        return await AcceptedAnyCreatorRoot(agreement.Hash, hash =>
            HasCreatorAcceptance(userId, agreement with { Hash = hash }));
    }

    /// <summary>
    /// Checks the published root first, then every other accepted root.
    /// </summary>
    internal static async Task<bool> AcceptedAnyCreatorRoot(
        string publishedHash,
        Func<string, Task<bool>> accepted)
    {
        if (await accepted(publishedHash))
            return true;
        foreach (var hash in CreatorMarketplaceHashes)
            if (hash != publishedHash && await accepted(hash))
                return true;
        return false;
    }

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
        return SelectPurchase(
            agreement,
            consumerRightsRegime,
            Language(socket));
    }

    /// <summary>
    /// Picks the regime disclosure and its locale. ROW (rest of world) texts
    /// only exist in English, so ROW always resolves "en" regardless of the
    /// player's language.
    /// </summary>
    internal static MarketplacePurchaseContext SelectPurchase(
        AgreementSnapshot agreement,
        string consumerRightsRegime,
        string language)
    {
        var regime = consumerRightsRegime ?? "EU";
        if (regime == "ROW")
            language = "en";
        if (agreement.Purchase?.Regimes.TryGetValue(regime, out var purchase) != true
            || !purchase.Locales.TryGetValue(language, out var localized))
            throw new CoflnetException(
                "purchase_unavailable",
                "Expert Config purchases are not supported for this country.");
        return new(
            agreement,
            localized,
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
