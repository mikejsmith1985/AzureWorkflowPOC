// MAF executor for a visual FunctionTransform node (spec-019 T018) — the GA replacement for the SK
// FunctionTransformStep. Reuses the same pure field-mapping logic (parity — FR-015).
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Processes.Steps;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Reshapes the incoming payload using the node's realized <see cref="TransformNodeConfig"/> field
/// mappings (or passes it through when un-realized / not a JSON object), then forwards it — or yields it
/// when terminal. Reuses <see cref="FunctionTransformStep.ApplyMappings"/> so the transform is identical.
/// </summary>
[SendsMessage(typeof(WorkflowStepData))]
[YieldsOutput(typeof(WorkflowStepData))]
public sealed class FunctionTransformExecutor : Executor<WorkflowStepData>
{
    private readonly NodeRuntimeConfig _config;
    private readonly bool _isTerminal;

    /// <summary>Creates the transform executor for one canvas node.</summary>
    public FunctionTransformExecutor(string nodeId, NodeRuntimeConfig config, bool isTerminal)
        : base(nodeId)
    {
        _config = config;
        _isTerminal = isTerminal;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(WorkflowStepData stepData, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var transform = NodeConfigSerializer.ReadConfig<TransformNodeConfig>(_config.FunctionConfig);
        var output = FunctionTransformStep.ApplyMappings(stepData.InputPayload, transform?.FieldMappings);

        await NodeCompletion.EmitAsync(context, stepData with { OutputPayload = output }, _isTerminal, cancellationToken);
    }
}
