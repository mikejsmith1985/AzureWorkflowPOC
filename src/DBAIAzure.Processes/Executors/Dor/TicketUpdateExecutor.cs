// Applies a resolution to the ticket (spec-021 US2): writes only the whitelisted fields, transitions to the
// ready status, and adds a summary comment. Enforces the AI-editable field whitelist in code (FR-021) and gates
// every write on the dry-run flag (FR-032).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>
/// Writes the resolved field values (filtered to the configured whitelist — never trusting the model),
/// transitions the ticket to the ready status, and posts an internal comment summarizing what changed and which
/// criteria were resolved. Records the outcome as auto-resolved. All writes are skipped in dry-run mode.
/// </summary>
[SendsMessage(typeof(DorRunState))]
public sealed class TicketUpdateExecutor : Executor<DorRunState>
{
    private readonly IWorkTrackerAdapter _adapter;
    private readonly IDorConfigResolver _configResolver;
    private readonly IDorWorkflowInstanceStore _instanceStore;

    public TicketUpdateExecutor(
        IWorkTrackerAdapter adapter, IDorConfigResolver configResolver, IDorWorkflowInstanceStore instanceStore)
        : base(MafExecutorIds.DorUpdate)
    {
        _adapter = adapter;
        _configResolver = configResolver;
        _instanceStore = instanceStore;
    }

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(DorRunState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = await _configResolver.ResolveActiveAsync(cancellationToken);
        var whitelisted = DorFieldWhitelist.Filter(state.ResolvedFieldUpdates, config.Jira.AiEditableFields);

        var updated = state with { State = DorState.Updating, Outcome = DorOutcome.ResolvedAuto };
        await _instanceStore.UpdateAsync(DorInstanceMapper.ToInstance(updated), cancellationToken);

        if (!state.IsDryRun)
        {
            var item = new WorkItemRef(state.TicketKey);
            if (whitelisted.Count > 0)
            {
                var native = whitelisted.ToDictionary(entry => entry.Key, entry => (object?)entry.Value);
                await _adapter.SetFieldsAsync(item, native, cancellationToken);
            }
            await _adapter.TransitionAsync(item, config.Jira.ReadyTransitionId, cancellationToken);
            await _adapter.AppendCommentAsync(item, BuildSummaryComment(state, whitelisted.Keys), cancellationToken);
        }

        await context.SendMessageAsync(updated, cancellationToken);
    }

    private static string BuildSummaryComment(DorRunState state, IEnumerable<string> changedFields)
    {
        var fields = string.Join(", ", changedFields);
        var fieldsText = string.IsNullOrEmpty(fields) ? "no fields" : fields;
        return $"DoR resolved via conversation ({state.PrimaryIterations} exchange(s)). Updated: {fieldsText}. Ticket moved toward ready.";
    }
}
