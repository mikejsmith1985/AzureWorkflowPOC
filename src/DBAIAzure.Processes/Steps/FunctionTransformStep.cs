// Data transformation step — reshapes or filters data between agentic steps.
#pragma warning disable SKEXP0080

using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Data transformation step — reshapes or filters data between agentic steps.
/// Receives the current <see cref="WorkflowStepData"/>, applies a transformation
/// to <see cref="WorkflowStepData.InputPayload"/>, and emits
/// <see cref="WorkflowNodeEvents.NodeCompleted"/> with the transformed output so the
/// downstream router can continue the pipeline.
/// </summary>
public sealed class FunctionTransformStep : KernelProcessStep
{
    /// <summary>
    /// Applies the configured transformation to the incoming step data and signals
    /// that this node is done. For POC, the transformation prepends "Transformed: "
    /// to the input payload; a production implementation would use the node's
    /// <c>FunctionConfig</c> to invoke a real mapping or filtering expression.
    /// </summary>
    /// <param name="ctx">Process-step context used to emit lifecycle events.</param>
    /// <param name="stepData">Immutable data envelope carrying the run ID, node ID, and input payload.</param>
    /// <param name="kernel">Semantic Kernel instance — available for future LLM-assisted transforms.</param>
    [KernelFunction]
    public async Task TransformAsync(
        KernelProcessStepContext ctx,
        WorkflowStepData stepData,
        Kernel kernel)
    {
        var transformedOutput = $"Transformed: {stepData.InputPayload}";

        var completedData = stepData with { OutputPayload = transformedOutput };

        await ctx.EmitEventAsync(new() { Id = WorkflowNodeEvents.NodeCompleted, Data = completedData });
    }
}
