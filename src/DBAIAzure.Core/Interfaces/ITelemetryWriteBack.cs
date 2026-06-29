// Writes a run's aggregated LLM telemetry into the Azure DevOps work item it produced.
namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Inputs for one telemetry write-back: which run's metrics to write, and which work item to write
/// them onto. <paramref name="SpeckitPhase"/> is the optional phase label (Spec/Plan/…) for the
/// Speckit Phase field; null when the run is not a Spec Kit phase run.
/// </summary>
public sealed record TelemetryWriteBackRequest(
    string RunId,
    string WorkItemType,
    int WorkItemId,
    string? SpeckitPhase = null);

/// <summary>
/// Outcome of a telemetry write-back. <see cref="Attempted"/> is false when nothing was written —
/// e.g. no manifest, the work item type has no configured fields, or the run had no fillable metrics.
/// </summary>
public sealed record TelemetryWriteBackResult
{
    /// <summary>True when a work item patch was actually sent to Azure DevOps.</summary>
    public required bool Attempted { get; init; }

    /// <summary>How many telemetry fields were written.</summary>
    public required int FieldsWritten { get; init; }

    /// <summary>Configured fields that were skipped (no manifest target, or no captured value).</summary>
    public IReadOnlyList<string> SkippedFields { get; init; } = [];

    /// <summary>Human-readable explanation, safe to log (no secrets).</summary>
    public string? Message { get; init; }

    /// <summary>A non-attempted result carrying the reason nothing was written.</summary>
    public static TelemetryWriteBackResult Skipped(string message) =>
        new() { Attempted = false, FieldsWritten = 0, Message = message };
}

/// <summary>
/// Writes a run's aggregated LLM telemetry onto the work item the run produced, using the field
/// targets resolved by the ADO telemetry preflight (custom fields in Bootstrap mode, native fallbacks
/// in Adaptive mode). Never throws on a delivery problem — failures are returned as a result so the
/// calling pipeline step stays non-blocking.
/// </summary>
public interface ITelemetryWriteBack
{
    /// <summary>Writes the telemetry for <paramref name="request"/> and reports what happened.</summary>
    Task<TelemetryWriteBackResult> WriteBackAsync(
        TelemetryWriteBackRequest request, CancellationToken cancellationToken = default);
}
