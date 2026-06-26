// Delivery seam for the Messaging connector — chooses MCP or webhook and hides platform specifics.
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Delivers a plain-text message to the configured messaging platform, choosing the MCP path when an MCP
/// server is configured and falling back to the platform's direct webhook otherwise (FR-005). Connector
/// configuration and secrets are resolved at each call so changes hot-reload without a restart (FR-015).
/// All three callers — HITL notifications, workflow notify nodes, and the Settings Test Connection — go
/// through this single seam so none of them branch on platform or delivery path.
/// </summary>
public interface IMessageDelivery
{
    /// <summary>
    /// Sends <paramref name="message"/> to the configured target. Returns the outcome, including which
    /// platform and delivery path were used. A delivery failure is returned as an unsuccessful result —
    /// it is never thrown — so callers (HITL/notify) stay non-blocking (FR-010).
    /// </summary>
    Task<MessageDeliveryResult> SendAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a fixed, clearly labelled connectivity-test message and returns a <see cref="ConnectorTestResult"/>
    /// whose message names the platform and the delivery path used (FR-008, FR-009).
    /// </summary>
    Task<ConnectorTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a single delivery attempt. Carries no secret values and is safe to log.</summary>
public sealed record MessageDeliveryResult(
    bool IsSuccess,
    MessagingPlatform Platform,
    DeliveryPath Path,
    string Message);

/// <summary>Which route a message took (or would take) — selected MCP-first with webhook fallback.</summary>
public enum DeliveryPath
{
    /// <summary>Delivered via a remote MCP server's send-message tool.</summary>
    Mcp,

    /// <summary>Delivered via the platform's direct incoming webhook.</summary>
    Webhook,

    /// <summary>Neither an MCP server nor a webhook is configured — nothing can be sent.</summary>
    NotConfigured,
}
