// Posts the gap message that opens (or continues) the human conversation and starts the SLA clock, then forwards
// the run to the human-in-the-loop gate (spec-021 US2). Reached both from the initial review (fail) and from a
// partial reply (follow-up).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>
/// Posts a message to the conversation channel — the initial gap list, or the focused follow-up carried from a
/// partial reply — sets the run to <see cref="DorState.AwaitingResponse"/>, ensures a thread + SLA clock start,
/// and forwards to the HITL gate. The write is dry-run gated.
/// </summary>
[SendsMessage(typeof(DorRunState))]
public sealed class GapOutreachExecutor : Executor<DorRunState>
{
    private readonly IMessageDelivery _messageDelivery;
    private readonly IDorConfigResolver _configResolver;
    private readonly IDorWorkflowInstanceStore _instanceStore;

    public GapOutreachExecutor(
        IMessageDelivery messageDelivery, IDorConfigResolver configResolver, IDorWorkflowInstanceStore instanceStore)
        : base(MafExecutorIds.DorOutreach)
    {
        _messageDelivery = messageDelivery;
        _configResolver = configResolver;
        _instanceStore = instanceStore;
    }

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(DorRunState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        _ = await _configResolver.ResolveActiveAsync(cancellationToken); // resolved per run; channel targeting refined in the SLA increment

        var message = !string.IsNullOrWhiteSpace(state.PendingOutreachMessage)
            ? state.PendingOutreachMessage!
            : BuildGapMessage(state);

        var next = state with
        {
            State = DorState.AwaitingResponse,
            ThreadRef = string.IsNullOrEmpty(state.ThreadRef) ? state.RunId : state.ThreadRef,
            SlaClockStartedAt = state.SlaClockStartedAt ?? DateTimeOffset.UtcNow,
            PendingOutreachMessage = null,
        };

        if (!state.IsDryRun)
            await _messageDelivery.SendAsync(message, cancellationToken);

        await _instanceStore.UpdateAsync(DorInstanceMapper.ToInstance(next), cancellationToken);
        await context.SendMessageAsync(next, cancellationToken);
    }

    private static string BuildGapMessage(DorRunState state)
    {
        var gaps = state.OutstandingGaps.Count > 0
            ? "\n- " + string.Join("\n- ", state.OutstandingGaps)
            : " (see the ticket)";
        return $"Ticket {state.TicketKey} isn't ready yet. Missing/insufficient:{gaps}\n{state.TicketUrl}";
    }
}
