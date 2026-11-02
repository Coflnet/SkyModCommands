using System;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Cassandra;
using Coflnet.Core.Security;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;
using PemTlsExtensions = Coflnet.Cassandra.PemTlsExtensions;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>Which client TLS setup the CASSANDRA configuration section asks for.</summary>
public enum CassandraTlsMode
{
    None,
    Pfx,
    Pem
}

/// <summary>
/// Builds the Scylla/Cassandra client TLS options from the CASSANDRA configuration section.
/// A CASSANDRA:TLS section (CA_CERT_PATH, CERT_PATH, KEY_PATH, SERVER_NAME) selects PEM mutual TLS and wins over the
/// legacy PFX keys (X509Certificate_PATHS, X509Certificate_PASSWORD, X509Certificate_VALIDATION_PATH).
/// </summary>
public static class CassandraTlsConfiguration
{
    public static CassandraTlsMode SelectMode(IConfiguration cassandra)
    {
        if (cassandra.GetSection("TLS").Exists())
            return CassandraTlsMode.Pem;
        if (!string.IsNullOrEmpty(cassandra["X509Certificate_PATHS"]))
            return CassandraTlsMode.Pfx;
        return CassandraTlsMode.None;
    }

    public static void Apply(Builder builder, IConfiguration cassandra)
    {
        var sslOptions = CreateSslOptions(cassandra);
        if (sslOptions != null)
            builder.WithSSL(sslOptions);
    }

    public static SSLOptions CreateSslOptions(IConfiguration cassandra) => SelectMode(cassandra) switch
    {
        CassandraTlsMode.Pem => CreatePemOptions(cassandra.GetSection("TLS")),
        CassandraTlsMode.Pfx => CreatePfxOptions(cassandra),
        _ => null
    };

    /// <summary>
    /// Per-service PEM client leaf. The server must chain to the CA_CERT_PATH bundle (system trust is ignored) and its
    /// SAN must match SERVER_NAME, which is also sent as SNI. Renewed PEM files are re-read for new connections.
    /// </summary>
    private static SSLOptions CreatePemOptions(IConfiguration tls)
    {
        var serverName = Required(tls, "SERVER_NAME");
        EnsureCaBundleHasCertificates(RequiredFile(tls, "CA_CERT_PATH"));
        RequiredFile(tls, "CERT_PATH");
        RequiredFile(tls, "KEY_PATH");
        // Lives as long as the cluster: the returned options keep reading the PEM files through it.
        var credentials = PemTlsOptions.FromConfiguration(tls);
        return PemTlsExtensions.CreateCassandraSslOptions(credentials, serverName);
    }

    private static void EnsureCaBundleHasCertificates(string caPath)
    {
        var roots = new X509Certificate2Collection();
        roots.ImportFromPemFile(caPath);
        var count = roots.Count;
        foreach (var root in roots)
            root.Dispose();
        if (count == 0)
            throw new InvalidOperationException("CASSANDRA:TLS:CA_CERT_PATH contains no certificates.");
    }

    private static string Required(IConfiguration tls, string key)
    {
        var value = tls[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"CASSANDRA:TLS:{key} must be set for PEM mutual TLS.");
        return value;
    }

    private static string RequiredFile(IConfiguration tls, string key)
    {
        var path = Required(tls, key);
        if (!File.Exists(path))
            throw new InvalidOperationException($"CASSANDRA:TLS:{key} points to a file that does not exist.");
        return path;
    }

    /// <summary>
    /// Legacy shared PFX keys. Kept exactly as before: without a validation path every server certificate is accepted.
    /// </summary>
    private static SSLOptions CreatePfxOptions(IConfiguration cassandra)
    {
        var certificatePaths = cassandra["X509Certificate_PATHS"];
        var validationCertificatePath = cassandra["X509Certificate_VALIDATION_PATH"];
        var password = cassandra["X509Certificate_PASSWORD"] ?? throw new InvalidOperationException("CASSANDRA:X509Certificate_PASSWORD must be set if CASSANDRA:X509Certificate_PATHS is set.");
        CustomRootCaCertificateValidator certificateValidator = null;
        if (!string.IsNullOrEmpty(validationCertificatePath))
            certificateValidator = new CustomRootCaCertificateValidator(LoadPfx(validationCertificatePath, password));
        return new SSLOptions(
            // TLSv1.2 is required as of October 9, 2019.
            // See: https://www.instaclustr.com/removing-support-for-outdated-encryption-mechanisms/
            SslProtocols.Tls12,
            false,
            // Custom validator avoids need to trust the CA system-wide.
            (sender, certificate, chain, errors) => certificateValidator?.Validate(certificate, chain, errors) ?? true
        ).SetCertificateCollection(new(certificatePaths.Split(',').Select(p => LoadPfx(p, password)).ToArray()));
    }

    private static X509Certificate2 LoadPfx(string path, string password) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.DefaultKeySet, Pkcs12LoaderLimits.Defaults);
}
