// The pass-path executor (spec-021): transitions a ready ticket to the configured status and optionally posts a
// success notification. Every external write is gated by the run's dry-run flag (FR-032).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>
/// Advances a ticket that passed the DoR: transitions it to the configured ready status via the work-tracker
/// adapter and (when enabled) posts a success message. In dry-run mode it performs neither write — the intended
/// action is implied by the persisted state (a "would-do" audit event is added in the observability increment).
/// </summary>
[SendsMessage(typeof(DorRunState))]
public sealed class PassTransitionExecutor : Executor<DorRunState>
{
    private readonly IWorkTrackerAdapter _adapter;
    private readonly IMessageDelivery _messageDelivery;
    private readonly IDorConfigResolver _configResolver;
    private readonly IDorWorkflowInstanceStore _instanceStore;

    public PassTransitionExecutor(
        IWorkTrackerAdapter adapter, IMessageDelivery messageDelivery,
        IDorConfigResolver configResolver, IDorWorkflowInstanceStore instanceStore)
        : base(MafExecutorIds.DorPass)
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
        var updating = state with { State = DorState.Updating, Outcome = DorOutcome.Passed };
        await _instanceStore.UpdateAsync(DorInstanceMapper.ToInstance(updating), cancellationToken);

        if (!state.IsDryRun)
        {
            await _adapter.TransitionAsync(new WorkItemRef(state.TicketKey), config.Jira.ReadyTransitionId, cancellationToken);
            if (config.Comms.Success.Enabled)
            {
                await _messageDelivery.SendAsync(
                    $"{state.TicketKey} passed the Definition of Ready and was moved to '{config.Jira.ReadyStatus}'. {state.TicketUrl}",
                    cancellationToken);
            }
        }

        await context.SendMessageAsync(updating, cancellationToken);
    }
}
