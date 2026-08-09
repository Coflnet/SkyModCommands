using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.MC;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.ModCommands.Services;

public sealed class AgreementManifestService : BackgroundService
{
    private const string AgreementId = "skycofl";
    private const string AgreementKind = "coflnet-legal-agreement-node";
    private static readonly string[] RequiredDocuments =
        ["terms", "commerceTerms", "aiTerms", "skycoflTerms"];
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromHours(1);
    private static readonly Uri CoflnetOrigin = new("https://coflnet.com/");
    private readonly IHttpClientFactory clients;
    private readonly IConfiguration configuration;
    private readonly ILogger<AgreementManifestService> logger;
    private Uri manifestUri;

    internal string ActiveHash { get; private set; }

    public AgreementManifestService(
        IHttpClientFactory clients,
        IConfiguration configuration,
        ILogger<AgreementManifestService> logger)
    {
        this.clients = clients;
        this.configuration = configuration;
        this.logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        manifestUri = new Uri(
            configuration["LEGAL_MANIFEST_URL"]
            ?? "https://coflnet.com/legal/manifest.json");
        if (!IsCoflnetHttpsOrigin(manifestUri))
            throw new InvalidOperationException(
                "LEGAL_MANIFEST_URL must use the Coflnet HTTPS origin.");
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var agreement = await Load(manifestUri, stoppingToken);
                var untilEffective = agreement.EffectiveFromUtc - DateTime.UtcNow;
                if (untilEffective > TimeSpan.Zero)
                {
                    await Task.Delay(
                        untilEffective < RefreshDelay
                            ? untilEffective
                            : RefreshDelay,
                        stoppingToken);
                    continue;
                }
                CurrentAgreement.Initialize(agreement);
                ActiveHash = agreement.Hash;
                await Task.Delay(RefreshDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Loading the SkyCofl agreement failed; retrying in {RetryDelay}.",
                    RetryDelay);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }
    }

    private async Task<AgreementSnapshot> Load(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(nameof(AgreementManifestService));
        var manifest = Deserialize<Manifest>(
            await client.GetByteArrayAsync(uri, cancellationToken),
            "The legal manifest is invalid.");
        if (manifest.SchemaVersion != 1
            || manifest.AgreementTreeVersion != 1
            || !Uri.TryCreate(manifest.Source, UriKind.Absolute, out var source)
            || source != CoflnetOrigin
            || !manifest.Agreements.TryGetValue(AgreementId, out var summary)
            || summary.Type != "service"
            || !IsSha256(summary.AgreementHash)
            || !TryAgreementUri(
                summary.AgreementUrl,
                summary.AgreementHash,
                out var agreementUri))
            throw new InvalidOperationException(
                "The SkyCofl agreement root is incomplete.");

        var root = await LoadAgreement(
            client,
            agreementUri,
            AgreementId,
            summary.AgreementHash,
            new Dictionary<string, LoadedAgreement>(
                StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            cancellationToken);
        var resolved = ResolveDocuments(root);
        if (!RequiredDocuments.All(resolved.ContainsKey)
            || resolved.Count != RequiredDocuments.Length
            || summary.ResolvedDocuments.Count != resolved.Count
            || summary.ResolvedDocuments.Select(item => item.Key)
                .Distinct(StringComparer.Ordinal).Count() != resolved.Count
            || !RequiredDocuments.All(key =>
                summary.ResolvedDocuments.Any(item => item.Key == key)))
            throw new InvalidOperationException(
                "The SkyCofl agreement must resolve exactly four documents.");

        var documents = new List<AgreementDocumentSnapshot>();
        foreach (var item in summary.ResolvedDocuments)
        {
            if (!resolved.TryGetValue(item.Key, out var descriptorDocument)
                || descriptorDocument.Version != item.Version
                || !string.Equals(
                    descriptorDocument.AcceptanceHash,
                    item.AcceptanceHash,
                    StringComparison.OrdinalIgnoreCase)
                || !manifest.Documents.TryGetValue(item.Key, out var document)
                || !SameDocument(descriptorDocument, document))
                throw new InvalidOperationException(
                    "The resolved document list does not match the agreement root.");
            await VerifyDocument(client, document, cancellationToken);
            documents.Add(new(
                item.Key,
                document.Title,
                document.Version,
                document.Locales.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value.Url)));
        }

        var serviceTerms = root.Descriptor.Documents.SingleOrDefault(
            document => document.Key == "skycoflTerms")
            ?? throw new InvalidOperationException(
                "The SkyCofl root does not contain its service terms.");
        return new(
            AgreementId,
            serviceTerms.Version,
            summary.AgreementHash.ToLowerInvariant(),
            agreementUri.ToString(),
            documents.Max(document => DateTimeOffset.Parse(
                resolved[document.Key].EffectiveFromUtc).UtcDateTime),
            documents);
    }

    private static async Task<LoadedAgreement> LoadAgreement(
        HttpClient client,
        Uri uri,
        string expectedId,
        string expectedHash,
        Dictionary<string, LoadedAgreement> loaded,
        HashSet<string> active,
        CancellationToken cancellationToken)
    {
        if (!active.Add(expectedHash))
            throw new InvalidOperationException(
                "The agreement graph contains a cycle.");
        if (loaded.TryGetValue(expectedHash, out var cached))
        {
            active.Remove(expectedHash);
            if (cached.Descriptor.Id != expectedId)
                throw new InvalidOperationException(
                    "An agreement hash was reused for another ID.");
            return cached;
        }

        var bytes = await client.GetByteArrayAsync(uri, cancellationToken);
        if (!Sha256(bytes).Equals(
                expectedHash,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Agreement hash mismatch for {uri}.");
        var descriptor = Deserialize<AgreementDescriptor>(
            bytes,
            "An agreement descriptor is invalid.");
        if (descriptor.SchemaVersion != 1
            || descriptor.Kind != AgreementKind
            || descriptor.Id != expectedId
            || descriptor.Type is not ("shared" or "service" or "role"))
            throw new InvalidOperationException(
                "An agreement descriptor identity is invalid.");

        var result = new LoadedAgreement(descriptor, []);
        loaded.Add(expectedHash, result);
        foreach (var dependency in descriptor.Dependencies)
        {
            if (!IsSha256(dependency.AgreementHash)
                || !TryAgreementUri(
                    dependency.Path,
                    dependency.AgreementHash,
                    out var dependencyUri))
                throw new InvalidOperationException(
                    "An agreement dependency is invalid.");
            result.Dependencies.Add(await LoadAgreement(
                client,
                dependencyUri,
                dependency.Id,
                dependency.AgreementHash,
                loaded,
                active,
                cancellationToken));
        }
        active.Remove(expectedHash);
        return result;
    }

    private static Dictionary<string, Document> ResolveDocuments(
        LoadedAgreement root)
    {
        var resolved = new Dictionary<string, Document>(StringComparer.Ordinal);
        void Visit(LoadedAgreement agreement)
        {
            foreach (var document in agreement.Descriptor.Documents)
            {
                if (resolved.TryGetValue(document.Key, out var existing)
                    && !SameDocument(existing, document))
                    throw new InvalidOperationException(
                        "The agreement graph contains conflicting documents.");
                resolved[document.Key] = document;
            }
            foreach (var dependency in agreement.Dependencies)
                Visit(dependency);
        }
        Visit(root);
        return resolved;
    }

    private static async Task VerifyDocument(
        HttpClient client,
        Document document,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document.Version)
            || document.Locales.Count != 2
            || !document.Locales.TryGetValue("en", out var english)
            || !document.Locales.TryGetValue("de", out var german)
            || !DateTime.TryParse(document.EffectiveFromUtc, out _))
            throw new InvalidOperationException(
                "A legal document entry is incomplete.");
        var canonical = Encoding.UTF8.GetBytes(
            $"version={document.Version}\nen={english.Sha256}\nde={german.Sha256}\n");
        if (!Sha256(canonical).Equals(
                document.AcceptanceHash,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "A legal document acceptance hash is invalid.");
        foreach (var locale in document.Locales.Values)
        {
            if (!Uri.TryCreate(locale.Url, UriKind.Absolute, out var documentUri)
                || !IsCoflnetHttpsOrigin(documentUri)
                || !IsSha256(locale.Sha256)
                || !Sha256(await client.GetByteArrayAsync(
                        documentUri,
                        cancellationToken)).Equals(
                    locale.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "A localized legal document is invalid.");
        }
    }

    private static bool SameDocument(Document left, Document right) =>
        left.Key == right.Key
        && left.Version == right.Version
        && left.PublishedAtUtc == right.PublishedAtUtc
        && left.EffectiveFromUtc == right.EffectiveFromUtc
        && string.Equals(
            left.AcceptanceHash,
            right.AcceptanceHash,
            StringComparison.OrdinalIgnoreCase)
        && left.Locales.Count == right.Locales.Count
        && left.Locales.All(item =>
            right.Locales.TryGetValue(item.Key, out var other)
            && item.Value.Url == other.Url
            && string.Equals(
                item.Value.Sha256,
                other.Sha256,
                StringComparison.OrdinalIgnoreCase));

    private static bool TryAgreementUri(
        string value,
        string hash,
        out Uri uri)
    {
        if (!Uri.TryCreate(CoflnetOrigin, value, out uri)
            || !IsCoflnetHttpsOrigin(uri)
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
            || uri.AbsolutePath != $"/legal/agreements/{hash}.json")
        {
            uri = null;
            return false;
        }
        return true;
    }

    private static bool IsCoflnetHttpsOrigin(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps
        && uri.IdnHost.Equals(
            "coflnet.com",
            StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsSha256(string value) =>
        value?.Length == 64 && value.All(Uri.IsHexDigit);

    private static string Sha256(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static T Deserialize<T>(byte[] bytes, string message) =>
        JsonSerializer.Deserialize<T>(
            bytes,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException(message);

    private sealed class Manifest
    {
        public int SchemaVersion { get; set; }
        public int AgreementTreeVersion { get; set; }
        public string Source { get; set; }
        public Dictionary<string, Document> Documents { get; set; } = [];
        public Dictionary<string, AgreementSummary> Agreements { get; set; } = [];
    }

    private sealed class AgreementSummary
    {
        public string Type { get; set; }
        public string AgreementHash { get; set; }
        public string AgreementUrl { get; set; }
        public List<DocumentSummary> ResolvedDocuments { get; set; } = [];
    }

    private sealed class DocumentSummary
    {
        public string Key { get; set; }
        public string Version { get; set; }
        public string AcceptanceHash { get; set; }
    }

    private sealed class AgreementDescriptor
    {
        public int SchemaVersion { get; set; }
        public string Kind { get; set; }
        public string Id { get; set; }
        public string Type { get; set; }
        public List<Document> Documents { get; set; } = [];
        public List<AgreementDependency> Dependencies { get; set; } = [];
    }

    private sealed class AgreementDependency
    {
        public string Id { get; set; }
        public string AgreementHash { get; set; }
        public string Path { get; set; }
    }

    private sealed class Document
    {
        public string Key { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
        public string PublishedAtUtc { get; set; }
        public string EffectiveFromUtc { get; set; }
        public Dictionary<string, Locale> Locales { get; set; } = [];
        public string AcceptanceHash { get; set; }
    }

    private sealed class Locale
    {
        public string Url { get; set; }
        public string Sha256 { get; set; }
    }

    private sealed record LoadedAgreement(
        AgreementDescriptor Descriptor,
        List<LoadedAgreement> Dependencies);
}
