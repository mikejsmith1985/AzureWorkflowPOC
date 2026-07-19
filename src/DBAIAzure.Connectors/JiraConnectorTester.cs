// Functional connectivity test for Jira Cloud via the REST API (spec-020, FR-007/D5).
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors;

/// <summary>
/// Tests the Jira provider of the Work Tracking System connector by authenticating and confirming the
/// configured project exists, using the credentials stored on the <see cref="ConnectorType.WorkTracker"/>
/// connector (resolved at each call — hot-reload). Lives in DBAIAzure.Connectors alongside the ADO tester so
/// <see cref="ConnectorHealthChecker"/> orchestrates both from one assembly. Creates no issue (safe probe).
/// </summary>
public sealed class JiraConnectorTester
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>How long a single probe waits before reporting a connectivity timeout.</summary>
    private const int ProbeTimeoutSeconds = 30;

    private readonly IConnectorConfigRepository _configRepo;
    private readonly IHttpClientFactory _httpClientFactory;

    public JiraConnectorTester(IConnectorConfigRepository configRepo, IHttpClientFactory httpClientFactory)
    {
        _configRepo = configRepo;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Probes <c>GET /rest/api/3/myself</c> (auth + reachability) then <c>GET /rest/api/3/project/{key}</c>
    /// (project exists / accessible), returning an actionable pass/fail. Creates no work item.
    /// </summary>
    public async Task<ConnectorTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var (siteUrl, email, projectKey, apiToken) = await ResolveCredentialsAsync(ct);

        if (string.IsNullOrEmpty(siteUrl) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(apiToken))
            return Fail("Jira is not fully configured — site URL, account email, and API token are required.");

        using var http = CreateAuthedClient(email, apiToken);
        try
        {
            // Step 1 — authenticate; on failure return immediately, otherwise capture the account label.
            var (authFailure, accountLabel) = await ProbeAuthAsync(http, siteUrl, email, ct);
            if (authFailure is not null)
                return authFailure;

            // Step 2 — confirm the project exists (or report auth-only success when no project key is set).
            return await ProbeProjectAsync(http, siteUrl, projectKey, accountLabel, ct);
        }
        catch (TaskCanceledException)
        {
            return Fail($"Test timed out after {ProbeTimeoutSeconds} seconds — check network connectivity to Jira.");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Service unreachable — could not connect to {siteUrl}. {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>Builds a Basic-authed client for the probe (short timeout, no shared state).</summary>
    private HttpClient CreateAuthedClient(string email, string apiToken)
    {
        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(ProbeTimeoutSeconds);
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{email}:{apiToken}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        return http;
    }

    /// <summary>Authenticates against <c>/myself</c>. Returns a failure result (or null) plus the account label.</summary>
    private async Task<(ConnectorTestResult? Failure, string AccountLabel)> ProbeAuthAsync(
        HttpClient http, string siteUrl, string email, CancellationToken ct)
    {
        var response = await http.GetAsync($"{siteUrl.TrimEnd('/')}/rest/api/3/myself", ct);
        if (!response.IsSuccessStatusCode)
        {
            var failure = (int)response.StatusCode switch
            {
                401 or 403 => Fail("Authentication failed — API token or email rejected (check the token value and that it belongs to this account)."),
                _          => Fail($"Unexpected response from Jira sign-in: {(int)response.StatusCode} {response.StatusCode}."),
            };
            return (failure, email);
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var accountLabel = TryReadString(body, "displayName") ?? TryReadString(body, "emailAddress") ?? email;
        return (null, accountLabel);
    }

    /// <summary>Confirms the project exists (when a key is set), returning the final pass/fail result.</summary>
    private async Task<ConnectorTestResult> ProbeProjectAsync(
        HttpClient http, string siteUrl, string projectKey, string accountLabel, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(projectKey))
            return Pass($"Authenticated as {accountLabel} in {siteUrl} (no project key set — project not verified).");

        var response = await http.GetAsync($"{siteUrl.TrimEnd('/')}/rest/api/3/project/{Uri.EscapeDataString(projectKey)}", ct);
        if (response.IsSuccessStatusCode)
            return Pass($"Authenticated as {accountLabel} — project '{projectKey}' confirmed in {siteUrl}.");

        return (int)response.StatusCode == 404
            ? Fail($"Authenticated as {accountLabel}, but project '{projectKey}' was not found or is not accessible.")
            : Fail($"Authenticated as {accountLabel}, but the project check returned {(int)response.StatusCode} {response.StatusCode}.");
    }

    private static ConnectorTestResult Pass(string message) =>
        new(ConnectorType.WorkTracker, true, message, DateTimeOffset.UtcNow);

    private static ConnectorTestResult Fail(string message) =>
        new(ConnectorType.WorkTracker, false, message, DateTimeOffset.UtcNow);

    private async Task<(string SiteUrl, string Email, string ProjectKey, string ApiToken)> ResolveCredentialsAsync(
        CancellationToken ct)
    {
        try
        {
            var configResult = await _configRepo.GetAsync(ConnectorType.WorkTracker, ct);
            var nonSecret = configResult?.NonSecretConfig is { } nsJson
                ? JsonSerializer.Deserialize<JiraConnectorConfig>(nsJson, JsonOptions)
                : null;

            var secretsJson = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.WorkTracker, ct);
            var apiToken = secretsJson is not null ? TryReadString(secretsJson, "apiToken") : null;

            return (
                nonSecret?.SiteUrl ?? string.Empty,
                nonSecret?.Email ?? string.Empty,
                nonSecret?.ProjectKey ?? string.Empty,
                apiToken ?? string.Empty
            );
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty, string.Empty);
        }
    }

    /// <summary>Reads a top-level string property from a JSON document, or null if absent/not JSON.</summary>
    private static string? TryReadString(string json, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
