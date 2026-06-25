// Builds MCP tool arguments by substituting {{target}}/{{message}} into a JSON template (FR-007).
using System.Text.Json;

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

    /// <summary>The template used when the operator leaves the argument template blank.</summary>
    public const string Default = """{"target":"{{target}}","text":"{{message}}"}""";

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
