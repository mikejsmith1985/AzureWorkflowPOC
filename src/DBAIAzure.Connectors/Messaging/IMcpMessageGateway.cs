// MCP delivery gateway — sends a message by calling a send-message tool on a remote MCP server (FR-007).
namespace DBAIAzure.Connectors.Messaging;

/// <summary>
/// Sends a message through a remote MCP server's send-message tool over HTTP/SSE. Builds the tool's
/// arguments from a JSON argument template with <c>{{target}}</c>/<c>{{message}}</c> placeholders, so any
/// MCP server's argument names are expressible without code changes (FR-007, FR-014).
/// </summary>
public interface IMcpMessageGateway
{
    /// <summary>Calls the configured tool and returns whether the message was accepted. Never throws —
    /// transport/tool errors are returned as an unsuccessful result so delivery stays non-blocking.</summary>
    Task<McpSendResult> SendAsync(McpSendRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Inputs for one MCP send. <paramref name="AuthToken"/> is a secret and is never logged.</summary>
/// <param name="ServerUrl">Absolute http/https MCP endpoint (SSE / streamable HTTP).</param>
/// <param name="ToolName">The send-message tool to invoke.</param>
/// <param name="ArgumentTemplateJson">JSON object with {{target}}/{{message}} placeholders (may be blank → default).</param>
/// <param name="Target">Value substituted for {{target}} (channel / recipient id).</param>
/// <param name="Message">Value substituted for {{message}}.</param>
/// <param name="AuthToken">Optional bearer token sent as an Authorization header.</param>
public sealed record McpSendRequest(
    string ServerUrl,
    string ToolName,
    string? ArgumentTemplateJson,
    string Target,
    string Message,
    string? AuthToken);

/// <summary>Outcome of an MCP send. Message is human-readable and safe to log (no secret echoed).</summary>
public sealed record McpSendResult(bool IsSuccess, string Message);
