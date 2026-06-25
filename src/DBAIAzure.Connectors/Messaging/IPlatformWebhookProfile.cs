// Per-platform incoming-webhook payload format and success signal (webhook fallback path, FR-006).
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors.Messaging;

/// <summary>
/// Encapsulates one messaging platform's direct incoming-webhook contract: how to shape the request body
/// for a plain-text message and how to recognize a successful delivery. Each platform's success signal is
/// distinct (Teams returns "1", Slack returns "ok", Discord returns HTTP 204), so a generic "any 2xx"
/// check is insufficient. Adding a platform = add one implementation of this interface.
/// </summary>
public interface IPlatformWebhookProfile
{
    /// <summary>The platform this profile serves.</summary>
    MessagingPlatform Platform { get; }

    /// <summary>Builds the JSON request body that delivers <paramref name="message"/> on this platform.</summary>
    string BuildBody(string message);

    /// <summary>True when the platform's response indicates the message was accepted.</summary>
    bool IsSuccess(int statusCode, string responseBody);
}
