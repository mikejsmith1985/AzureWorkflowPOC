// Drives the Intelligent DoR Validation Workflow for one ticket (spec-021). This increment runs the pass path
// to completion (no human-in-the-loop suspension yet); durable pause/resume + the SLA sweeper arrive with the
// HITL increment. Idempotency (FR-004) is enforced here at run creation.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Extensions.Logging;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Starts a DoR workflow run for a ticket: resolves the active configuration, creates the durable instance
/// (rejecting a duplicate for an already-active ticket), builds the MAF graph, and drives it. A run failure is
/// recorded on the instance as a manual-required outcome rather than thrown to the caller (best-effort trigger).
/// The active work-tracker adapter is supplied at construction so this Processes-layer type holds no Web type.
/// </summary>
public sealed class DorWorkflowOrchestrator
{
    private readonly IDorReviewService _reviewService;
    private readonly IWorkTrackerAdapter _activeAdapter;
    private readonly IDorDocumentSource _documentSource;
    private readonly IDorConfigResolver _configResolver;
    private readonly IMessageDelivery _messageDelivery;
    private readonly IDorWorkflowInstanceStore _instanceStore;
    private readonly ILogger<DorWorkflowOrchestrator> _logger;

    public DorWorkflowOrchestrator(
        IDorReviewService reviewService,
        IWorkTrackerAdapter activeAdapter,
        IDorDocumentSource documentSource,
        IDorConfigResolver configResolver,
        IMessageDelivery messageDelivery,
        IDorWorkflowInstanceStore instanceStore,
        ILogger<DorWorkflowOrchestrator> logger)
    {
        _reviewService = reviewService;
        _activeAdapter = activeAdapter;
        _documentSource = documentSource;
        _configResolver = configResolver;
        _messageDelivery = messageDelivery;
        _instanceStore = instanceStore;
        _logger = logger;
    }

    /// <summary>
    /// Starts (and, this increment, runs to completion) the DoR workflow for <paramref name="ticketKey"/>.
    /// No-ops when the workflow is not configured; discards a duplicate trigger for an already-active ticket.
    /// </summary>
    public async Task StartAsync(string ticketKey, CancellationToken cancellationToken = default)
    {
        var config = await _configResolver.ResolveActiveAsync(cancellationToken);
        if (!config.IsConfigured)
        {
            _logger.LogInformation("DoR workflow is not configured — ignoring trigger for {Ticket}.", ticketKey);
            return;
        }

        var runId = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;
        var created = new DorWorkflowInstance
        {
            RunId = runId,
            TicketKey = ticketKey,
            State = DorState.Created,
            IsDryRun = config.Run.DryRun,
            StartedAt = now,
            UpdatedAt = now,
        };

        if (!await _instanceStore.TryCreateAsync(created, cancellationToken))
        {
            _logger.LogInformation("An active DoR instance already exists for {Ticket} — discarding duplicate trigger.", ticketKey);
            return;
        }

        var seed = new DorRunState
        {
            RunId = runId,
            TicketKey = ticketKey,
            State = DorState.Created,
            IsDryRun = config.Run.DryRun,
        };

        var workflow = MafDorWorkflowFactory.Build(
            _reviewService, _activeAdapter, _documentSource, _configResolver, _messageDelivery, _instanceStore);

        try
        {
            await MafWorkflowExecution.RunAsync<DorRunState, DorRunState>(workflow, seed, runId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DoR workflow run {RunId} for {Ticket} failed.", runId, ticketKey);
            if (await _instanceStore.GetAsync(runId, cancellationToken) is { } instance)
            {
                await _instanceStore.UpdateAsync(
                    instance with
                    {
                        State = DorState.Done,
                        Outcome = DorOutcome.ManualRequired,
                        FailureReason = ex.Message,
                        CompletedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow,
                    },
                    cancellationToken);
            }
        }
    }
}
