// Builds MCP tool arguments by substituting {{target}}/{{message}} into a JSON template (FR-007).
using System.Text.Json;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors.Messaging;

/// <summary>
/// Produces the JSON arguments for an MCP send-message tool from an operator-supplied template. The
/// template is a JSON object whose string values may contain the placeholders <c>{{target}}</c> and
/// <c>{{message}}</c>; both are substituted with JSON-escaped values so the result stays valid JSON even
/// when the message contains quotes or newlines. Kept separate from the gateway so the substitution rule
/// is unit-testable without a live MCP server.
/// </summary>
public static class McpArgumentTemplate
{
    private const string TargetPlaceholder = "{{target}}";
    private const string MessagePlaceholder = "{{message}}";

    /// <summary>
    /// Generic fallback used when the operator leaves the template blank on a platform that has no verified
    /// tool schema. Its <c>target</c>/<c>text</c> keys match no specific MCP tool, so operators on those
    /// platforms are expected to override it with their tool's real argument names.
    /// </summary>
    public const string Default = """{"target":"{{target}}","text":"{{message}}"}""";

    /// <summary>
    /// Default for Slack's hosted MCP server. Its <c>slack_send_message</c> tool requires exactly
    /// <c>channel_id</c> and <c>message</c>; any other body key yields a <c>no_text</c> error. Verified
    /// against mcp.slack.com.
    /// </summary>
    public const string SlackDefault = """{"channel_id":"{{target}}","message":"{{message}}"}""";

    /// <summary>
    /// Returns the best-known default template for <paramref name="platform"/> when the operator has not
    /// supplied one. Slack has a verified tool schema and gets its specific keys; every other platform
    /// falls back to the generic template, which the operator should override for their MCP tool.
    /// </summary>
    public static string DefaultFor(MessagingPlatform platform) => platform switch
    {
        MessagingPlatform.Slack => SlackDefault,
        _ => Default,
    };

    /// <summary>
    /// Returns the template (or <see cref="Default"/> when blank) with the placeholders replaced by the
    /// JSON-escaped <paramref name="target"/> and <paramref name="message"/>. The placeholders are expected
    /// to sit inside JSON string literals (e.g. <c>"text":"{{message}}"</c>), so the escaped inner value is
    /// substituted without adding quotes.
    /// </summary>
    public static string Substitute(string? templateJson, string target, string message)
    {
        var template = string.IsNullOrWhiteSpace(templateJson) ? Default : templateJson;
        return template
            .Replace(TargetPlaceholder, JsonEscapeInner(target))
            .Replace(MessagePlaceholder, JsonEscapeInner(message));
    }

    // Serialize the value as a JSON string then strip the surrounding quotes, leaving the escaped contents
    // suitable for placement inside an existing pair of quotes in the template.
    private static string JsonEscapeInner(string value)
    {
        var quoted = JsonSerializer.Serialize(value);
        return quoted.Length >= 2 ? quoted[1..^1] : quoted;
    }
}
