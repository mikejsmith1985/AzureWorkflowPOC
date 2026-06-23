// IWorkflowObserver implementation that pushes events to connected browser clients (FR-21.3).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Forwards workflow execution events to the <see cref="WorkflowRunHub"/> so the Execution
/// History page can display a live-updating step timeline. Also notifies the review-queue
/// group on run pause and resume events so the Review Queue page refreshes in real time.
/// Fire-and-forget: never throws.
/// </summary>
public sealed class SignalRWorkflowObserver : IWorkflowObserver
{
    private readonly IHubContext<WorkflowRunHub> _hub;
    private readonly ILogger<SignalRWorkflowObserver> _logger;

    public SignalRWorkflowObserver(
        IHubContext<WorkflowRunHub> hub,
        ILogger<SignalRWorkflowObserver> logger)
    {
        _hub    = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordEventAsync(WorkflowExecutionEvent evt, CancellationToken ct = default)
    {
        try
        {
            // Push per-run event to any subscriber watching this specific run.
            await _hub.Clients
                .Group(WorkflowRunHub.RunGroup(evt.RunId))
                .SendAsync("RunEventReceived", evt, ct);

            // On pause/resume, also notify the Review Queue group so the queue refreshes.
            if (evt.EventType is WorkflowEventType.RunPaused or WorkflowEventType.RunResumed)
            {
                await _hub.Clients
                    .Group(WorkflowRunHub.ReviewQueueGroup)
                    .SendAsync("QueueChanged", evt.RunId, evt.EventType.ToString(), ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalRWorkflowObserver failed for event {EventId} on run {RunId}",
                evt.EventId, evt.RunId);
        }
    }
}
