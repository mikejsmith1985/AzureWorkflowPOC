// MCP delivery gateway implementation — connects to a remote MCP server and calls its send-message tool.
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace DBAIAzure.Connectors.Messaging;

/// <summary>
/// Sends a message via a remote MCP server using the official MCP client SDK over HTTP/SSE
/// (<see cref="HttpClientTransport"/> in auto-detect mode). A fresh client is connected per call and
/// disposed afterwards (sends are infrequent, so connection reuse is not worth the lifecycle complexity).
/// Tool, transport, and template errors are returned as failures, never thrown (FR-010).
/// </summary>
public sealed class McpMessageGateway : IMcpMessageGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc/>
    public async Task<McpSendResult> SendAsync(McpSendRequest request, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(request.ServerUrl, UriKind.Absolute, out var endpoint))
            return new McpSendResult(false, $"MCP server URL '{request.ServerUrl}' is not a valid absolute URL.");

        Dictionary<string, object?> arguments;
        try
        {
            var argsJson = McpArgumentTemplate.Substitute(request.ArgumentTemplateJson, request.Target, request.Message);
            arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson, JsonOptions) ?? new();
        }
        catch (JsonException ex)
        {
            return new McpSendResult(false, $"MCP argument template did not produce valid JSON: {ex.Message}");
        }

        var options = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.AutoDetect,
            Name = "messaging-mcp",
        };
        if (!string.IsNullOrEmpty(request.AuthToken))
            options.AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {request.AuthToken}" };

        try
        {
            var transport = new HttpClientTransport(options, NullLoggerFactory.Instance);
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);

            var result = await client.CallToolAsync(request.ToolName, arguments, cancellationToken: cancellationToken);

            if (result.IsError == true)
                return new McpSendResult(false,
                    $"MCP tool '{request.ToolName}' reported an error: {ExtractText(result) ?? "no detail"}.");

            return new McpSendResult(true, $"MCP tool '{request.ToolName}' accepted the message.");
        }
        catch (Exception ex)
        {
            // Connection refused, tool-not-found, timeout, protocol error — all surface as a clear failure.
            return new McpSendResult(false,
                $"Could not deliver via MCP server '{request.ServerUrl}' (tool '{request.ToolName}'): {ex.Message}");
        }
    }

    private static string? ExtractText(CallToolResult result) =>
        result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;
}
