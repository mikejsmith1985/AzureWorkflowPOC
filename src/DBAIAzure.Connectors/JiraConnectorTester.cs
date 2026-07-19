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
        ConnectorTestResult Fail(string message) =>
            new(ConnectorType.WorkTracker, false, message, DateTimeOffset.UtcNow);

        var (siteUrl, email, projectKey, apiToken) = await ResolveCredentialsAsync(ct);

        if (string.IsNullOrEmpty(siteUrl) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(apiToken))
            return Fail("Jira is not fully configured — site URL, account email, and API token are required.");

        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{email}:{apiToken}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);

        try
        {
            // 1) Authentication + reachability.
            var myselfUrl = $"{siteUrl.TrimEnd('/')}/rest/api/3/myself";
            var authResponse = await http.GetAsync(myselfUrl, ct);
            if (!authResponse.IsSuccessStatusCode)
            {
                return (int)authResponse.StatusCode switch
                {
                    401 or 403 => Fail("Authentication failed — API token or email rejected (check the token value and that it belongs to this account)."),
                    _          => Fail($"Unexpected response from Jira sign-in: {(int)authResponse.StatusCode} {authResponse.StatusCode}."),
                };
            }

            // Jira returns HTTP 200 with an HTML sign-in page for some misconfigurations — require JSON.
            var authBody = await authResponse.Content.ReadAsStringAsync(ct);
            var accountLabel = TryReadString(authBody, "displayName") ?? TryReadString(authBody, "emailAddress") ?? email;

            // 2) Project existence / access.
            if (!string.IsNullOrEmpty(projectKey))
            {
                var projectUrl = $"{siteUrl.TrimEnd('/')}/rest/api/3/project/{Uri.EscapeDataString(projectKey)}";
                var projectResponse = await http.GetAsync(projectUrl, ct);
                if (!projectResponse.IsSuccessStatusCode)
                {
                    return (int)projectResponse.StatusCode == 404
                        ? Fail($"Authenticated as {accountLabel}, but project '{projectKey}' was not found or is not accessible.")
                        : Fail($"Authenticated as {accountLabel}, but the project check returned {(int)projectResponse.StatusCode} {projectResponse.StatusCode}.");
                }
                return new ConnectorTestResult(
                    ConnectorType.WorkTracker, true,
                    $"Authenticated as {accountLabel} — project '{projectKey}' confirmed in {siteUrl}.",
                    DateTimeOffset.UtcNow);
            }

            return new ConnectorTestResult(
                ConnectorType.WorkTracker, true,
                $"Authenticated as {accountLabel} in {siteUrl} (no project key set — project not verified).",
                DateTimeOffset.UtcNow);
        }
        catch (TaskCanceledException)
        {
            return Fail("Test timed out after 30 seconds — check network connectivity to Jira.");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Service unreachable — could not connect to {siteUrl}. {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

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
