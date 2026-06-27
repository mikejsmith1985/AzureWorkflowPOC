// Functional connectivity test for Azure DevOps Boards via the REST API (FR-009).
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors;

/// <summary>
/// Tests the Azure DevOps connector by calling the project REST endpoint with the stored PAT.
/// Credentials are resolved from <see cref="IConnectorConfigRepository"/> at each call (hot-reload — FR-014).
/// Lives in DBAIAzure.Connectors so <see cref="ConnectorHealthChecker"/> can orchestrate all four
/// testers from the same assembly without a circular reference to DBAIAzure.Web.
/// </summary>
public sealed class AdoConnectorTester
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectorConfigRepository _configRepo;
    private readonly IHttpClientFactory _httpClientFactory;

    public AdoConnectorTester(IConnectorConfigRepository configRepo, IHttpClientFactory httpClientFactory)
    {
        _configRepo = configRepo;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Calls <c>GET {OrganizationUrl}/_apis/projects/{ProjectName}?api-version=7.1</c> with the stored PAT
    /// and verifies the project name in the response body (FR-009).
    /// </summary>
    public async Task<ConnectorTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        ConnectorTestResult Fail(string message) =>
            new(ConnectorType.AzureDevOps, false, message, DateTimeOffset.UtcNow);

        var (orgUrl, project, pat) = await ResolveCredentialsAsync(ct);

        if (string.IsNullOrEmpty(pat) || string.IsNullOrEmpty(orgUrl) || string.IsNullOrEmpty(project))
            return Fail("Connector is not configured — no credentials stored.");

        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);

        var url = $"{orgUrl.TrimEnd('/')}/_apis/projects/{Uri.EscapeDataString(project)}?api-version=7.1";

        try
        {
            var response = await http.GetAsync(url, ct);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);
                var name = doc.RootElement.TryGetProperty("name", out var nameProp)
                    ? nameProp.GetString() ?? project
                    : project;
                return new ConnectorTestResult(
                    ConnectorType.AzureDevOps, true,
                    $"Authenticated — project '{name}' confirmed in {orgUrl}.",
                    DateTimeOffset.UtcNow);
            }

            return (int)response.StatusCode switch
            {
                401 or 203 => Fail("Authentication failed — PAT rejected. Check the token value and scope (needs 'Project: Read')."),
                403        => Fail("Insufficient permissions — PAT lacks read access to this project (403 Forbidden)."),
                404        => Fail($"Project '{project}' not found in {orgUrl}. Check the project name."),
                _          => Fail($"Unexpected response from Azure DevOps: {(int)response.StatusCode} {response.StatusCode}."),
            };
        }
        catch (TaskCanceledException)
        {
            return Fail("Test timed out after 30 seconds — check network connectivity to Azure DevOps.");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Service unreachable — could not connect to {orgUrl}. {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<(string OrgUrl, string Project, string Pat)> ResolveCredentialsAsync(
        CancellationToken ct)
    {
        try
        {
            var configResult = await _configRepo.GetAsync(ConnectorType.AzureDevOps, ct);
            var nonSecret = configResult?.NonSecretConfig is { } nsJson
                ? JsonSerializer.Deserialize<AzureDevOpsConnectorConfig>(nsJson, JsonOptions)
                : null;

            var secretsJson = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.AzureDevOps, ct);
            var secrets = secretsJson is not null
                ? JsonSerializer.Deserialize<AzureDevOpsSecrets>(secretsJson, JsonOptions)
                : null;

            return (
                nonSecret?.OrganizationUrl ?? string.Empty,
                nonSecret?.ProjectName ?? string.Empty,
                secrets?.PersonalAccessToken ?? string.Empty
            );
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty);
        }
    }

    private sealed record AzureDevOpsSecrets(string? PersonalAccessToken);
}
