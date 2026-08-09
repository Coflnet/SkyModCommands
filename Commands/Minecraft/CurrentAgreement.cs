using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using IndexerTermsAcceptance = Coflnet.Sky.Indexer.Client.Model.TermsAcceptance;

namespace Coflnet.Sky.Commands.MC;

internal static class CurrentAgreement
{
    private static volatile AgreementSnapshot current;
    private static readonly TaskCompletionSource<AgreementSnapshot> loaded =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal static void Initialize(AgreementSnapshot agreement)
    {
        current = agreement ?? throw new ArgumentNullException(nameof(agreement));
        loaded.TrySetResult(agreement);
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
        var language = Language(socket);
        socket.Dialog(dialog => dialog
            .MsgLine("Please review the current SkyCofl agreement package. Existing users may continue under previously accepted terms, but new purchases require current acceptance.")
            .ForEach(agreement.Documents, (builder, document) => builder.MsgLine(
                $"{McColorCodes.AQUA}[{document.Title} ({document.Version})]",
                document.Locales[language],
                $"Open {document.Title}"))
            .MsgLine(
                $"{McColorCodes.AQUA}[Agreement descriptor]",
                agreement.Url,
                "Open the immutable agreement descriptor")
            .Button(
                "Accept agreement package",
                $"/cofl terms {agreement.Hash} {language}",
                "Record acceptance of the displayed SkyCofl agreement package"));
    }

    internal static async Task Accept(
        IMinecraftSocket socket,
        string hash,
        string locale)
    {
        var agreement = current;
        if (agreement == null
            || !string.Equals(hash, agreement.Hash, StringComparison.OrdinalIgnoreCase))
            throw new CoflnetException(
                "agreement_changed",
                "The SkyCofl agreement changed. Please review it again.");
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
                    $"minecraft-{language}"));
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
    IReadOnlyList<AgreementDocumentSnapshot> Documents);

internal sealed record AgreementDocumentSnapshot(
    string Key,
    string Title,
    string Version,
    IReadOnlyDictionary<string, string> Locales);
