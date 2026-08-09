using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.ModCommands.Dialogs;
using Moq;
using NUnit.Framework;
using IndexerTermsAcceptance = Coflnet.Sky.Indexer.Client.Model.TermsAcceptance;
using IndexerUserApi = Coflnet.Sky.Indexer.Client.Api.IUserApi;

namespace Coflnet.Sky.Commands.MC;

public class CurrentAgreementLoginTests
{
    [Test]
    public async Task Prompt_links_every_document_and_accepts_the_exact_root()
    {
        var hash = new string('a', 64);
        CurrentAgreement.Initialize(new(
            "skycofl",
            "2030-01-01",
            hash,
            $"https://coflnet.com/legal/agreements/{hash}.json",
            DateTime.UtcNow,
            new[]
            {
                Document("terms"),
                Document("commerceTerms"),
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
                dialog.Count(part => part.onClick?.EndsWith("-en") == true),
                Is.EqualTo(4));
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

    private static AgreementDocumentSnapshot Document(string key) => new(
        key,
        key,
        "2030-01-01",
        new Dictionary<string, string>
        {
            ["en"] = $"https://coflnet.com/{key}-en",
            ["de"] = $"https://coflnet.com/{key}-de"
        });
}
