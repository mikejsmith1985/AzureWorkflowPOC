// Translates between the single "argument overrides" box an operator types into and the three per-tool argument
// templates the Jira MCP client uses, so one optional field covers servers whose tools need extra arguments.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DBAIAzure.Core.Models;

/// <summary>The three per-tool argument templates, each null when the operator left it to the default.</summary>
/// <param name="Read">Arguments for the read-issue tool.</param>
/// <param name="Transition">Arguments for the transition tool.</param>
/// <param name="Search">Arguments for the JQL search tool.</param>
public sealed record JiraMcpArgumentTemplates(string? Read, string? Transition, string? Search);

/// <summary>
/// Parses and renders the operator-facing overrides blob — a JSON object keyed by tool role
/// (<c>read</c> / <c>transition</c> / <c>search</c>) whose values are the argument objects to send. Pure and
/// forgiving: anything unparseable yields no overrides, which falls back to the built-in argument shapes rather
/// than failing a save.
/// </summary>
public static class JiraMcpArgumentOverrides
{
    private const string ReadKey = "read";
    private const string TransitionKey = "transition";
    private const string SearchKey = "search";

    /// <summary>Splits the overrides blob into per-tool templates; all null when blank or malformed.</summary>
    public static JiraMcpArgumentTemplates Parse(string? overridesJson)
    {
        if (string.IsNullOrWhiteSpace(overridesJson))
            return new JiraMcpArgumentTemplates(null, null, null);

        try
        {
            if (JsonNode.Parse(overridesJson) is not JsonObject root)
                return new JiraMcpArgumentTemplates(null, null, null);

            return new JiraMcpArgumentTemplates(
                ReadTemplate(root, ReadKey), ReadTemplate(root, TransitionKey), ReadTemplate(root, SearchKey));
        }
        catch (JsonException)
        {
            return new JiraMcpArgumentTemplates(null, null, null);
        }
    }

    /// <summary>
    /// Rebuilds the operator-facing blob from stored templates, so reopening the form shows what was saved.
    /// Returns an empty string when no template is set, leaving the field blank rather than showing "{}".
    /// </summary>
    public static string Format(JiraMcpArgumentTemplates templates)
    {
        var root = new JsonObject();
        AddTemplate(root, ReadKey, templates.Read);
        AddTemplate(root, TransitionKey, templates.Transition);
        AddTemplate(root, SearchKey, templates.Search);

        return root.Count == 0
            ? string.Empty
            : root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Returns one role's argument object as compact JSON, or null when absent or not an object.</summary>
    private static string? ReadTemplate(JsonObject root, string roleKey) =>
        root.TryGetPropertyValue(roleKey, out var node) && node is JsonObject argumentObject
            ? argumentObject.ToJsonString()
            : null;

    /// <summary>Adds one role's template back onto the blob, skipping blanks and anything that will not parse.</summary>
    private static void AddTemplate(JsonObject root, string roleKey, string? templateJson)
    {
        if (string.IsNullOrWhiteSpace(templateJson))
            return;

        try
        {
            if (JsonNode.Parse(templateJson) is JsonObject argumentObject)
                root[roleKey] = argumentObject;
        }
        catch (JsonException)
        {
            // A stored template we cannot re-parse is simply not shown; the operator can re-enter it.
        }
    }
}
