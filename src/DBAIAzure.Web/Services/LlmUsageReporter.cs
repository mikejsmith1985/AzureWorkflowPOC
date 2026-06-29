// Records each LLM call's usage as a run-correlated execution event (the single capture point for both
// the workflow-runner and phase-handler paths). Never throws — telemetry must not disrupt a call.
using DBAIAzure.Core.Diagnostics;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;

namespace DBAIAzure.Web.Services;

/// <summary>
/// <see cref="ILlmUsageReporter"/> that turns a connector-reported <see cref="LlmUsage"/> into a
/// <see cref="WorkflowExecutionEvent"/> persisted via the registered <see cref="IWorkflowObserver"/>s,
/// tagged with the ambient <see cref="LlmRunContext.CurrentRunId"/>. Writes are awaited synchronously so
/// the event is durable before the run proceeds to telemetry write-back (no read-before-write race);
/// every failure is logged and swallowed so a telemetry problem never breaks the LLM call (FR-010).
/// </summary>
public sealed class LlmUsageReporter : ILlmUsageReporter
{
    private readonly IEnumerable<IWorkflowObserver> _observers;
    private readonly ILogger<LlmUsageReporter> _logger;

    public LlmUsageReporter(IEnumerable<IWorkflowObserver> observers, ILogger<LlmUsageReporter> logger)
    {
        _observers = observers;
        _logger = logger;
    }

    /// <inheritdoc/>
    public void Report(LlmUsage usage)
    {
        try
        {
            var executionEvent = BuildEvent(usage);
            foreach (var observer in _observers)
                RecordSafely(observer, executionEvent);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LlmUsageReporter could not record an LLM usage event.");
        }
    }

    // A failed call carries no token data — record the model + error outcome only so it is counted.
    private static WorkflowExecutionEvent BuildEvent(LlmUsage usage) => new(
        EventId:                Guid.NewGuid(),
        RunId:                  LlmRunContext.CurrentRunId.Value ?? "unknown",
        NodeId:                 null,
        NodeLabel:              null,
        EventType:              WorkflowEventType.LlmCallCompleted,
        OccurredAt:             DateTimeOffset.UtcNow,
        DurationMs:             usage.DurationMs,
        Outcome:                usage.IsError ? "error" : "success",
        LlmModelName:           usage.ModelName,
        LlmInputTokens:         usage.IsError ? null : usage.InputTokens,
        LlmOutputTokens:        usage.IsError ? null : usage.OutputTokens,
        LlmCacheReadTokens:     usage.IsError ? null : usage.CacheReadTokens,
        LlmCacheCreationTokens: usage.IsError ? null : usage.CacheCreationTokens);

    private void RecordSafely(IWorkflowObserver observer, WorkflowExecutionEvent executionEvent)
    {
        try
        {
            // Sync-over-async is safe here: server context (no SynchronizationContext) and a fast local
            // write. Blocking guarantees durability before write-back reads the run's events.
            observer.RecordEventAsync(executionEvent).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Observer {Observer} failed to record an LLM usage event.",
                observer.GetType().Name);
        }
    }
}
