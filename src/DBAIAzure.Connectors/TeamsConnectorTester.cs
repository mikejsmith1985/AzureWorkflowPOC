// Functional connectivity test for the Teams webhook (FR-011).
using System.Text;
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors;

/// <summary>
/// Tests the Teams connector by posting a labelled Adaptive Card to the configured webhook URL
/// (FR-011). A Teams webhook returns the string <c>1</c> on success — this is the verified pass
/// condition, not just an HTTP 200.
/// </summary>
public sealed class TeamsConnectorTester
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Labelled test payload so operators can identify the message in Teams (SC-003).
    private static readonly string TestCardJson = """
        {
          "type": "message",
          "attachments": [{
            "contentType": "application/vnd.microsoft.card.adaptive",
            "content": {
              "type": "AdaptiveCard",
              "version": "1.2",
              "body": [{ "type": "TextBlock", "text": "Pipeline connector test — this message confirms your Teams channel is reachable from the pipeline. No action required." }]
            }
          }]
        }
        """;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConnectorConfigRepository _configRepo;

    public TeamsConnectorTester(IHttpClientFactory httpClientFactory, IConnectorConfigRepository configRepo)
    {
        _httpClientFactory = httpClientFactory;
        _configRepo = configRepo;
    }

    /// <summary>
    /// Posts a labeled Adaptive Card to the configured Teams webhook and verifies the response
    /// body equals <c>1</c> (the Teams accepted-delivery signal, FR-011).
    /// </summary>
    public async Task<ConnectorTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        ConnectorTestResult Fail(string message) =>
            new(ConnectorType.Teams, false, message, DateTimeOffset.UtcNow);

        var webhookUrl = await ResolveWebhookUrlAsync(ct);

        if (string.IsNullOrEmpty(webhookUrl))
            return Fail("Connector is not configured — no webhook URL stored.");

        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(30);

        try
        {
            using var content = new StringContent(TestCardJson, Encoding.UTF8, "application/json");
            var response = await http.PostAsync(webhookUrl, content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return (int)response.StatusCode switch
                {
                    400 => Fail("Teams rejected the webhook payload (400 Bad Request). The webhook URL may be malformed."),
                    401 => Fail("Teams rejected the webhook authentication (401). The webhook URL may have been revoked."),
                    403 => Fail("Teams denied the webhook (403). The webhook URL may belong to a deleted channel or connector."),
                    _   => Fail($"Unexpected response from Teams: {(int)response.StatusCode} {response.StatusCode}."),
                };
            }

            // Teams Power Automate webhooks return the string "1" on successful delivery.
            if (body.Trim('"').Trim() == "1")
            {
                return new ConnectorTestResult(
                    ConnectorType.Teams, true,
                    "Teams webhook accepted the test message — check the configured channel for a connector test card.",
                    DateTimeOffset.UtcNow);
            }

            return Fail($"Teams responded with HTTP 200 but unexpected body '{body}'. The webhook URL may not point to a Teams incoming webhook.");
        }
        catch (TaskCanceledException)
        {
            return Fail("Test timed out after 30 seconds — check network connectivity to Microsoft Teams.");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Service unreachable — could not POST to the Teams webhook. {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<string?> ResolveWebhookUrlAsync(CancellationToken ct)
    {
        try
        {
            var secretsJson = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.Teams, ct);
            if (secretsJson is null) return null;
            var secrets = JsonSerializer.Deserialize<TeamsSecrets>(secretsJson, JsonOptions);
            return secrets?.WebhookUrl;
        }
        catch
        {
            return null;
        }
    }

    internal sealed record TeamsSecrets(string? WebhookUrl);
}
