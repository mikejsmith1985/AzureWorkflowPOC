// IWorkflowObserver that emits custom events to Azure Monitor / Application Insights (FR-21.3).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Forwards workflow execution events to Azure Monitor as custom telemetry events.
/// Each event is emitted as a named <c>TrackEvent</c> call with properties for the run,
/// node, event type, and (for LLM events) token counts for cost visibility.
/// Falls back to no-op when Application Insights is not configured — the SDK's
/// <see cref="TelemetryClient"/> is tolerant of a missing instrumentation key.
/// Never throws.
/// </summary>
public sealed class AzureMonitorWorkflowObserver : IWorkflowObserver
{
    private readonly TelemetryClient _telemetry;
    private readonly ILogger<AzureMonitorWorkflowObserver> _logger;

    public AzureMonitorWorkflowObserver(
        TelemetryClient telemetry,
        ILogger<AzureMonitorWorkflowObserver> logger)
    {
        _telemetry = telemetry;
        _logger    = logger;
    }

    /// <inheritdoc />
    public Task RecordEventAsync(WorkflowExecutionEvent evt, CancellationToken ct = default)
    {
        try
        {
            var eventName = $"Workflow.{evt.EventType}";
            var properties = new Dictionary<string, string>
            {
                ["runId"]      = evt.RunId,
                ["eventType"]  = evt.EventType.ToString(),
                ["occurredAt"] = evt.OccurredAt.ToString("O"),
            };

            if (evt.NodeId    is not null) properties["nodeId"]    = evt.NodeId;
            if (evt.NodeLabel is not null) properties["nodeLabel"]  = evt.NodeLabel;
            if (evt.Outcome   is not null) properties["outcome"]   = evt.Outcome;

            var metrics = new Dictionary<string, double>();
            if (evt.DurationMs.HasValue)     metrics["durationMs"]      = evt.DurationMs.Value;
            if (evt.LlmInputTokens.HasValue) metrics["llmInputTokens"]  = evt.LlmInputTokens.Value;
            if (evt.LlmOutputTokens.HasValue) metrics["llmOutputTokens"] = evt.LlmOutputTokens.Value;

            _telemetry.TrackEvent(eventName, properties, metrics);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AzureMonitorWorkflowObserver failed for event {EventId}", evt.EventId);
        }

        return Task.CompletedTask;
    }
}
