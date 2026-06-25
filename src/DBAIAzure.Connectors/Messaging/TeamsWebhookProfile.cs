// Microsoft Teams incoming-webhook profile — Adaptive Card body, "1" success signal (FR-006).
using System.Text.Json;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors.Messaging;

/// <summary>
/// Teams incoming webhook contract: wraps the message in an Adaptive Card attachment and treats the
/// response body "1" (the Teams/Power Automate accepted-delivery signal) as success. Preserves the
/// behavior of the original Teams connector.
/// </summary>
public sealed class TeamsWebhookProfile : IPlatformWebhookProfile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc/>
    public MessagingPlatform Platform => MessagingPlatform.Teams;

    /// <inheritdoc/>
    public string BuildBody(string message) => JsonSerializer.Serialize(new
    {
        type = "message",
        attachments = new[]
        {
            new
            {
                contentType = "application/vnd.microsoft.card.adaptive",
                content = new
                {
                    type = "AdaptiveCard",
                    version = "1.2",
                    body = new[] { new { type = "TextBlock", text = message, wrap = true } },
                },
            },
        },
    }, JsonOptions);

    /// <inheritdoc/>
    public bool IsSuccess(int statusCode, string responseBody) =>
        statusCode is >= 200 and < 300 && responseBody.Trim().Trim('"').Trim() == "1";
}
