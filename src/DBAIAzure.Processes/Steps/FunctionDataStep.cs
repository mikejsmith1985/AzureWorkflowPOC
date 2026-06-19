// Data source step — reads from or writes to a configured data store between pipeline nodes.
#pragma warning disable SKEXP0080

using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Reads or writes data from a configured data source. For POC, passes data through.
/// In production this step inspects the node's <c>FunctionConfig</c> to determine
/// whether it is performing a read or a write, resolves the appropriate data connector
/// from the kernel's DI container, and executes the operation. Emits
/// <see cref="WorkflowNodeEvents.NodeCompleted"/> once the data operation is complete.
/// </summary>
public sealed class FunctionDataStep : KernelProcessStep
{
    /// <summary>
    /// Executes the configured data read or write operation and signals pipeline completion
    /// for this node. For POC the payload is passed through unchanged; a production
    /// implementation would resolve an <c>IDataConnector</c> from the kernel's DI container,
    /// execute the query or write, and populate <see cref="WorkflowStepData.OutputPayload"/>
    /// with the result.
    /// </summary>
    /// <param name="ctx">Process-step context used to emit lifecycle events.</param>
    /// <param name="stepData">Immutable data envelope carrying the run ID, node ID, and input payload.</param>
    /// <param name="kernel">Semantic Kernel instance — provides access to DI services including data connectors.</param>
    [KernelFunction]
    public async Task ReadWriteAsync(
        KernelProcessStepContext ctx,
        WorkflowStepData stepData,
        Kernel kernel)
    {
        // Pass the payload through unchanged for POC; in production, replace with a
        // real connector call that reads from or writes to the configured data source.
        var completedData = stepData with { OutputPayload = stepData.InputPayload };

        await ctx.EmitEventAsync(new() { Id = WorkflowNodeEvents.NodeCompleted, Data = completedData });
    }
}
