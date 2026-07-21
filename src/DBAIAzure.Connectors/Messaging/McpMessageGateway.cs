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
        if (!Uri.TryCreate(request.ServerUrl, UriKind.Absolute, out _))
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

        var outcome = await CallToolAsync(request.ServerUrl, request.ToolName, arguments, request.AuthToken, cancellationToken);
        if (!outcome.Reached)
            return new McpSendResult(false, outcome.Error!);
        if (outcome.IsToolError)
            return new McpSendResult(false,
                $"MCP tool '{request.ToolName}' reported an error: {outcome.Content ?? "no detail"}.");

        return new McpSendResult(true, $"MCP tool '{request.ToolName}' accepted the message.");
    }

    /// <inheritdoc/>
    public async Task<McpReadResult> ReadAsync(McpReadRequest request, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(request.ServerUrl, UriKind.Absolute, out _))
            return new McpReadResult(false, null, $"MCP server URL '{request.ServerUrl}' is not a valid absolute URL.");

        Dictionary<string, object?> arguments;
        try
        {
            arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(request.ArgumentsJson, JsonOptions) ?? new();
        }
        catch (JsonException ex)
        {
            return new McpReadResult(false, null, $"MCP read arguments were not valid JSON: {ex.Message}");
        }

        var outcome = await CallToolAsync(request.ServerUrl, request.ToolName, arguments, request.AuthToken, cancellationToken);
        if (!outcome.Reached)
            return new McpReadResult(false, null, outcome.Error!);
        if (outcome.IsToolError)
            return new McpReadResult(false, null,
                $"MCP tool '{request.ToolName}' reported an error: {outcome.Content ?? "no detail"}.");

        return new McpReadResult(true, outcome.Content, $"MCP tool '{request.ToolName}' returned content.");
    }

    // Connects a fresh MCP client and invokes one tool. Shared by send and read so the transport, auth-header,
    // and error handling live in one place. Never throws — an unreachable server / failed call is reported.
    private static async Task<ToolCallOutcome> CallToolAsync(
        string serverUrl, string toolName, Dictionary<string, object?> arguments, string? authToken,
        CancellationToken cancellationToken)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = new Uri(serverUrl), // already validated as absolute by the caller
            TransportMode = HttpTransportMode.AutoDetect,
            Name = "messaging-mcp",
        };
        if (!string.IsNullOrEmpty(authToken))
            options.AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {authToken}" };

        try
        {
            var transport = new HttpClientTransport(options, NullLoggerFactory.Instance);
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
            return new ToolCallOutcome(true, result.IsError == true, ExtractText(result), null);
        }
        catch (Exception ex)
        {
            // Connection refused, tool-not-found, timeout, protocol error — all surface as a clear failure.
            return new ToolCallOutcome(false, false, null,
                $"Could not reach MCP server '{serverUrl}' (tool '{toolName}'): {ex.Message}");
        }
    }

    private static string? ExtractText(CallToolResult result) =>
        result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;

    /// <summary>The result of one tool invocation: whether the server was reached, whether the tool itself
    /// errored, the returned text content, and a transport error message when the server was unreachable.</summary>
    private sealed record ToolCallOutcome(bool Reached, bool IsToolError, string? Content, string? Error);
}
