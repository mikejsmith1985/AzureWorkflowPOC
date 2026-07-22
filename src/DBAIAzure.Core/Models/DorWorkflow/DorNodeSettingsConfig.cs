// Pure read/write of the "dor" settings object inside a workflow node's FunctionConfig blob, preserving every
// other key (the node's references and any type-specific fields) so the two never clobber each other.
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace DBAIAzure.Core.Models.DorWorkflow;

/// <summary>
/// Reads and writes the <c>dor</c> settings object a workflow node carries inside its
/// <see cref="WorkflowNode.FunctionConfig"/> blob — the node's own slice of the DoR configuration. Mirrors
/// <see cref="NodeReferenceConfig"/>: both live in the same blob under different keys and each preserves the
/// other, so editing a node's settings never drops its references and vice versa. Pure and side-effect-free so
/// the round-trip is unit-testable (&lt;10ms).
/// </summary>
public static class DorNodeSettingsConfig
{
    private const string SettingsKey = "dor";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Extracts the node's DoR settings. A null, blank, or malformed blob — or one carrying no <c>dor</c> object —
    /// yields <see langword="null"/>, meaning "this node holds no DoR settings" rather than throwing.
    /// </summary>
    public static DorNodeSettings? Read(string? functionConfig)
    {
        if (string.IsNullOrWhiteSpace(functionConfig))
            return null;

        try
        {
            if (JsonNode.Parse(functionConfig) is not JsonObject root || root[SettingsKey] is not JsonObject settings)
                return null;

            return settings.Deserialize<DorNodeSettings>(Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Merges the given settings into the node's FunctionConfig blob, preserving every other key. Passing
    /// <see langword="null"/> removes the <c>dor</c> object so a node that is no longer part of the DoR workflow
    /// keeps a clean config. A null, blank, or malformed base blob starts from a fresh empty object.
    /// </summary>
    public static string Write(string? functionConfig, DorNodeSettings? settings)
    {
        var root = ParseRootOrEmpty(functionConfig);

        if (settings is null)
        {
            root.Remove(SettingsKey);
            return root.ToJsonString();
        }

        root[SettingsKey] = JsonSerializer.SerializeToNode(settings, Options);
        return root.ToJsonString();
    }

    /// <summary>Reads just the node's role — the cheap check the builder and assembler use to dispatch.</summary>
    public static DorNodeRole ReadRole(string? functionConfig) => Read(functionConfig)?.Role ?? DorNodeRole.None;

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
}
