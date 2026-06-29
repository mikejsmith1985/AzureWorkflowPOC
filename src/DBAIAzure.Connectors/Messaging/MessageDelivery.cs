// Messaging delivery seam — MCP-first selection with webhook fallback (FR-005), platform-agnostic to callers.
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors.Messaging;

/// <summary>
/// Single delivery seam behind the Messaging connector. Resolves the connector's configuration and
/// decrypted secrets at each call (hot-reload, FR-015), chooses the MCP path when an MCP server is
/// configured and the platform's direct webhook otherwise (FR-005), and never throws on a delivery
/// failure so HITL/notify callers stay non-blocking (FR-010). The MCP path is wired in a later phase;
/// until then a configured MCP server reports that MCP delivery is unavailable rather than silently
/// using the webhook.
/// </summary>
public sealed class MessageDelivery : IMessageDelivery
{
    // Labelled so an operator can recognise the connectivity-test message in the channel (SC-003).
    private const string TestMessage =
        "Pipeline connector test — this confirms your messaging channel is reachable from the pipeline. No action required.";

    private const int WebhookTimeoutSeconds = 30;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly IConnectorConfigRepository _configRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IReadOnlyDictionary<MessagingPlatform, IPlatformWebhookProfile> _webhookProfiles;
    private readonly IMcpMessageGateway? _mcpGateway;

    public MessageDelivery(
        IConnectorConfigRepository configRepo,
        IHttpClientFactory httpClientFactory,
        IEnumerable<IPlatformWebhookProfile> webhookProfiles,
        IMcpMessageGateway? mcpGateway = null)
    {
        _configRepo = configRepo;
        _httpClientFactory = httpClientFactory;
        _webhookProfiles = webhookProfiles.ToDictionary(profile => profile.Platform);
        _mcpGateway = mcpGateway;
    }

    /// <inheritdoc/>
    public async Task<MessageDeliveryResult> SendAsync(string message, CancellationToken cancellationToken = default)
    {
        var (config, secrets) = await ResolveAsync(cancellationToken);

        if (config is null)
            return new MessageDeliveryResult(false, MessagingPlatform.Teams, DeliveryPath.NotConfigured,
                "Messaging connector is not configured.");

        // MCP-first: a configured MCP server endpoint selects the MCP path — no silent webhook fallback.
        if (!string.IsNullOrWhiteSpace(config.McpServerUrl))
            return await SendViaMcpAsync(config, secrets, message, cancellationToken);

        if (!string.IsNullOrWhiteSpace(secrets?.WebhookUrl))
            return await SendViaWebhookAsync(config.Platform, secrets!.WebhookUrl!, message, cancellationToken);

        return new MessageDeliveryResult(false, config.Platform, DeliveryPath.NotConfigured,
            $"{config.Platform}: not configured — no MCP server or webhook URL stored.");
    }

    /// <inheritdoc/>
    public async Task<ConnectorTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var result = await SendAsync(TestMessage, cancellationToken);

        var pathLabel = result.Path switch
        {
            DeliveryPath.Mcp     => "MCP",
            DeliveryPath.Webhook => "webhook",
            _                    => "none",
        };

        var message = result.IsSuccess
            ? $"{result.Platform} reachable via {pathLabel} — a labelled test message was sent. Check the channel."
            : result.Message;

        return new ConnectorTestResult(ConnectorType.Messaging, result.IsSuccess, message, DateTimeOffset.UtcNow);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<MessageDeliveryResult> SendViaMcpAsync(
        MessagingConnectorConfig config, MessagingSecrets? secrets, string message, CancellationToken cancellationToken)
    {
        if (_mcpGateway is null)
            return new MessageDeliveryResult(false, config.Platform, DeliveryPath.Mcp,
                $"{config.Platform}: MCP delivery is not available.");

        if (string.IsNullOrWhiteSpace(config.McpToolName))
            return new MessageDeliveryResult(false, config.Platform, DeliveryPath.Mcp,
                $"{config.Platform}: an MCP server is configured but no tool name is set.");

        // When the operator left the argument template blank, fall back to a platform-aware default so a
        // Slack connector works out of the box (Slack requires channel_id/message, not the generic keys).
        var argumentTemplate = string.IsNullOrWhiteSpace(config.McpArgumentTemplate)
            ? McpArgumentTemplate.DefaultFor(config.Platform)
            : config.McpArgumentTemplate;

        var request = new McpSendRequest(
            config.McpServerUrl!, config.McpToolName!, argumentTemplate,
            config.Target ?? string.Empty, message, secrets?.McpAuthToken);

        var result = await _mcpGateway.SendAsync(request, cancellationToken);

        return new MessageDeliveryResult(
            result.IsSuccess, config.Platform, DeliveryPath.Mcp,
            result.IsSuccess
                ? $"{config.Platform} accepted the message via MCP (tool '{config.McpToolName}')."
                : result.Message);
    }

    private async Task<MessageDeliveryResult> SendViaWebhookAsync(
        MessagingPlatform platform, string webhookUrl, string message, CancellationToken cancellationToken)
    {
        if (!_webhookProfiles.TryGetValue(platform, out var profile))
            return new MessageDeliveryResult(false, platform, DeliveryPath.Webhook,
                $"{platform}: no webhook profile is registered for this platform.");

        var http = _httpClientFactory.CreateClient(nameof(MessageDelivery));
        http.Timeout = TimeSpan.FromSeconds(WebhookTimeoutSeconds);

        try
        {
            using var content = new StringContent(profile.BuildBody(message), Encoding.UTF8, "application/json");
            var response = await http.PostAsync(webhookUrl, content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (profile.IsSuccess((int)response.StatusCode, body))
                return new MessageDeliveryResult(true, platform, DeliveryPath.Webhook,
                    $"{platform} accepted the message via webhook.");

            return new MessageDeliveryResult(false, platform, DeliveryPath.Webhook,
                $"{platform} webhook rejected the message (HTTP {(int)response.StatusCode}). The webhook URL may be wrong for this platform.");
        }
        catch (TaskCanceledException)
        {
            return new MessageDeliveryResult(false, platform, DeliveryPath.Webhook,
                $"{platform} webhook timed out after {WebhookTimeoutSeconds} seconds — check network connectivity.");
        }
        catch (HttpRequestException ex)
        {
            return new MessageDeliveryResult(false, platform, DeliveryPath.Webhook,
                $"{platform} webhook unreachable — {ex.Message}");
        }
    }

    private async Task<(MessagingConnectorConfig? Config, MessagingSecrets? Secrets)> ResolveAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var configResult = await _configRepo.GetAsync(ConnectorType.Messaging, cancellationToken);
            var nonSecret = configResult?.NonSecretConfig is { } json
                ? JsonSerializer.Deserialize<MessagingConnectorConfig>(json, JsonOptions)
                : null;

            var secretsJson = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.Messaging, cancellationToken);
            var secrets = secretsJson is not null
                ? JsonSerializer.Deserialize<MessagingSecrets>(secretsJson, JsonOptions)
                : null;

            return (nonSecret, secrets);
        }
        catch
        {
            // Undecryptable secret / malformed config → treat as not configured rather than crash (Edge Cases).
            return (null, null);
        }
    }

    /// <summary>Encrypted secret shape for the Messaging connector. Both fields are optional.</summary>
    internal sealed record MessagingSecrets(string? McpAuthToken, string? WebhookUrl);
}
