// Identifies which instant-message platform a Messaging connector delivers to.
namespace DBAIAzure.Core.Models;

/// <summary>
/// The direct-message / instant-message platform a Messaging connector targets. Each platform has its
/// own webhook payload format and success signal (used by the webhook fallback path): Microsoft Teams
/// posts an Adaptive Card and returns the body "1"; Slack posts a text body and returns "ok"; Discord
/// posts a content body and returns HTTP 204. Adding a platform is a localized change — a new member
/// here plus a matching webhook profile.
/// </summary>
public enum MessagingPlatform
{
    /// <summary>Microsoft Teams — Adaptive Card webhook; success signalled by the response body "1".</summary>
    Teams,

    /// <summary>Slack — text/Block Kit webhook; success signalled by the response body "ok".</summary>
    Slack,

    /// <summary>Discord — content webhook; success signalled by HTTP 204 No Content.</summary>
    Discord,
}
