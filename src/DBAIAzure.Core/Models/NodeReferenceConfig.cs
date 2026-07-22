// Pure read/write of the "references" array inside a workflow node's FunctionConfig JSON blob,
// preserving every other key so a node's type-specific config (for example a Trigger's
// initialDataDescription) survives an edit to its references.
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DBAIAzure.Core.Models;

/// <summary>
/// Reads and writes the <c>references</c> array that any workflow node can carry inside its
/// <see cref="WorkflowNode.FunctionConfig"/> blob. References live on the node itself — not as a
/// standalone canvas node — so ownership is unambiguous. Both methods are pure and side-effect-free so
/// the split/merge round-trip is unit-testable (&lt;10ms), and they preserve unrelated keys so editing a
/// node's references never clobbers its other configuration.
/// </summary>
public static class NodeReferenceConfig
{
    private const string ReferencesKey = "references";
    private const string TypeKey = "type";
    private const string NameKey = "name";
    private const string ValueKey = "value";

    /// <summary>
    /// Extracts a node's references from its FunctionConfig blob. A null, blank, or malformed blob — or one
    /// without a <c>references</c> array — yields an empty list rather than throwing, because a node simply
    /// has no references until an operator adds one. Entries with no name are skipped as unusable.
    /// </summary>
    public static IReadOnlyList<NodeReference> Read(string? functionConfig)
    {
        if (string.IsNullOrWhiteSpace(functionConfig))
            return Array.Empty<NodeReference>();

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(functionConfig);
        }
        catch (JsonException)
        {
            return Array.Empty<NodeReference>();
        }

        if (root is not JsonObject obj || obj[ReferencesKey] is not JsonArray array)
            return Array.Empty<NodeReference>();

        var references = new List<NodeReference>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonObject entry)
                continue;

            var name = (string?)entry[NameKey];
            if (string.IsNullOrWhiteSpace(name))
                continue; // A reference with no label is not usable — skip it rather than surface junk.

            references.Add(new NodeReference
            {
                Type = ParseType((string?)entry[TypeKey]),
                Name = name,
                Value = (string?)entry[ValueKey] ?? string.Empty,
            });
        }

        return references;
    }

    /// <summary>
    /// Merges the given references into the node's FunctionConfig blob, preserving every other key. An empty
    /// reference list removes the <c>references</c> key entirely so a node with no references keeps a clean
    /// config. A null, blank, or malformed base blob starts from a fresh empty object.
    /// </summary>
    public static string Write(string? functionConfig, IReadOnlyList<NodeReference> references)
    {
        var root = ParseRootOrEmpty(functionConfig);

        if (references.Count == 0)
        {
            root.Remove(ReferencesKey);
            return root.ToJsonString();
        }

        var array = new JsonArray();
        foreach (var reference in references)
        {
            array.Add(new JsonObject
            {
                [TypeKey] = ToWire(reference.Type),
                [NameKey] = reference.Name,
                [ValueKey] = reference.Value,
            });
        }

        root[ReferencesKey] = array;
        return root.ToJsonString();
    }

    /// <summary>Parses the base blob into a mutable object, treating null/blank/malformed input as empty.</summary>
    private static JsonObject ParseRootOrEmpty(string? functionConfig)
    {
        if (string.IsNullOrWhiteSpace(functionConfig))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(functionConfig) as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            return new JsonObject();
        }
    }

    /// <summary>Maps a stored wire string back to the enum, defaulting unknown/blank values to Document.</summary>
    private static NodeReferenceType ParseType(string? wire) => wire?.Trim().ToLowerInvariant() switch
    {
        "url" => NodeReferenceType.Url,
        "dashboard" => NodeReferenceType.Dashboard,
        "binary" => NodeReferenceType.Binary,
        _ => NodeReferenceType.Document,
    };

    /// <summary>Maps the enum to its lowercase wire string used in the JSON blob.</summary>
    private static string ToWire(NodeReferenceType type) => type switch
    {
        NodeReferenceType.Url => "url",
        NodeReferenceType.Dashboard => "dashboard",
        NodeReferenceType.Binary => "binary",
        _ => "document",
    };
}
