using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Cassandra;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;

namespace Coflnet.Sky.ModCommands.Services;

public class CassandraTlsConfigurationTests
{
    private const string Password = "test-only";
    private const string ServerName = "sky-client";
    private const string ServerAuth = "1.3.6.1.5.5.7.3.1";
    private const string ClientAuth = "1.3.6.1.5.5.7.3.2";
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

    [Test]
    public void SelectMode_TlsSectionWinsOverPfxPaths()
    {
        var config = Config(new() { ["TLS:SERVER_NAME"] = ServerName, ["X509Certificate_PATHS"] = "/certs/client.pfx" });

        Assert.That(CassandraTlsConfiguration.SelectMode(config), Is.EqualTo(CassandraTlsMode.Pem));
    }

    [Test]
    public void Pem_LoadsClientCertificateWithPrivateKey_AndSendsServerNameAsTarget()
    {
        using var ca = CreateCa("ca");
        using var client = Issue(ca, "sky-mod-commands", null, ClientAuth);

        var ssl = CassandraTlsConfiguration.CreateSslOptions(PemConfig(ca, client));

        Assert.That(ssl.CertificateCollection, Has.Count.EqualTo(1));
        Assert.That(((X509Certificate2)ssl.CertificateCollection[0]).HasPrivateKey, Is.True);
        Assert.That(ssl.HostNameResolver(IPAddress.Loopback), Is.EqualTo(ServerName));
    }

    [Test]
    public void Pem_AppliesSslToTheClusterBuilder()
    {
        using var ca = CreateCa("ca");
        using var client = Issue(ca, "sky-mod-commands", null, ClientAuth);
        var builder = Cluster.Builder().AddContactPoint("127.0.0.1");

        CassandraTlsConfiguration.Apply(builder, PemConfig(ca, client));

        Assert.That(builder.Build().Configuration.ProtocolOptions.SslOptions, Is.Not.Null);
    }

    [TestCase("SERVER_NAME")]
    [TestCase("CA_CERT_PATH")]
    [TestCase("CERT_PATH")]
    [TestCase("KEY_PATH")]
    public void Pem_FailsFastWhenASettingIsMissing(string key)
    {
        using var ca = CreateCa("ca");
        using var client = Issue(ca, "sky-mod-commands", null, ClientAuth);
        var values = PemValues(ca, client);
        values["TLS:" + key] = key == "SERVER_NAME" ? "" : Path.Combine(dir, "missing.pem");

        var error = Assert.Throws<InvalidOperationException>(() => CassandraTlsConfiguration.CreateSslOptions(Config(values)));

        Assert.That(error.Message, Does.Contain("CASSANDRA:TLS:" + key));
    }

    [Test]
    public void Pem_FailsFastWhenCaBundleHasNoCertificates()
    {
        using var ca = CreateCa("ca");
        using var client = Issue(ca, "sky-mod-commands", null, ClientAuth);
        var values = PemValues(ca, client);
        File.WriteAllText(values["TLS:CA_CERT_PATH"], "not a certificate");

        var error = Assert.Throws<InvalidOperationException>(() => CassandraTlsConfiguration.CreateSslOptions(Config(values)));

        Assert.That(error.Message, Does.Contain("CA_CERT_PATH"));
    }

    [Test]
    public void Pem_AcceptsServerIssuedByConfiguredCa()
    {
        using var ca = CreateCa("ca");
        using var server = Issue(ca, "scylla", ServerName, ServerAuth);

        Assert.That(ValidateServer(ca, server, SslPolicyErrors.RemoteCertificateChainErrors), Is.True);
    }

    [Test]
    public void Pem_RejectsServerFromAnotherCa()
    {
        using var ca = CreateCa("ca");
        using var otherCa = CreateCa("other");
        using var server = Issue(otherCa, "scylla", ServerName, ServerAuth);

        Assert.That(ValidateServer(ca, server, SslPolicyErrors.RemoteCertificateChainErrors), Is.False);
    }

    [Test]
    public void Pem_RejectsNameMismatchReportedByTheHandshake()
    {
        using var ca = CreateCa("ca");
        using var server = Issue(ca, "scylla", ServerName, ServerAuth);

        Assert.That(ValidateServer(ca, server, SslPolicyErrors.RemoteCertificateNameMismatch), Is.False);
    }

    [Test]
    public void Pem_RejectsServerWithoutServerAuthUsage()
    {
        using var ca = CreateCa("ca");
        using var server = Issue(ca, "scylla", ServerName, ClientAuth);

        Assert.That(ValidateServer(ca, server, SslPolicyErrors.None), Is.False);
    }

    [Test]
    public async Task Pem_HandshakeSucceedsWithMatchingServerSan_AndPresentsClientCertificate()
    {
        using var ca = CreateCa("ca");
        using var server = Issue(ca, "scylla", ServerName, ServerAuth);
        using var client = Issue(ca, "sky-mod-commands", null, ClientAuth);
        var ssl = CassandraTlsConfiguration.CreateSslOptions(PemConfig(ca, client));

        var clientCertificate = await Handshake(ssl, server);

        Assert.That(clientCertificate?.Subject, Is.EqualTo("CN=sky-mod-commands"));
    }

    [Test]
    public void Pem_HandshakeFailsWhenServerSanDoesNotMatchServerName()
    {
        using var ca = CreateCa("ca");
        using var server = Issue(ca, ServerName, "other-host", ServerAuth);
        using var client = Issue(ca, "sky-mod-commands", null, ClientAuth);
        var ssl = CassandraTlsConfiguration.CreateSslOptions(PemConfig(ca, client));

        Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(() => Handshake(ssl, server));
    }

    [Test]
    public void TestChain_ValidityWindowsAreStrictlyNested()
    {
        // Regression (ported from VerApi): windows from separate UtcNow reads let a child outlive its issuer.
        using var ca = CreateCa("ca");
        using var leaf = Issue(ca, "leaf", null, ClientAuth);

        Assert.That(leaf.NotAfter, Is.LessThan(ca.NotAfter));
        Assert.That(leaf.NotBefore, Is.GreaterThan(ca.NotBefore));
    }

    /// <summary>Runs a loopback TLS handshake: client side uses the Cassandra SSLOptions exactly as the driver does.</summary>
    private static async Task<X509Certificate> Handshake(SSLOptions ssl, X509Certificate2 serverCertificate)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var tcpClient = new TcpClient();
        var connect = tcpClient.ConnectAsync(IPAddress.Loopback, port);
        using var accepted = await listener.AcceptTcpClientAsync();
        await connect;
        using var serverStream = new SslStream(accepted.GetStream(), false, (_, certificate, _, _) => certificate != null);
        using var clientStream = new SslStream(tcpClient.GetStream(), false, ssl.RemoteCertValidationCallback);
        var serverSide = serverStream.AuthenticateAsServerAsync(serverCertificate, true, SslProtocols.Tls12 | SslProtocols.Tls13, false);
        var targetHost = ssl.HostNameResolver(IPAddress.Loopback);
        var clientSide = clientStream.AuthenticateAsClientAsync(targetHost, ssl.CertificateCollection, ssl.SslProtocol, false);
        try
        {
            await clientSide;
        }
        finally
        {
            tcpClient.Close();
            await Task.WhenAny(serverSide);
        }
        await serverSide;
        return serverStream.RemoteCertificate;
    }

    private bool ValidateServer(X509Certificate2 ca, X509Certificate2 server, SslPolicyErrors errors)
    {
        using var client = Issue(ca, "sky-mod-commands", null, ClientAuth);
        var ssl = CassandraTlsConfiguration.CreateSslOptions(PemConfig(ca, client));
        return ssl.RemoteCertValidationCallback(this, server, null, errors);
    }

    private IConfiguration PemConfig(X509Certificate2 ca, X509Certificate2 client) => Config(PemValues(ca, client));

    private Dictionary<string, string> PemValues(X509Certificate2 ca, X509Certificate2 client)
    {
        var keyPath = Path.Combine(dir, "tls.key");
        File.WriteAllText(keyPath, client.GetECDsaPrivateKey().ExportPkcs8PrivateKeyPem());
        return new()
        {
            ["TLS:SERVER_NAME"] = ServerName,
            ["TLS:CA_CERT_PATH"] = WritePem(ca, "scylla-ca.crt"),
            ["TLS:CERT_PATH"] = WritePem(client, "tls.crt"),
            ["TLS:KEY_PATH"] = keyPath
        };
    }

    private string WritePem(X509Certificate2 certificate, string file)
    {
        var path = Path.Combine(dir, file);
        File.WriteAllText(path, certificate.ExportCertificatePem());
        return path;
    }

    private static X509Certificate2 CreateCa(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var ca = request.CreateSelfSigned(Anchor.AddDays(-2), Anchor.AddDays(2));
        return X509CertificateLoader.LoadPkcs12(ca.Export(X509ContentType.Pfx), null);
    }

    private static X509Certificate2 Issue(X509Certificate2 ca, string commonName, string dnsName, string usage)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid(usage) }, false));
        if (dnsName != null)
            request.CertificateExtensions.Add(DnsSan(dnsName));
        using var signed = request.Create(ca, Anchor.AddHours(-1), Anchor.AddHours(1), RandomNumberGenerator.GetBytes(8));
        using var withKey = signed.CopyWithPrivateKey(key);
        // PFX round trip so SslStream can use the key as a server certificate on every platform.
        return X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.Exportable);
    }

    private static X509Extension DnsSan(string dnsName)
    {
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(dnsName);
        return names.Build();
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
