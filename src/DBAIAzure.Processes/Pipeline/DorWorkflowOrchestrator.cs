// Drives the Intelligent DoR Validation Workflow for a ticket (spec-021). Launches the MAF run on a background
// task and drives it across human-in-the-loop suspensions: at each gate it persists AwaitingResponse and awaits
// the human reply (supplied out-of-band via SubmitReply), then resumes. Idempotency (FR-004) is enforced at run
// creation. Durable checkpointing is wired so a paused run can be resumed after a restart (rehydration service
// added alongside). The active work-tracker adapter is supplied at construction so this Processes-layer type
// holds no Web type.
using System.Collections.Concurrent;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Owns the lifecycle of DoR workflow runs. <see cref="StartAsync"/> creates the durable instance (rejecting a
/// duplicate for an already-active ticket), starts the MAF session, and launches the background drive loop;
/// <see cref="SubmitReply"/> supplies a human reply to a suspended run.
/// </summary>
public sealed class DorWorkflowOrchestrator
{
    private readonly IDorReviewService _reviewService;
    private readonly IDorConversationService _conversationService;
    private readonly IWorkTrackerAdapter _activeAdapter;
    private readonly IDorDocumentSource _documentSource;
    private readonly IDorConfigResolver _configResolver;
    private readonly IMessageDelivery _messageDelivery;
    private readonly IDorWorkflowInstanceStore _instanceStore;
    private readonly CheckpointManager? _checkpointManager;
    private readonly ILogger<DorWorkflowOrchestrator> _logger;

    private readonly ConcurrentDictionary<string, DorWorkflowRun> _runs = new();

    public DorWorkflowOrchestrator(
        IDorReviewService reviewService,
        IDorConversationService conversationService,
        IWorkTrackerAdapter activeAdapter,
        IDorDocumentSource documentSource,
        IDorConfigResolver configResolver,
        IMessageDelivery messageDelivery,
        IDorWorkflowInstanceStore instanceStore,
        ILogger<DorWorkflowOrchestrator> logger,
        CheckpointManager? checkpointManager = null)
    {
        _reviewService = reviewService;
        _conversationService = conversationService;
        _activeAdapter = activeAdapter;
        _documentSource = documentSource;
        _configResolver = configResolver;
        _messageDelivery = messageDelivery;
        _instanceStore = instanceStore;
        _logger = logger;
        _checkpointManager = checkpointManager;
    }

    /// <summary>The live run handle, or null if unknown (completed runs are removed).</summary>
    public DorWorkflowRun? GetRun(string runId) => _runs.GetValueOrDefault(runId);

    /// <summary>Supplies a human reply to a suspended run's conversation.</summary>
    public void SubmitReply(string runId, string reply)
    {
        if (_runs.TryGetValue(runId, out var run))
            run.ProvideReply(reply);
    }

    /// <summary>
    /// Starts the DoR workflow for <paramref name="ticketKey"/> on a background task and returns its run handle
    /// (null when unconfigured or a duplicate). The run drives itself to completion, suspending at each human
    /// gate until a reply is submitted. Callers may await <see cref="DorWorkflowRun.Completion"/>.
    /// </summary>
    public async Task<DorWorkflowRun?> StartAsync(string ticketKey, CancellationToken cancellationToken = default)
    {
        var config = await _configResolver.ResolveActiveAsync(cancellationToken);
        if (!config.IsConfigured)
        {
            _logger.LogInformation("DoR workflow is not configured — ignoring trigger for {Ticket}.", ticketKey);
            return null;
        }

        var runId = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;
        var created = new DorWorkflowInstance
        {
            RunId = runId, TicketKey = ticketKey, State = DorState.Created,
            IsDryRun = config.Run.DryRun, StartedAt = now, UpdatedAt = now,
        };

        if (!await _instanceStore.TryCreateAsync(created, cancellationToken))
        {
            _logger.LogInformation("An active DoR instance already exists for {Ticket} — discarding duplicate trigger.", ticketKey);
            return null;
        }

        var seed = new DorRunState { RunId = runId, TicketKey = ticketKey, State = DorState.Created, IsDryRun = config.Run.DryRun };
        var workflow = MafDorWorkflowFactory.Build(
            _reviewService, _conversationService, _activeAdapter, _documentSource, _configResolver, _messageDelivery, _instanceStore);

        var session = await MafWorkflowSession<DorRunState>.StartAsync(workflow, seed, runId, _checkpointManager, cancellationToken);
        var run = new DorWorkflowRun(runId);
        _runs[runId] = run;

        _ = Task.Run(() => DriveLoopAsync(run, session, seed), cancellationToken);
        return run;
    }

    /// <summary>
    /// Drives a run across its human-in-the-loop suspensions until it completes. At each suspension the paused
    /// state is recovered from the request, AwaitingResponse is already persisted by the outreach step, and the
    /// loop awaits the human reply before resuming with it.
    /// </summary>
    private async Task DriveLoopAsync(DorWorkflowRun run, MafWorkflowSession<DorRunState> session, DorRunState seed)
    {
        try
        {
            while (true)
            {
                var segment = await session.DriveAsync(CancellationToken.None);

                if (!segment.Suspended)
                {
                    run.State = DorState.Done;
                    return;
                }

                var paused = ExtractPaused(segment.PendingRequest!, seed);
                run.State = DorState.AwaitingResponse;
                run.ArmReply();
                run.SignalSuspended();

                var reply = await run.WaitForReplyAsync();
                var responded = paused with { HumanReply = reply };
                await session.RespondAsync(segment.PendingRequest!.Request, responded, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DoR workflow run {RunId} failed.", run.RunId);
            if (await _instanceStore.GetAsync(run.RunId, CancellationToken.None) is { } instance)
            {
                await _instanceStore.UpdateAsync(
                    instance with
                    {
                        State = DorState.Done, Outcome = DorOutcome.ManualRequired,
                        FailureReason = ex.Message, CompletedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                    },
                    CancellationToken.None);
            }
            run.State = DorState.Done;
        }
        finally
        {
            run.Complete();
            _runs.TryRemove(run.RunId, out _);
        }
    }

    /// <summary>Recovers the paused run state from the pending request, falling back to the seed.</summary>
    private static DorRunState ExtractPaused(RequestInfoEvent request, DorRunState fallback)
        => request.Request.TryGetDataAs<DorRunState>(out var state) && state is not null ? state : fallback;
}
