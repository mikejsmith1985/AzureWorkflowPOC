// Non-secret configuration fields for the multi-platform Messaging connector.
namespace DBAIAzure.Core.Models;

/// <summary>
/// Non-secret fields for the Messaging connector, serialized to/from <c>ConnectorConfig.NonSecretConfig</c>.
/// Credentials — the MCP auth token and the incoming-webhook URL — are stored separately in the encrypted
/// secrets blob and never appear in this record (Article IX). Delivery is MCP-first: when
/// <see cref="McpServerUrl"/> is set the connector calls the configured MCP tool; otherwise it falls back
/// to the platform's direct webhook.
/// </summary>
public sealed record MessagingConnectorConfig(
    /// <summary>The instant-message platform this connector targets.</summary>
    MessagingPlatform Platform,

    /// <summary>Remote MCP server endpoint (HTTP/SSE). When non-empty, the MCP delivery path is used.</summary>
    string? McpServerUrl = null,

    /// <summary>Name of the MCP send-message tool to invoke (required when <see cref="McpServerUrl"/> is set).</summary>
    string? McpToolName = null,

    /// <summary>
    /// JSON object describing the MCP tool's input arguments, using <c>{{target}}</c> and <c>{{message}}</c>
    /// placeholders. When null/blank a sensible default is used.
    /// </summary>
    string? McpArgumentTemplate = null,

    /// <summary>Channel id / recipient substituted for <c>{{target}}</c> on the MCP path (the webhook path
    /// encodes its channel in the URL, so this may be empty there).</summary>
    string? Target = null);
