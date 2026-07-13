// Shared completion helper for visual node executors (spec-019 US1): forward the run payload to the next
// node, or yield it as the workflow output when the node is terminal — the MAF analogue of the SK
// NodeCompleted event either flowing to a successor or ending the run.
using DBAIAzure.Core.Models;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors;

/// <summary>Routes a completed node's payload onward, or yields it as output when the node is terminal.</summary>
internal static class NodeCompletion
{
    /// <summary>Broadcasts <paramref name="data"/> to successors, or yields it as output if terminal.</summary>
    public static async ValueTask EmitAsync(
        IWorkflowContext context, WorkflowStepData data, bool isTerminal, CancellationToken cancellationToken)
    {
        if (isTerminal)
        {
            await context.YieldOutputAsync(data, cancellationToken);
        }
        else
        {
            await context.SendMessageAsync(data, cancellationToken);
        }
    }
}
