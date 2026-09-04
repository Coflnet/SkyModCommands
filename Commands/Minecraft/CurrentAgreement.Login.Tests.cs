using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Coflnet.Sky.ModCommands.Dialogs;
using Moq;
using NUnit.Framework;
using IndexerTermsAcceptance = Coflnet.Sky.Indexer.Client.Model.TermsAcceptance;
using IndexerUserApi = Coflnet.Sky.Indexer.Client.Api.IUserApi;

namespace Coflnet.Sky.Commands.MC;

public class CurrentAgreementLoginTests
{
    [Test]
    public async Task Prompt_links_only_updated_documents_to_rendered_page_and_accepts_exact_root()
    {
        var hash = new string('a', 64);
        var effective = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        CurrentAgreement.Initialize(new(
            "skycofl",
            "2030-01-01",
            hash,
            $"https://coflnet.com/legal/agreements/{hash}.json",
            effective,
            new[]
            {
                Document("terms"),
                Document("commerceTerms", effective),
                Document("aiTerms"),
                Document("skycoflTerms")
            }));
        var api = new Mock<IndexerUserApi>();
        var socket = new Mock<IMinecraftSocket>();
        ChatPart[] dialog = null;
        socket.SetupGet(item => item.UserId).Returns("42");
        socket.Setup(item => item.GetService<IndexerUserApi>())
            .Returns(api.Object);
        socket.Setup(item => item.Dialog(
                It.IsAny<Func<SocketDialogBuilder, DialogBuilder>>()))
            .Callback<Func<SocketDialogBuilder, DialogBuilder>>(create =>
                dialog = create(new SocketDialogBuilder(null)).Build());

        CurrentAgreement.Ask(socket.Object);
        await CurrentAgreement.Accept(socket.Object, hash, "de-DE");

        Assert.Multiple(() =>
        {
            Assert.That(
                dialog.Count(part => part.onClick ==
                    "https://coflnet.com/commerce-and-programme-terms"),
                Is.EqualTo(1));
            Assert.That(
                dialog.Any(part => part.onClick ==
                    "https://coflnet.com/legal/archive/commerceTerms-en.md"),
                Is.False);
            Assert.That(
                dialog.Any(part => part.onClick ==
                    $"https://coflnet.com/legal/agreements/{hash}.json"),
                Is.True);
            Assert.That(
                dialog.Any(part => part.onClick ==
                    $"/cofl terms {hash} en"),
                Is.True);
        });
        api.Verify(client => client.UserUserIdAgreementsAgreementPostAsync(
            42,
            "skycofl",
            It.Is<IndexerTermsAcceptance>(acceptance =>
                acceptance.VarVersion == "2030-01-01"
                && acceptance.Hash == hash
                && acceptance.AcceptedAtUtc.Kind == DateTimeKind.Utc
                && acceptance.Source == "minecraft-de"),
            0,
            It.IsAny<CancellationToken>()));
    }

    [Test]
    public void Terms_command_is_registered_for_the_login_prompt()
    {
        Assert.That(
            MinecraftSocket.Commands["terms"],
            Is.TypeOf<AgreementTermsCommand>());
    }

    [Test]
    public async Task Preview_user_can_accept_both_marketplace_roots_before_effective_date()
    {
        var effective = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        CurrentAgreement.InitializeExpertConfig(
            ExpertAgreement("expertMarketplace",
                CurrentAgreement.ExpertMarketplaceHash, effective),
            ExpertAgreement("creatorMarketplace",
                CurrentAgreement.CreatorMarketplaceHash, effective));
        var api = new Mock<IndexerUserApi>();
        var socket = new Mock<IMinecraftSocket>();
        socket.SetupGet(item => item.UserId).Returns("7");
        socket.Setup(item => item.GetService<IndexerUserApi>()).Returns(api.Object);

        await CurrentAgreement.AcceptCreator(socket.Object,
            CurrentAgreement.CreatorMarketplaceHash, "en");
        await CurrentAgreement.AcceptMarketplace(socket.Object,
            CurrentAgreement.ExpertMarketplaceHash, "en");

        api.Verify(client => client.UserUserIdAgreementsAgreementPostAsync(
            7,
            "creatorMarketplace",
            It.Is<IndexerTermsAcceptance>(acceptance =>
                acceptance.VarVersion == "2026-09-04"
                && acceptance.Hash == CurrentAgreement.CreatorMarketplaceHash
                && acceptance.Source == "minecraft-sellconfig-en"),
            0,
            It.IsAny<CancellationToken>()));
        api.Verify(client => client.UserUserIdAgreementsAgreementPostAsync(
            7,
            "expertMarketplace",
            It.Is<IndexerTermsAcceptance>(acceptance =>
                acceptance.Hash == CurrentAgreement.ExpertMarketplaceHash
                && acceptance.Source == "minecraft-buyconfig-en"),
            0,
            It.IsAny<CancellationToken>()));
    }

    [Test]
    public void Expert_config_rollout_uses_exact_user_and_effective_boundary()
    {
        var effective = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        CurrentAgreement.InitializeExpertConfig(
            ExpertAgreement("expertMarketplace",
                CurrentAgreement.ExpertMarketplaceHash, effective),
            ExpertAgreement("creatorMarketplace",
                CurrentAgreement.CreatorMarketplaceHash, effective));

        Assert.Multiple(() =>
        {
            Assert.That(CurrentAgreement.ExpertConfigAvailable(
                "7", effective.AddTicks(-1)), Is.True);
            Assert.That(CurrentAgreement.ExpertConfigAvailable(
                "07", effective.AddTicks(-1)), Is.False);
            Assert.That(CurrentAgreement.ExpertConfigAvailable(
                "42", effective.AddTicks(-1)), Is.False);
            Assert.That(CurrentAgreement.ExpertConfigAvailable(
                "42", effective), Is.True);
        });
    }

    [Test]
    public void Direct_acceptance_rejects_zero_padded_preview_user_before_rollout()
    {
        var effective = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        CurrentAgreement.InitializeExpertConfig(
            ExpertAgreement("expertMarketplace",
                CurrentAgreement.ExpertMarketplaceHash, effective),
            ExpertAgreement("creatorMarketplace",
                CurrentAgreement.CreatorMarketplaceHash, effective));
        var socket = new Mock<IMinecraftSocket>();
        socket.SetupGet(item => item.UserId).Returns("07");

        Assert.ThrowsAsync<CoflnetException>(() =>
            CurrentAgreement.AcceptCreator(socket.Object,
                CurrentAgreement.CreatorMarketplaceHash, "en"));
        Assert.ThrowsAsync<CoflnetException>(() =>
            CurrentAgreement.AcceptMarketplace(socket.Object,
                CurrentAgreement.ExpertMarketplaceHash, "en"));
    }

    [Test]
    public void Expert_config_rollout_rejects_wrong_roots()
    {
        var effective = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Throws<InvalidOperationException>(() =>
            CurrentAgreement.InitializeExpertConfig(
                ExpertAgreement("expertMarketplace", new string('a', 64), effective),
                ExpertAgreement("creatorMarketplace",
                    CurrentAgreement.CreatorMarketplaceHash, effective)));
        Assert.Throws<InvalidOperationException>(() =>
            CurrentAgreement.InitializeExpertConfig(
                ExpertAgreement("expertMarketplace",
                    CurrentAgreement.ExpertMarketplaceHash, effective),
                ExpertAgreement("creatorMarketplace", new string('b', 64), effective)));
    }

    private static AgreementSnapshot ExpertAgreement(
        string id,
        string hash,
        DateTime effective) => new(
            id,
            "2026-09-04",
            hash,
            $"https://coflnet.com/legal/agreements/{hash}.json",
            effective,
            new[] { Document(id) });

    private static AgreementDocumentSnapshot Document(
        string key,
        DateTime? effective = null) => new(
        key,
        key,
        "2030-01-01",
        effective ?? new DateTime(2029, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new Dictionary<string, string>
        {
            ["en"] = $"https://coflnet.com/legal/archive/{key}-en.md",
            ["de"] = $"https://coflnet.com/legal/archive/{key}-de.md"
        });
}
