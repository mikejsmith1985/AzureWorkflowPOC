// Data transformation helper — reshapes data between nodes using the node's realized TransformNodeConfig.
// When the upstream payload is a JSON object, the configured field mappings are applied for real. The
// FunctionTransformExecutor (MAF) calls ApplyMappings directly.
using System.Text.Json;
using System.Text.Json.Nodes;
using DBAIAzure.Core.Models.NodeConfig;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Reshapes the incoming data into the shape the downstream node expects, using the realized
/// <see cref="TransformNodeConfig"/> field mappings (FR-15.4). When the upstream payload is a JSON
/// object the mappings are applied to produce a new object with just the mapped fields; otherwise, and
/// when the node is un-realized, the payload passes through unchanged so existing workflows still run.
/// </summary>
public static class FunctionTransformStep
{
    /// <summary>
    /// Applies the field mappings to <paramref name="input"/>. When the input is a JSON object, returns
    /// a new JSON object carrying each mapping's source value under its target name (missing sources are
    /// skipped). When there are no mappings, or the input is not a JSON object, the input is returned
    /// unchanged. Pure and side-effect free so it can be unit-tested directly.
    /// </summary>
    public static string? ApplyMappings(string? input, IReadOnlyList<FieldMapping>? mappings)
    {
        if (mappings is null || mappings.Count == 0 || string.IsNullOrWhiteSpace(input))
            return input;

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(input);
        }
        catch (JsonException)
        {
            return input;
        }

        if (parsed is not JsonObject sourceObject)
            return input;

        var result = new JsonObject();
        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.FromField) || string.IsNullOrWhiteSpace(mapping.ToField))
                continue;
            if (sourceObject.TryGetPropertyValue(mapping.FromField, out var value))
                result[mapping.ToField] = value?.DeepClone();
        }

        return result.ToJsonString();
    }
}
