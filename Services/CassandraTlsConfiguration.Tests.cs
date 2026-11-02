using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class CassandraTlsConfigurationTests
{
    private const string Password = "test-only";
    // One whole-second anchor for every test certificate: certificates store whole seconds, and windows computed from
    // separate UtcNow reads let a child outlive its issuer by a fraction of a second (CertificateRequest.Create throws).
    private static readonly DateTimeOffset Anchor = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    private string dir;

    [SetUp]
    public void SetUp() => dir = Directory.CreateTempSubdirectory("skymod-cassandra-tls-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(dir, true);

    [Test]
    public void SelectMode_WithoutTlsKeys_IsNone()
    {
        var config = Config(new() { ["HOSTS"] = "localhost" });

        Assert.That(CassandraTlsConfiguration.SelectMode(config), Is.EqualTo(CassandraTlsMode.None));
        Assert.That(CassandraTlsConfiguration.CreateSslOptions(config), Is.Null);
    }

    [Test]
    public void SelectMode_WithPfxPaths_IsPfx()
    {
        var config = Config(new() { ["X509Certificate_PATHS"] = "/certs/client.pfx" });

        Assert.That(CassandraTlsConfiguration.SelectMode(config), Is.EqualTo(CassandraTlsMode.Pfx));
    }

    [Test]
    public void Pfx_WithoutPassword_Throws()
    {
        var config = Config(new() { ["X509Certificate_PATHS"] = "/certs/client.pfx" });

        var error = Assert.Throws<InvalidOperationException>(() => CassandraTlsConfiguration.CreateSslOptions(config));

        Assert.That(error.Message, Does.Contain("X509Certificate_PASSWORD"));
    }

    [Test]
    public void Pfx_WithoutValidationPath_KeepsAcceptingAnyServerCertificate()
    {
        // Legacy behaviour is intentionally unchanged until the chart moves to CASSANDRA:TLS.
        using var self = SelfSigned("client");
        var pfx = Path.Combine(dir, "client.pfx");
        File.WriteAllBytes(pfx, self.Export(X509ContentType.Pfx, Password));
        var config = Config(new() { ["X509Certificate_PATHS"] = pfx, ["X509Certificate_PASSWORD"] = Password });

        var ssl = CassandraTlsConfiguration.CreateSslOptions(config);

        Assert.That(ssl.CertificateCollection, Has.Count.EqualTo(1));
        Assert.That(ssl.RemoteCertValidationCallback(this, self, new X509Chain(), SslPolicyErrors.RemoteCertificateChainErrors), Is.True);
    }

    private static IConfiguration Config(Dictionary<string, string> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static X509Certificate2 SelfSigned(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(Anchor.AddDays(-1), Anchor.AddDays(1));
    }
}
