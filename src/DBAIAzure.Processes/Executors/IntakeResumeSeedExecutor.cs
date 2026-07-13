// Migration seed executor (spec-019 T033): forwards a paused ticket straight to the clarification gate so
// a checkpoint is created at the human-in-the-loop suspension WITHOUT re-running the earlier LLM steps.
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// The entry executor of the intake <em>resume</em> workflow used to migrate an SK-paused clarification
/// run onto MAF: it forwards the already-paused ticket (with its clarifying questions) directly to the HITL
/// <see cref="RequestPort"/>, so running this workflow with checkpointing produces a checkpoint at the same
/// suspension point — without re-normalising, re-validating, or re-generating questions. On the human's
/// answer the run continues through the real validation loop, exactly as a fresh not-ready run would.
/// </summary>
[SendsMessage(typeof(TicketState))]
public sealed class IntakeResumeSeedExecutor : Executor<TicketState>
{
    /// <summary>Creates the seed executor.</summary>
    public IntakeResumeSeedExecutor() : base(MafExecutorIds.IntakeResumeSeed) { }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(TicketState ticket, IWorkflowContext context, CancellationToken cancellationToken)
        => await context.SendMessageAsync(ticket, cancellationToken);
}
