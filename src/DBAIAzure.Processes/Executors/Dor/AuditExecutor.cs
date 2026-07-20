// The terminal DoR executor (spec-021): records the final outcome and ends the run. The persisted instance row
// is the durable, queryable audit record from which the operational metrics are derived (FR-023/FR-024).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>
/// Finalizes a run: sets the terminal state and outcome, persists the instance, and yields it as the workflow
/// output. A run that reached here without a resolved outcome and did not pass the DoR is recorded as requiring
/// manual attention (the conversational resolution path is added in the HITL increment).
/// </summary>
[YieldsOutput(typeof(DorRunState))]
public sealed class AuditExecutor : Executor<DorRunState>
{
    private readonly IDorWorkflowInstanceStore _instanceStore;

    public AuditExecutor(IDorWorkflowInstanceStore instanceStore)
        : base(MafExecutorIds.DorAudit)
        => _instanceStore = instanceStore;

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(DorRunState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var outcome = state.Outcome ?? (state.ReviewPassed ? DorOutcome.Passed : DorOutcome.ManualRequired);
        var done = state with { State = DorState.Done, Outcome = outcome };

        await _instanceStore.UpdateAsync(DorInstanceMapper.ToInstance(done), cancellationToken);
        await context.YieldOutputAsync(done, cancellationToken);
    }
}
