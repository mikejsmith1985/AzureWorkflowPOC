// Escalates a breached conversation to the escalation tier (spec-021 US3). Reached when the SLA sweeper answers
// the human gate with an escalation request. Posts a context summary to the escalation channel, resets the
// iteration budget, starts a fresh SLA clock, and returns to the gate to await an escalation-tier reply.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>
/// Moves a run to the escalation tier: posts a summary to the escalation channel, sets
/// <see cref="DorState.Escalated"/> with a fresh SLA deadline and a reset escalation-iteration budget, then
/// suspends again for a human reply in the escalation channel. The message is dry-run gated.
/// </summary>
[SendsMessage(typeof(DorRunState))]
public sealed class EscalationExecutor : Executor<DorRunState>
{
    private readonly IMessageDelivery _messageDelivery;
    private readonly IDorConfigResolver _configResolver;
    private readonly IDorWorkflowInstanceStore _instanceStore;

    public EscalationExecutor(
        IMessageDelivery messageDelivery, IDorConfigResolver configResolver, IDorWorkflowInstanceStore instanceStore)
        : base(MafExecutorIds.DorEscalate)
    {
        _messageDelivery = messageDelivery;
        _configResolver = configResolver;
        _instanceStore = instanceStore;
    }

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(DorRunState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = await _configResolver.ResolveActiveAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var deadline = BusinessHoursSlaCalculator.ComputeDeadline(now, config.Sla.EscalationSlaHours, config.Sla);

        var next = state with
        {
            State = DorState.Escalated,
            SlaTier = SlaTier.Escalation,
            EscalationIterations = 0,               // the escalation loop has its own budget (FR-017)
            SlaClockStartedAt = now,
            SlaDeadlineAt = deadline,
            ActiveChannelId = config.Comms.Escalation.ChannelId,
            EscalateRequested = false,
            PendingOutreachMessage = null,
        };

        if (!state.IsDryRun)
            await _messageDelivery.SendAsync(BuildEscalationSummary(state), cancellationToken);

        await _instanceStore.UpdateAsync(DorInstanceMapper.ToInstance(next), cancellationToken);
        await context.SendMessageAsync(next, cancellationToken); // back to the HITL gate at the escalation tier
    }

    private static string BuildEscalationSummary(DorRunState state)
    {
        var gaps = state.OutstandingGaps.Count > 0 ? string.Join(", ", state.OutstandingGaps) : "(see the ticket)";
        return $"Escalation: {state.TicketKey} has not met the Definition of Ready within its SLA. "
             + $"Outstanding: {gaps}. Please help resolve it. {state.TicketUrl}";
    }
}
