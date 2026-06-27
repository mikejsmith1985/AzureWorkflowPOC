// Slack incoming-webhook profile — {"text": …} body, "ok" success signal (FR-006).
using System.Text.Json;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors.Messaging;

/// <summary>
/// Slack incoming webhook contract: posts a plain <c>{"text": …}</c> body (Block Kit can be layered later)
/// and treats the response body "ok" as success.
/// </summary>
public sealed class SlackWebhookProfile : IPlatformWebhookProfile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc/>
    public MessagingPlatform Platform => MessagingPlatform.Slack;

    /// <inheritdoc/>
    public string BuildBody(string message) => JsonSerializer.Serialize(new { text = message }, JsonOptions);

    /// <inheritdoc/>
    public bool IsSuccess(int statusCode, string responseBody) =>
        statusCode is >= 200 and < 300 && responseBody.Trim() == "ok";
}
