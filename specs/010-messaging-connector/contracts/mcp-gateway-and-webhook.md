# Contract: MCP gateway & webhook profiles

**Feature**: 010-messaging-connector

Two strategy seams behind `IMessageDelivery`. Both are pure with respect to configuration inputs
(easy to unit-test with a fake HTTP handler / fake MCP client).

---

## `IMcpMessageGateway` — MCP path

```csharp
namespace DBAIAzure.Connectors.Messaging;

/// <summary>
/// Sends a message by calling a send-message tool on a remote MCP server over HTTP/SSE.
/// Builds the tool arguments from a JSON argument template (FR-007).
/// </summary>
public interface IMcpMessageGateway
{
    Task<McpSendResult> SendAsync(McpSendRequest request, CancellationToken ct = default);
}

/// <param name="ServerUrl">Absolute http/https MCP endpoint (SSE/streamable HTTP).</param>
/// <param name="ToolName">Tool to invoke.</param>
/// <param name="ArgumentTemplateJson">JSON object with {{target}}/{{message}} placeholders.</param>
/// <param name="Target">Value substituted for {{target}}.</param>
/// <param name="Message">Value substituted for {{message}}.</param>
/// <param name="AuthToken">Optional bearer token (Authorization header). Never logged.</param>
public sealed record McpSendRequest(
    string ServerUrl, string ToolName, string ArgumentTemplateJson,
    string Target, string Message, string? AuthToken);

public sealed record McpSendResult(bool IsSuccess, string Message);
```

### Argument-template substitution (testable unit)

1. Start from `ArgumentTemplateJson`, or the default `{"target":"{{target}}","text":"{{message}}"}`
   when null/blank.
2. Replace `{{target}}` → `Target` and `{{message}}` → `Message` **as JSON string values**
   (values are JSON-escaped before substitution so a message containing quotes/newlines stays valid).
3. Parse the result into the argument dictionary passed to `CallToolAsync(ToolName, args, ct)`.

### Success/failure mapping

| MCP outcome | Result |
|-------------|--------|
| Tool returns, `IsError == false` | `IsSuccess = true`. |
| Tool returns `IsError == true` | `IsSuccess = false`, message = sanitized tool error (no token echoed). |
| Tool not found on server | `IsSuccess = false`, message = "tool '<name>' not found on MCP server". |
| Connect/timeout/transport error | `IsSuccess = false`, message = "could not reach MCP server …". |

Implementation note: backed by `ModelContextProtocol.Core` —
`McpClientFactory.CreateAsync(new SseClientTransport(new SseClientTransportOptions{ Endpoint = ... , AdditionalHeaders = auth }))`,
one client per call (R3), disposed after.

---

## `IPlatformWebhookProfile` — webhook fallback path

```csharp
namespace DBAIAzure.Connectors.Messaging;

/// <summary>Per-platform incoming-webhook payload + success contract (R4, FR-006).</summary>
public interface IPlatformWebhookProfile
{
    MessagingPlatform Platform { get; }
    string BuildBody(string message);                       // platform-correct JSON
    bool IsSuccess(int statusCode, string responseBody);    // platform success signal
}
```

| Platform | `BuildBody` | `IsSuccess` |
|----------|-------------|-------------|
| Teams | Adaptive Card wrapping `message` (reuse existing builder) | `statusCode is 2xx && body.Trim('"').Trim() == "1"` |
| Slack | `{"text": <json-escaped message>}` | `statusCode is 2xx && body.Trim() == "ok"` |
| Discord | `{"content": <json-escaped message>}` | `statusCode == 204` |

The webhook URL itself comes from `MessagingSecrets.WebhookUrl` (encrypted) and is POSTed with
`application/json`.
