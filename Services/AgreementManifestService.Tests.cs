using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class AgreementManifestServicePurchaseTests
{
    private const string RowText =
        "I request immediate delivery of this Expert Config. I understand it is digital content.";

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static object Declaration(string version, params (string locale, string text)[] locales) => new
    {
        version,
        locales = locales.ToDictionary(
            item => item.locale,
            item => (object)new { text = item.text, sha256 = Hash(item.text) })
    };

    private static byte[] Manifest(bool withRow)
    {
        var declarations = new Dictionary<string, object>
        {
            ["digitalContentEarlySupplyEu"] = Declaration("eu-v", ("en", "eu en"), ("de", "eu de")),
            ["digitalContentEarlySupplyUk"] = Declaration("uk-v", ("en", "uk en"), ("de", "uk de")),
            ["digitalContentEarlySupplyUs"] = Declaration("us-v", ("en", "us en"), ("de", "us de")),
        };
        if (withRow)
            declarations["digitalContentEarlySupplyRow"] = Declaration("row-v", ("en", RowText));
        return JsonSerializer.SerializeToUtf8Bytes(new
        {
            documents = new Dictionary<string, object>
            {
                ["withdrawal"] = new
                {
                    version = "2026-09-30",
                    locales = new Dictionary<string, object>
                    {
                        ["en"] = new { url = "https://coflnet.com/legal/archive/withdrawal-en.md", sha256 = new string('a', 64) },
                        ["de"] = new { url = "https://coflnet.com/legal/archive/withdrawal-de.md", sha256 = new string('b', 64) }
                    }
                }
            },
            declarations
        });
    }

    private static AgreementSnapshot Agreement(MarketplacePurchaseSnapshot purchase) => new(
        "expertMarketplace", "1", new string('c', 64), "https://coflnet.com/x.json",
        DateTime.UtcNow, [], purchase);

    [Test]
    public void ManifestWithoutRowStillLoadsAndRowBuyersGetPurchaseUnavailable()
    {
        var purchase = AgreementManifestService.ParsePurchase(Manifest(withRow: false));

        Assert.That(purchase.Regimes.Keys, Is.EquivalentTo(new[] { "EU", "UK", "US" }));
        var ex = Assert.Throws<CoflnetException>(() =>
            CurrentAgreement.SelectPurchase(Agreement(purchase), "ROW", "de"));
        Assert.That(ex.Slug, Is.EqualTo("purchase_unavailable"));
    }

    [Test]
    public void ManifestWithRowResolvesEnglishTextsForGermanPlayer()
    {
        var purchase = AgreementManifestService.ParsePurchase(Manifest(withRow: true));

        var context = CurrentAgreement.SelectPurchase(Agreement(purchase), "ROW", "de");

        Assert.Multiple(() =>
        {
            Assert.That(context.Locale, Is.EqualTo("en"));
            Assert.That(context.ConsumerRightsRegime, Is.EqualTo("ROW"));
            Assert.That(context.DeclarationVersion, Is.EqualTo("row-v"));
            Assert.That(context.Purchase.DeclarationText, Is.EqualTo(RowText));
            Assert.That(context.Purchase.DeclarationSha256, Is.EqualTo(Hash(RowText)));
            Assert.That(context.Purchase.WithdrawalSha256, Is.EqualTo(new string('a', 64)));
            Assert.That(context.Purchase.WithdrawalUrl, Does.EndWith("withdrawal-en.md"));
        });
    }

    [Test]
    public void EuBuyerStillUsesPlayerLanguage()
    {
        var purchase = AgreementManifestService.ParsePurchase(Manifest(withRow: true));

        var context = CurrentAgreement.SelectPurchase(Agreement(purchase), "EU", "de");

        Assert.That(context.Purchase.DeclarationText, Is.EqualTo("eu de"));
    }

    [Test]
    public void MissingRequiredRegimeStillFailsManifestLoad()
    {
        var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Manifest(true));
        var declarations = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            json["declarations"].GetRawText());
        declarations.Remove("digitalContentEarlySupplyUs");
        var broken = JsonSerializer.SerializeToUtf8Bytes(new
        {
            documents = json["documents"],
            declarations
        });

        Assert.Throws<InvalidOperationException>(() =>
            AgreementManifestService.ParsePurchase(broken));
    }
}
