// Loads the DoR document through the source-type seam (spec-021 D6): inline markdown from config, or an
// authless URL fetch cached for the configured window. Confluence/SharePoint are deferred behind this seam.
using System.Security.Cryptography;
using System.Text;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Web.Services.Dor;

/// <summary>
/// The DoR document source. Dispatches on the configured <c>source_type</c>: <c>inline</c> returns the markdown
/// held in configuration; <c>url</c> fetches a published document over HTTP and caches it for
/// <c>cache_ttl_minutes</c> (0 = always fresh). On a URL fetch failure it serves any cached copy with a warning;
/// with no cache it throws <see cref="DorDocumentUnavailableException"/> so the workflow degrades to a manual
/// exit rather than reviewing against an empty DoR.
/// </summary>
public sealed class DorDocumentSource : IDorDocumentSource
{
    private readonly IDorConfigResolver _configResolver;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DorDocumentSource> _logger;

    private readonly object _cacheLock = new();
    private (string Uri, DorDocument Doc)? _cache;

    public DorDocumentSource(
        IDorConfigResolver configResolver, IHttpClientFactory httpClientFactory, ILogger<DorDocumentSource> logger)
    {
        _configResolver = configResolver;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<DorDocument> LoadAsync(CancellationToken ct = default)
    {
        var dor = (await _configResolver.ResolveActiveAsync(ct)).Dor;
        return (dor.SourceType?.ToLowerInvariant()) switch
        {
            "inline" => LoadInline(dor),
            "url" => await LoadUrlAsync(dor, ct),
            _ => throw new DorDocumentUnavailableException($"Unsupported DoR source_type '{dor.SourceType}'."),
        };
    }

    private static DorDocument LoadInline(DorDocConfig dor)
    {
        if (string.IsNullOrWhiteSpace(dor.InlineMarkdown))
            throw new DorDocumentUnavailableException("DoR source_type is 'inline' but inline_markdown is empty.");
        return new DorDocument(dor.InlineMarkdown, ShortHash(dor.InlineMarkdown), DateTimeOffset.UtcNow, "inline");
    }

    private async Task<DorDocument> LoadUrlAsync(DorDocConfig dor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dor.SourceUri))
            throw new DorDocumentUnavailableException("DoR source_type is 'url' but source_uri is empty.");

        if (ReadFreshCache(dor.SourceUri, dor.CacheTtlMinutes) is { } fresh)
            return fresh;

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(DorDocumentSource));
            using var response = await client.GetAsync(dor.SourceUri, ct);
            response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync(ct);
            var version = response.Headers.ETag?.Tag
                          ?? response.Content.Headers.LastModified?.ToString("o")
                          ?? ShortHash(text);
            var doc = new DorDocument(text, version, DateTimeOffset.UtcNow, "url");
            StoreCache(dor.SourceUri, doc);
            return doc;
        }
        catch (Exception ex) when (ex is not DorDocumentUnavailableException)
        {
            if (ReadAnyCache(dor.SourceUri) is { } stale)
            {
                _logger.LogWarning(ex, "DoR document fetch failed; serving cached copy loaded at {LoadedAt}.", stale.LoadedAt);
                return stale;
            }
            throw new DorDocumentUnavailableException(
                $"DoR document at '{dor.SourceUri}' is unreachable and no cached copy exists.", ex);
        }
    }

    private DorDocument? ReadFreshCache(string uri, int ttlMinutes)
    {
        if (ttlMinutes <= 0) return null; // 0 = always fresh — never serve from cache.
        lock (_cacheLock)
        {
            if (_cache is { } c && c.Uri == uri
                && (DateTimeOffset.UtcNow - c.Doc.LoadedAt).TotalMinutes < ttlMinutes)
                return c.Doc;
        }
        return null;
    }

    private DorDocument? ReadAnyCache(string uri)
    {
        lock (_cacheLock)
            return _cache is { } c && c.Uri == uri ? c.Doc : null;
    }

    private void StoreCache(string uri, DorDocument doc)
    {
        lock (_cacheLock) _cache = (uri, doc);
    }

    /// <summary>A short, stable content hash used as a document version when the source exposes no etag.</summary>
    private static string ShortHash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes, 0, 6).ToLowerInvariant();
    }
}
