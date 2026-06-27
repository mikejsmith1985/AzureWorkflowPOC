// Data transformation step — reshapes data between steps using the node's realized TransformNodeConfig.
// When the upstream payload is a JSON object, the configured field mappings are applied for real.
#pragma warning disable SKEXP0080

using System.Text.Json;
using System.Text.Json.Nodes;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes.Pipeline;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Reshapes the incoming data into the shape the downstream step expects, using the realized
/// <see cref="TransformNodeConfig"/> field mappings (FR-15.4). When the upstream payload is a JSON
/// object the mappings are applied to produce a new object with just the mapped fields; otherwise, and
/// when the node is un-realized, the payload passes through unchanged so existing workflows still run.
/// Per-node config is injected as Semantic Kernel step state by <see cref="WorkflowRuntimeBuilder"/>.
/// </summary>
public sealed class FunctionTransformStep : KernelProcessStep<NodeRuntimeConfig>
{
    private NodeRuntimeConfig _config = new();

    /// <summary>Captures the per-node configuration injected at build time.</summary>
    public override ValueTask ActivateAsync(KernelProcessStepState<NodeRuntimeConfig> state)
    {
        _config = state.State ?? new NodeRuntimeConfig();
        return ValueTask.CompletedTask;
    }

    /// <summary>Applies the realized field mappings to the step data and signals node completion.</summary>
    /// <param name="ctx">Process-step context used to emit lifecycle events.</param>
    /// <param name="stepData">Run payload carrying the input forwarded from the preceding step.</param>
    /// <param name="kernel">Kernel — available for future LLM-assisted transforms.</param>
    [KernelFunction]
    public async Task TransformAsync(
        KernelProcessStepContext ctx,
        WorkflowStepData stepData,
        Kernel kernel)
    {
        var transform = NodeConfigSerializer.ReadConfig<TransformNodeConfig>(_config.FunctionConfig);
        var output    = ApplyMappings(stepData.InputPayload, transform?.FieldMappings);

        await ctx.EmitEventAsync(new()
        {
            Id   = WorkflowNodeEvents.NodeCompleted,
            Data = stepData with { OutputPayload = output },
        });
    }

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
