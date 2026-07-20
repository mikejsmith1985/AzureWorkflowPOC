// Ends a run with a clean manual handoff (spec-021 US4). Reached when limits are exhausted (iterations or the
// escalation SLA). Posts a final message, adds an internal comment, and records a manual-required outcome —
// deliberately WITHOUT transitioning the ticket, leaving it for a human to action.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>
/// Performs the manual handoff: posts a closing message to the active channel and an internal comment
/// summarizing what was attempted and what remains, applies the manual outcome, and does NOT transition the
/// ticket status (FR-020). All external writes are dry-run gated.
/// </summary>
[SendsMessage(typeof(DorRunState))]
public sealed class ManualExitExecutor : Executor<DorRunState>
{
    private readonly IWorkTrackerAdapter _adapter;
    private readonly IMessageDelivery _messageDelivery;
    private readonly IDorConfigResolver _configResolver;
    private readonly IDorWorkflowInstanceStore _instanceStore;

    public ManualExitExecutor(
        IWorkTrackerAdapter adapter, IMessageDelivery messageDelivery,
        IDorConfigResolver configResolver, IDorWorkflowInstanceStore instanceStore)
        : base(MafExecutorIds.DorManualExit)
    {
        _adapter = adapter;
        _messageDelivery = messageDelivery;
        _configResolver = configResolver;
        _instanceStore = instanceStore;
    }

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(DorRunState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = await _configResolver.ResolveActiveAsync(cancellationToken);

        var next = state with
        {
            State = DorState.ManualExit,
            Outcome = DorOutcome.ManualRequired,
            EscalateRequested = false,
            ManualExitRequested = false,
        };
        await _instanceStore.UpdateAsync(DorInstanceMapper.ToInstance(next), cancellationToken);

        if (!state.IsDryRun)
        {
            var item = new WorkItemRef(state.TicketKey);
            await _adapter.AppendCommentAsync(item, BuildManualComment(state, config), cancellationToken);
            await _messageDelivery.SendAsync(
                $"Closing the DoR conversation for {state.TicketKey} — manual action required ({config.Jira.ManualLabel}).",
                cancellationToken);
            // Status is intentionally NOT transitioned (FR-020).
        }

        await context.SendMessageAsync(next, cancellationToken); // → audit
    }

    private static string BuildManualComment(DorRunState state, Core.Models.DorWorkflow.Config.DorWorkflowConfig config)
    {
        var gaps = state.OutstandingGaps.Count > 0 ? string.Join(", ", state.OutstandingGaps) : "(unspecified)";
        var reason = string.IsNullOrWhiteSpace(state.FailureReason) ? "SLA/iteration limits reached." : state.FailureReason;
        return $"[{config.Jira.ManualLabel}] Automated DoR resolution stopped — {reason} "
             + $"Outstanding: {gaps}. Attempts: {state.PrimaryIterations} primary / {state.EscalationIterations} escalation. "
             + "A human needs to complete the Definition of Ready.";
    }
}
