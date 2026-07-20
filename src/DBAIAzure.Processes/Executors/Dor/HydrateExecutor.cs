// The DoR workflow's first executor (spec-021): reads the ticket's watched fields and loads the DoR document
// into the review payload, then forwards to the review step.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>
/// Hydrates a run: reads the ticket via the active work-tracker adapter (its watched fields normalized to text)
/// and loads the current DoR document, producing the payload the review step evaluates. A DoR document that
/// cannot be loaded surfaces as an executor failure so the run is not reviewed against an empty DoR.
/// </summary>
[SendsMessage(typeof(DorRunState))]
public sealed class HydrateExecutor : Executor<DorRunState>
{
    private readonly IWorkTrackerAdapter _adapter;
    private readonly IDorDocumentSource _documentSource;
    private readonly IDorConfigResolver _configResolver;
    private readonly IDorWorkflowInstanceStore _instanceStore;

    public HydrateExecutor(
        IWorkTrackerAdapter adapter, IDorDocumentSource documentSource,
        IDorConfigResolver configResolver, IDorWorkflowInstanceStore instanceStore)
        : base(MafExecutorIds.DorHydrate)
    {
        _adapter = adapter;
        _documentSource = documentSource;
        _configResolver = configResolver;
        _instanceStore = instanceStore;
    }

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(DorRunState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = await _configResolver.ResolveActiveAsync(cancellationToken);
        var read = await _adapter.ReadWorkItemAsync(new WorkItemRef(state.TicketKey), config.Jira.WatchFields, cancellationToken);
        var document = await _documentSource.LoadAsync(cancellationToken); // throws DorDocumentUnavailableException when no DoR

        var next = state with
        {
            State = DorState.Reviewing,
            Fields = read.Fields,
            TicketUrl = read.Url,
            DorDocumentText = document.Text,
            DorDocumentVersion = document.Version,
        };

        await _instanceStore.UpdateAsync(DorInstanceMapper.ToInstance(next), cancellationToken);
        await context.SendMessageAsync(next, cancellationToken);
    }
}
