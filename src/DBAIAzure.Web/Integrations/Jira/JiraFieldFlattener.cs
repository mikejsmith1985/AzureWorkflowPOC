// Turns raw Jira field JSON into the plain text the DoR review prompt reads. Shared by both Jira transports —
// the direct REST adapter and the MCP client — so a ticket looks identical to the AI whichever path fetched it.
using System.Text;
using System.Text.Json;

namespace DBAIAzure.Web.Integrations.Jira;

/// <summary>
/// Flattens Jira's nested field values (option objects, user objects, arrays, and Atlassian Document Format
/// rich text) into simple strings. Pure and side-effect free so it can be unit tested without a Jira instance.
/// </summary>
public static class JiraFieldFlattener
{
    /// <summary>
    /// Projects a Jira <c>fields</c> object into a flat name→text dictionary. When <paramref name="watchFields"/>
    /// is non-empty only those fields are returned, so the review payload stays stable and lines up with the
    /// configured field labels; otherwise every field Jira returned is included.
    /// </summary>
    public static Dictionary<string, string?> FlattenFields(
        JsonElement fieldsElement, IReadOnlyCollection<string> watchFields)
    {
        var flattened = new Dictionary<string, string?>();
        if (fieldsElement.ValueKind != JsonValueKind.Object)
            return flattened;

        if (watchFields.Count > 0)
        {
            foreach (var name in watchFields)
                if (fieldsElement.TryGetProperty(name, out var value))
                    flattened[name] = FlattenValue(value);
        }
        else
        {
            foreach (var property in fieldsElement.EnumerateObject())
                flattened[property.Name] = FlattenValue(property.Value);
        }

        return flattened;
    }

    /// <summary>Flattens a single Jira field value (string, number, option object, array, or ADF doc) to plain text.</summary>
    public static string? FlattenValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => string.Join(", ",
            value.EnumerateArray().Select(FlattenValue).Where(text => !string.IsNullOrEmpty(text))),
        JsonValueKind.Object => FlattenObject(value),
        _ => value.ToString(),
    };

    /// <summary>Renders an object field: an ADF document becomes its text; an option/user object its display label.</summary>
    private static string? FlattenObject(JsonElement element)
    {
        if (element.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "doc")
            return RenderAtlassianDocument(element).Trim();

        foreach (var labelKey in new[] { "displayName", "name", "value" })
            if (element.TryGetProperty(labelKey, out var label) && label.ValueKind == JsonValueKind.String)
                return label.GetString();

        return element.ToString();
    }

    /// <summary>Walks an Atlassian Document Format node tree collecting its text, paragraph by paragraph.</summary>
    private static string RenderAtlassianDocument(JsonElement node)
    {
        var builder = new StringBuilder();
        if (node.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            builder.Append(text.GetString());

        if (node.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in content.EnumerateArray())
            {
                builder.Append(RenderAtlassianDocument(child));
                if (child.TryGetProperty("type", out var childType) && childType.GetString() == "paragraph")
                    builder.Append('\n');
            }
        }

        return builder.ToString();
    }
}
