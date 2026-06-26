// Discord incoming-webhook profile — {"content": …} body, HTTP 204 success signal (FR-006).
using System.Text.Json;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors.Messaging;

/// <summary>
/// Discord webhook contract: posts a <c>{"content": …}</c> body and treats HTTP 204 No Content (Discord's
/// accepted-delivery response) as success. Discord returns 204 with an empty body, so the body is ignored.
/// </summary>
public sealed class DiscordWebhookProfile : IPlatformWebhookProfile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc/>
    public MessagingPlatform Platform => MessagingPlatform.Discord;

    /// <inheritdoc/>
    public string BuildBody(string message) => JsonSerializer.Serialize(new { content = message }, JsonOptions);

    /// <inheritdoc/>
    public bool IsSuccess(int statusCode, string responseBody) => statusCode == 204;
}
