// Aggregated LLM telemetry for one workflow/phase run — the values written back to ADO custom fields.
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// One captured LLM call from a run's execution events. Mirrors the subset of
/// <c>WorkflowExecutionEvent</c> that telemetry write-back consumes; kept as its own shape so the
/// aggregation logic is unit-testable without the storage layer. Cache/error fields are optional so
/// existing constructions remain valid.
/// </summary>
public sealed record LlmTelemetrySample(
    DateTimeOffset OccurredAt,
    long? DurationMs,
    string? ModelName,
    int? InputTokens,
    int? OutputTokens,
    int CacheReadTokens = 0,
    int CacheCreationTokens = 0,
    bool IsError = false);

/// <summary>
/// The aggregated telemetry for a single run — token sums (incl. cache), the model used, the number of
/// successful LLM calls, the error count, and elapsed AI time. Derives an estimated cache-hit rate.
/// Only metrics the pipeline actually captures are present; tool-accept rate has no source and is absent.
/// </summary>
public sealed record RunTelemetryAggregate
{
    /// <summary>The run identifier these metrics belong to (used as the AI Session ID).</summary>
    public required string RunId { get; init; }

    /// <summary>The model most recently used in a successful call (null when none recorded).</summary>
    public string? ModelName { get; init; }

    /// <summary>Total fresh (non-cached) prompt tokens across the run's LLM calls.</summary>
    public int InputTokens { get; init; }

    /// <summary>Total completion/output tokens across the run's LLM calls.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Total prompt tokens served from cache (reuse) across the run.</summary>
    public int CacheReadTokens { get; init; }

    /// <summary>Total prompt tokens written to cache across the run.</summary>
    public int CacheCreationTokens { get; init; }

    /// <summary>How many successful LLM calls the run made (errors excluded).</summary>
    public int LlmCallCount { get; init; }

    /// <summary>How many LLM calls failed at the provider.</summary>
    public int ErrorCount { get; init; }

    /// <summary>Total AI processing time in whole seconds (sum of per-call durations).</summary>
    public int DurationSeconds { get; init; }

    /// <summary>True when the run made at least one successful LLM call — token metrics are meaningful.</summary>
    public bool HasLlmActivity => LlmCallCount > 0;

    /// <summary>
    /// Percentage of prompt input served from cache: <c>cacheRead / (cacheRead + input) × 100</c>.
    /// Null when there was no prompt input at all (avoids a misleading 0%).
    /// </summary>
    public double? CacheHitRatePct
    {
        get
        {
            var totalInput = CacheReadTokens + InputTokens;
            if (totalInput <= 0)
                return null;
            return Math.Round((double)CacheReadTokens / totalInput * 100, 1);
        }
    }

    /// <summary>An aggregate for a run that produced no LLM telemetry (only the run id is known).</summary>
    public static RunTelemetryAggregate Empty(string runId) => new() { RunId = runId };

    /// <summary>
    /// Folds the run's LLM samples into a single aggregate. Token sums and the model come from the
    /// successful calls; error samples (zero tokens) only contribute to <see cref="ErrorCount"/>.
    /// </summary>
    public static RunTelemetryAggregate FromSamples(string runId, IReadOnlyList<LlmTelemetrySample> samples)
    {
        if (samples.Count == 0)
            return Empty(runId);

        var successful = samples.Where(sample => !sample.IsError).ToList();
        var totalDurationMs = samples.Sum(sample => sample.DurationMs ?? 0);
        var latestModel = successful
            .Where(sample => !string.IsNullOrWhiteSpace(sample.ModelName))
            .OrderByDescending(sample => sample.OccurredAt)
            .Select(sample => sample.ModelName)
            .FirstOrDefault();

        return new RunTelemetryAggregate
        {
            RunId = runId,
            ModelName = latestModel,
            InputTokens = successful.Sum(sample => sample.InputTokens ?? 0),
            OutputTokens = successful.Sum(sample => sample.OutputTokens ?? 0),
            CacheReadTokens = successful.Sum(sample => sample.CacheReadTokens),
            CacheCreationTokens = successful.Sum(sample => sample.CacheCreationTokens),
            LlmCallCount = successful.Count,
            ErrorCount = samples.Count - successful.Count,
            DurationSeconds = (int)Math.Round(totalDurationMs / 1000.0),
        };
    }
}
