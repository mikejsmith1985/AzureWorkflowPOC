// Pass-through executor sitting immediately after the HITL RequestPort (spec-021). A RequestPort routes to a
// single successor, so this node receives the resumed state and lets the graph's conditional edges (which work
// off a regular executor) fan out to reply-eval, escalation, or manual-exit.
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>Forwards the resumed run state so the conditional edges after it decide the route.</summary>
[SendsMessage(typeof(DorRunState))]
public sealed class ResumeDispatchExecutor : Executor<DorRunState>
{
    public ResumeDispatchExecutor() : base(MafExecutorIds.DorDispatch) { }

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(DorRunState state, IWorkflowContext context, CancellationToken cancellationToken)
        => await context.SendMessageAsync(state, cancellationToken);
}
