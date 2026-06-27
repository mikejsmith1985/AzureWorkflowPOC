// Functional connectivity test for the LLM provider via a minimal inference request (FR-010).
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors;

/// <summary>
/// Tests the LLM connector by sending a minimal inference request to the configured provider.
/// Credentials and model name are resolved from <see cref="IConnectorConfigRepository"/> at
/// each call (hot-reload — FR-014). Verifies that the API key is valid AND that the model is
/// accessible — not just that the endpoint is reachable (FR-010).
/// </summary>
public sealed class LlmConnectorTester
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectorConfigRepository _configRepo;
    private readonly IHttpClientFactory _httpClientFactory;

    public LlmConnectorTester(IConnectorConfigRepository configRepo, IHttpClientFactory httpClientFactory)
    {
        _configRepo = configRepo;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Posts a minimal inference request (<c>max_tokens: 5</c>) to the stored provider endpoint
    /// and verifies that the model returns a non-empty text response (FR-010).
    /// </summary>
    public async Task<ConnectorTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        ConnectorTestResult Fail(string message) =>
            new(ConnectorType.LLM, false, message, DateTimeOffset.UtcNow);

        var (endpoint, model, apiKey) = await ResolveCredentialsAsync(ct);

        if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(model) || string.IsNullOrEmpty(endpoint))
            return Fail("Connector is not configured — no credentials stored.");

        var http = _httpClientFactory.CreateClient();
        http.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        http.Timeout = TimeSpan.FromSeconds(35);

        // Minimal valid inference — 5 tokens, single turn.
        var probe = new
        {
            model,
            max_tokens = 5,
            messages = new[] { new { role = "user", content = "Respond with the word READY." } },
        };

        var json = JsonSerializer.Serialize(probe, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await http.PostAsync("/v1/messages", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return (int)response.StatusCode switch
                {
                    401 => Fail("Authentication failed — the API key was rejected (401 Unauthorized). Check the key value and try again."),
                    403 => Fail("Access denied — the API key lacks permission to use this model (403 Forbidden)."),
                    404 => Fail($"Model '{model}' not found or not available on this account (404). Check the model name."),
                    429 => Fail("Request rate limit or quota exceeded (429). Check your account quota."),
                    _   => Fail($"Unexpected response from LLM provider: {(int)response.StatusCode} {response.StatusCode}."),
                };
            }

            using var doc = JsonDocument.Parse(body);
            var hasText = doc.RootElement.TryGetProperty("content", out var contentArray) &&
                          contentArray.ValueKind == JsonValueKind.Array &&
                          contentArray.GetArrayLength() > 0 &&
                          contentArray[0].TryGetProperty("text", out var textProp) &&
                          !string.IsNullOrWhiteSpace(textProp.GetString());

            if (!hasText)
                return Fail("LLM provider responded with HTTP 200 but the response did not contain text content. Check the model name and provider endpoint.");

            return new ConnectorTestResult(
                ConnectorType.LLM, true,
                $"Model '{model}' responded to a test inference request via {endpoint}.",
                DateTimeOffset.UtcNow);
        }
        catch (TaskCanceledException)
        {
            return Fail("Test timed out after 35 seconds — check network connectivity to the LLM provider.");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Service unreachable — {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<(string Endpoint, string Model, string ApiKey)> ResolveCredentialsAsync(
        CancellationToken ct)
    {
        try
        {
            var configResult = await _configRepo.GetAsync(ConnectorType.LLM, ct);
            var nonSecret = configResult?.NonSecretConfig is { } nsJson
                ? JsonSerializer.Deserialize<LlmConnectorConfig>(nsJson, WebJsonOptions)
                : null;

            var secretsJson = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.LLM, ct);
            var secrets = secretsJson is not null
                ? JsonSerializer.Deserialize<LlmSecrets>(secretsJson, WebJsonOptions)
                : null;

            return (
                EndpointFromProvider(nonSecret?.Provider ?? string.Empty),
                nonSecret?.ModelName ?? string.Empty,
                secrets?.ApiKey ?? string.Empty
            );
        }
        catch
        {
            return (string.Empty, string.Empty, string.Empty);
        }
    }

    private static string EndpointFromProvider(string provider) => provider.ToLowerInvariant() switch
    {
        "anthropic" => "https://api.anthropic.com",
        "openai"    => "https://api.openai.com",
        _           => string.Empty,
    };

    private sealed record LlmSecrets(string? ApiKey);
}
