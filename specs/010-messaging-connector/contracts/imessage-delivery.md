# Contract: `IMessageDelivery`

**Feature**: 010-messaging-connector

The single seam through which all three behaviors (HITL notify, notify-node, Test Connection) send a
message. It encapsulates MCP-first-vs-webhook selection so callers never branch on platform or path.

```csharp
namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Delivers a plain-text message to the configured messaging platform, choosing the MCP path when an
/// MCP server is configured and falling back to the platform's direct webhook otherwise (FR-005).
/// Resolves connector configuration and secrets at each call (hot-reload, FR-015).
/// </summary>
public interface IMessageDelivery
{
    /// <summary>Sends <paramref name="message"/> to the configured target. Returns the outcome,
    /// including which platform and delivery path were used (FR-009). Never throws for a delivery
    /// failure — failures are returned as an unsuccessful result so callers stay non-blocking (FR-010).</summary>
    Task<MessageDeliveryResult> SendAsync(string message, CancellationToken ct = default);

    /// <summary>Sends a fixed, clearly labelled connectivity-test message and returns a
    /// <see cref="ConnectorTestResult"/> naming the platform and path used (FR-008, FR-009).</summary>
    Task<ConnectorTestResult> TestConnectionAsync(CancellationToken ct = default);
}

/// <summary>Outcome of a single delivery attempt. No secret values are ever included.</summary>
public sealed record MessageDeliveryResult(
    bool IsSuccess,
    MessagingPlatform Platform,
    DeliveryPath Path,
    string Message);   // human-readable; safe to log

public enum DeliveryPath { Mcp, Webhook, NotConfigured }
```

### Behavioral contract

| # | Given | When | Then |
|---|-------|------|------|
| C1 | `McpServerUrl` configured | `SendAsync` | Delivers via MCP tool call; `Path == Mcp`. |
| C2 | No MCP url, webhook stored | `SendAsync` | Delivers via webhook with platform-correct body; `Path == Webhook`. |
| C3 | Neither configured | `SendAsync`/`TestConnectionAsync` | `IsSuccess == false`, `Path == NotConfigured`, message = "not configured". |
| C4 | MCP url set but server unreachable | `SendAsync` | `IsSuccess == false`, `Path == Mcp`; **no** silent webhook fallback; never throws. |
| C5 | Any success | `TestConnectionAsync` | Result message names the platform AND the path (FR-009). |
| C6 | Stored secret undecryptable | any | Treated as not-configured / re-enter; never throws (Edge Cases). |

### Notes

- `TeamsHitlNotifier` becomes `MessagingHitlNotifier` and delegates HITL formatting to this seam;
  `IHitlNotifier` (existing signature) is unchanged.
- `TeamsConnectorAdapter` becomes `MessagingConnectorAdapter`, delegating `ExecuteAsync`/`HealthCheckAsync`
  to this seam; `IConnectorAdapter` (existing) is unchanged.
- `ConnectorHealthChecker` calls `TestConnectionAsync` for `ConnectorType.Messaging`.
