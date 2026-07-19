// Builds an authenticated Jira Cloud connection from the live connector store, per run (spec-020, D4/T021).
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Integrations.Jira;

/// <summary>An authed Jira client paired with the non-secret fields the adapter needs for URLs and project.</summary>
public sealed record JiraConnection(HttpClient Client, string SiteUrl, string ProjectKey);

/// <summary>Thrown when a Jira operation is attempted but Jira is not the configured/active provider.</summary>
public sealed class JiraNotConfiguredException : InvalidOperationException
{
    public JiraNotConfiguredException(string message) : base(message) { }
}

/// <summary>
/// Resolves the active Jira credentials from the connector store on each call and returns an authenticated
/// <see cref="HttpClient"/>, rebuilding it only when the resolved credentials change (cache key
/// <c>siteUrl|email|apiToken</c>). This is the direct analogue of <c>AzureDevOpsBoardsClient.GetClientAsync</c>
/// and the change that lets UI-entered Jira credentials take effect without an application restart (FR-005).
/// </summary>
public interface IJiraConnectionFactory
{
    /// <summary>
    /// Returns an authed connection for the current Jira configuration. Throws
    /// <see cref="JiraNotConfiguredException"/> when the active provider is not Jira, or when required
    /// credentials are missing.
    /// </summary>
    Task<JiraConnection> GetConnectionAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="IJiraConnectionFactory"/>
public sealed class JiraConnectionFactory : IJiraConnectionFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IWorkTrackerConfigResolver _resolver;

    // Guards the cached client so a credential change rebuilds it exactly once (mirrors the ADO boards client).
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private HttpClient? _cachedClient;
    private string? _cachedConnectionKey;

    public JiraConnectionFactory(IWorkTrackerConfigResolver resolver) => _resolver = resolver;

    /// <inheritdoc/>
    public async Task<JiraConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        var resolved = await _resolver.ResolveActiveAsync(ct);
        if (!resolved.IsConfigured || resolved.Provider != WorkTrackerProvider.Jira)
            throw new JiraNotConfiguredException("Jira is not the active work-tracker provider or is not configured.");

        var config = ParseNonSecret(resolved.NonSecretJson);
        var apiToken = ParseApiToken(resolved.DecryptedSecret);

        if (string.IsNullOrWhiteSpace(config.SiteUrl) || string.IsNullOrWhiteSpace(config.Email)
            || string.IsNullOrWhiteSpace(apiToken))
        {
            throw new JiraNotConfiguredException("Jira connection settings are incomplete (site URL, email, and API token are required).");
        }

        var client = await ResolveClientAsync(config, apiToken, ct);
        return new JiraConnection(client, config.SiteUrl, config.ProjectKey);
    }

    /// <summary>Returns the cached client, rebuilding it only when the credentials changed.</summary>
    private async Task<HttpClient> ResolveClientAsync(JiraConnectorConfig config, string apiToken, CancellationToken ct)
    {
        // The token participates in the key so a rotated secret rebuilds the client (hot-reload).
        var connectionKey = $"{config.SiteUrl}|{config.Email}|{apiToken}";

        await _clientLock.WaitAsync(ct);
        try
        {
            if (_cachedClient is null || _cachedConnectionKey != connectionKey)
            {
                _cachedClient?.Dispose();
                var basicToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{config.Email}:{apiToken}"));
                var client = new HttpClient { BaseAddress = new Uri(config.SiteUrl) };
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
                _cachedClient = client;
                _cachedConnectionKey = connectionKey;
            }

            return _cachedClient;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    private static JiraConnectorConfig ParseNonSecret(string? nonSecretJson)
    {
        if (string.IsNullOrWhiteSpace(nonSecretJson))
            return new JiraConnectorConfig(string.Empty, string.Empty, string.Empty);
        return JsonSerializer.Deserialize<JiraConnectorConfig>(nonSecretJson, JsonOptions)
            ?? new JiraConnectorConfig(string.Empty, string.Empty, string.Empty);
    }

    private static string ParseApiToken(string? secretJson)
    {
        if (string.IsNullOrWhiteSpace(secretJson))
            return string.Empty;
        try
        {
            using var document = JsonDocument.Parse(secretJson);
            return document.RootElement.TryGetProperty("apiToken", out var token) ? token.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
