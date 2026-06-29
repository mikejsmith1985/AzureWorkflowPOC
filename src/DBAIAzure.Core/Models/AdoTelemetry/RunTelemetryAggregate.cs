// Aggregated LLM telemetry for one workflow/phase run — the values written back to ADO custom fields.
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// One captured LLM call from a run's execution events. Mirrors the subset of
/// <c>WorkflowExecutionEvent</c> that telemetry write-back consumes; kept as its own shape so the
/// aggregation logic is unit-testable without the storage layer.
/// </summary>
public sealed record LlmTelemetrySample(
    DateTimeOffset OccurredAt,
    long? DurationMs,
    string? ModelName,
    int? InputTokens,
    int? OutputTokens);

/// <summary>
/// The aggregated telemetry for a single run — sums of tokens, the model used, the number of LLM
/// calls, and the elapsed AI time. Only the metrics the pipeline actually captures today are present;
/// cache tokens, tool-accept rate, API errors, and cache-hit rate have no capture source yet and are
/// intentionally absent rather than fabricated.
/// </summary>
public sealed record RunTelemetryAggregate
{
    /// <summary>The run identifier these metrics belong to (used as the AI Session ID).</summary>
    public required string RunId { get; init; }

    /// <summary>The model most recently used in the run (null when no model was recorded).</summary>
    public string? ModelName { get; init; }

    /// <summary>Total prompt/input tokens summed across the run's LLM calls.</summary>
    public int InputTokens { get; init; }

    /// <summary>Total completion/output tokens summed across the run's LLM calls.</summary>
    public int OutputTokens { get; init; }

    /// <summary>How many LLM calls (function invocations with usage) the run made.</summary>
    public int LlmCallCount { get; init; }

    /// <summary>Total AI processing time in whole seconds (sum of per-call durations).</summary>
    public int DurationSeconds { get; init; }

    /// <summary>True when the run made at least one LLM call — i.e. token metrics are meaningful.</summary>
    public bool HasLlmActivity => LlmCallCount > 0;

    /// <summary>An aggregate for a run that produced no LLM telemetry (only the run id is known).</summary>
    public static RunTelemetryAggregate Empty(string runId) => new() { RunId = runId };

    /// <summary>
    /// Folds the run's LLM samples into a single aggregate: sums tokens, counts calls, totals duration,
    /// and picks the most recently used model. A run with no samples yields <see cref="Empty"/>.
    /// </summary>
    public static RunTelemetryAggregate FromSamples(string runId, IReadOnlyList<LlmTelemetrySample> samples)
    {
        if (samples.Count == 0)
            return Empty(runId);

        var totalDurationMs = samples.Sum(sample => sample.DurationMs ?? 0);
        var latestModel = samples
            .Where(sample => !string.IsNullOrWhiteSpace(sample.ModelName))
            .OrderByDescending(sample => sample.OccurredAt)
            .Select(sample => sample.ModelName)
            .FirstOrDefault();

        return new RunTelemetryAggregate
        {
            RunId = runId,
            ModelName = latestModel,
            InputTokens = samples.Sum(sample => sample.InputTokens ?? 0),
            OutputTokens = samples.Sum(sample => sample.OutputTokens ?? 0),
            LlmCallCount = samples.Count,
            DurationSeconds = (int)Math.Round(totalDurationMs / 1000.0),
        };
    }
}
