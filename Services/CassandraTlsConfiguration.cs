using System;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Cassandra;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Configuration;

namespace Coflnet.Sky.ModCommands.Services;

/// <summary>Which client TLS setup the CASSANDRA configuration section asks for.</summary>
public enum CassandraTlsMode
{
    None,
    Pfx
}

/// <summary>
/// Builds the Scylla/Cassandra client TLS options from the CASSANDRA configuration section.
/// </summary>
public static class CassandraTlsConfiguration
{
    public static CassandraTlsMode SelectMode(IConfiguration cassandra)
    {
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
        CassandraTlsMode.Pfx => CreatePfxOptions(cassandra),
        _ => null
    };

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
