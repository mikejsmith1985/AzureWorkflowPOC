// ServiceNow outbound connector — functional test and future intake extensions (FR-008).
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors;

/// <summary>
/// Outbound HTTP client for ServiceNow. Credentials are resolved from
/// <see cref="IConnectorConfigRepository"/> at each call (hot-reload — FR-014).
/// The functional test authenticates with Basic Auth and reads a system properties record to
/// confirm the instance is reachable and the credentials are valid (FR-008).
/// </summary>
public sealed class ServiceNowClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConnectorConfigRepository _configRepo;

    public ServiceNowClient(IHttpClientFactory httpClientFactory, IConnectorConfigRepository configRepo)
    {
        _httpClientFactory = httpClientFactory;
        _configRepo = configRepo;
    }

    /// <summary>
    /// Authenticates with the configured ServiceNow instance and calls the Table API to read
    /// one system property. Confirms both network reachability AND credential validity (FR-008).
    /// </summary>
    public async Task<ConnectorTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        ConnectorTestResult Fail(string message) =>
            new(ConnectorType.ServiceNow, false, message, DateTimeOffset.UtcNow);

        var (instanceUrl, username, password) = await ResolveCredentialsAsync(ct);

        if (string.IsNullOrEmpty(instanceUrl) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            return Fail("Connector is not configured — no credentials stored.");

        var http = _httpClientFactory.CreateClient(nameof(ServiceNowClient));

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", encoded);

        var url = $"{instanceUrl.TrimEnd('/')}/api/now/table/sys_properties?sysparm_limit=1";

        try
        {
            var response = await http.GetAsync(url, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return (int)response.StatusCode switch
                {
                    401 => Fail("Authentication failed — credentials were rejected (401 Unauthorized). Check the username and password/token."),
                    403 => Fail("Insufficient permissions — the service account lacks read access to sys_properties (403 Forbidden)."),
                    404 => Fail($"ServiceNow instance not found at '{instanceUrl}'. Check the instance URL."),
                    _   => Fail($"Unexpected response from ServiceNow: {(int)response.StatusCode} {response.StatusCode}."),
                };
            }

            using var doc = JsonDocument.Parse(body);
            var hasResult = doc.RootElement.TryGetProperty("result", out var result) &&
                            result.ValueKind == JsonValueKind.Array &&
                            result.GetArrayLength() > 0;

            if (!hasResult)
                return Fail("ServiceNow responded with HTTP 200 but the response body is not a valid Table API result. Check the instance URL and that the Table API is accessible.");

            return new ConnectorTestResult(
                ConnectorType.ServiceNow, true,
                $"Authenticated as '{username}' — ServiceNow instance at '{instanceUrl}' responded with system properties.",
                DateTimeOffset.UtcNow);
        }
        catch (TaskCanceledException)
        {
            return Fail("Test timed out after 35 seconds — check network connectivity to ServiceNow.");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Service unreachable — could not connect to '{instanceUrl}'. {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<(string InstanceUrl, string Username, string Password)> ResolveCredentialsAsync(
        CancellationToken ct)
    {
        try
        {
            var configResult = await _configRepo.GetAsync(ConnectorType.ServiceNow, ct);
            var nonSecret = configResult?.NonSecretConfig is { } nsJson
                ? JsonSerializer.Deserialize<ServiceNowConnectorConfig>(nsJson, JsonOptions)
                : null;

            var secretsJson = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.ServiceNow, ct);
            var secrets = secretsJson is not null
                ? JsonSerializer.Deserialize<ServiceNowSecrets>(secretsJson, JsonOptions)
                : null;

            return (
                nonSecret?.InstanceUrl ?? string.Empty,
                nonSecret?.Username ?? string.Empty,
                secrets?.Password ?? secrets?.ApiToken ?? string.Empty
            );
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty);
        }
    }

    private sealed record ServiceNowSecrets(string? Password, string? ApiToken);
}
