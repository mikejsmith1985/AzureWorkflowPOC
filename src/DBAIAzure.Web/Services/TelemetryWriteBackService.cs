// Writes a run's aggregated LLM telemetry onto the ADO work item it produced, honouring the preflight
// manifest's field targets (custom fields in Bootstrap mode, native fallbacks in Adaptive mode).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Web.Integrations.AzureDevOps;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Orchestrates ADO telemetry write-back: it pulls the run's aggregated metrics, looks up the field
/// targets the preflight resolved, fills only the fields that have a real captured value (never
/// fabricating absent metrics), and patches the work item. Fields whose telemetry has no capture
/// source today (cache tokens, tool-accept rate, API errors, cache-hit rate) are simply omitted.
/// Delivery problems are returned, never thrown, so the calling pipeline step stays non-blocking.
/// </summary>
public sealed class TelemetryWriteBackService : ITelemetryWriteBack
{
    private const string TagsFieldReference = "System.Tags";
    private const string CustomFieldPrefix = "Custom.";

    private readonly IRunTelemetrySource _telemetrySource;
    private readonly IBoardsClient _boardsClient;
    private readonly IAdoTelemetryManifestReader _manifestReader;
    private readonly ILogger<TelemetryWriteBackService> _logger;

    public TelemetryWriteBackService(
        IRunTelemetrySource telemetrySource,
        IBoardsClient boardsClient,
        IAdoTelemetryManifestReader manifestReader,
        ILogger<TelemetryWriteBackService> logger)
    {
        _telemetrySource = telemetrySource;
        _boardsClient = boardsClient;
        _manifestReader = manifestReader;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TelemetryWriteBackResult> WriteBackAsync(
        TelemetryWriteBackRequest request, CancellationToken cancellationToken = default)
    {
        var targets = await _manifestReader.ReadAsync(cancellationToken);
        if (targets is null)
            return TelemetryWriteBackResult.Skipped("No ADO telemetry manifest — run the field preflight first.");

        // The manifest config defines which fields exist per work item type; only matching types get telemetry.
        var config = await AdoTelemetryPreflightService.LoadDefaultConfigAsync(cancellationToken);
        var workItemTypeKey = ResolveWorkItemTypeKey(request.WorkItemType, config);
        if (workItemTypeKey is null)
            return TelemetryWriteBackResult.Skipped(
                $"No telemetry fields are configured for work item type '{request.WorkItemType}'.");

        var aggregate = await _telemetrySource.GetAggregateAsync(request.RunId, cancellationToken);
        var fieldValues = BuildFieldValues(request, aggregate);

        var (patch, written, skipped) = ResolvePatch(config.WorkItemTypes[workItemTypeKey].Fields, fieldValues, targets);

        if (patch.Count == 0)
            return new TelemetryWriteBackResult
            {
                Attempted = false,
                FieldsWritten = 0,
                SkippedFields = skipped,
                Message = "No telemetry values were available to write for this run.",
            };

        return await PatchAsync(request.WorkItemId, patch, written, skipped, targets.Mode, cancellationToken);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds the field-reference → value map for the metrics the pipeline actually captures: always
    /// the session id; the model, tokens, call count, duration, and estimated cost when LLM activity
    /// occurred; and the Spec Kit phase when supplied. Uncaptured metrics are intentionally absent.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> BuildFieldValues(
        TelemetryWriteBackRequest request, RunTelemetryAggregate aggregate)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Custom.AISessionID"] = request.RunId,
        };

        if (!string.IsNullOrWhiteSpace(aggregate.ModelName))
            values["Custom.AIModelUsed"] = aggregate.ModelName;

        if (!string.IsNullOrWhiteSpace(request.SpeckitPhase))
            values["Custom.SpeckitPhase"] = request.SpeckitPhase;

        // Errors can occur even when no call succeeded, so record them independently of LLM activity.
        if (aggregate.ErrorCount > 0)
            values["Custom.AIAPIErrors"] = aggregate.ErrorCount;

        if (aggregate.HasLlmActivity)
        {
            values["Custom.AIInputTokens"] = aggregate.InputTokens;
            values["Custom.AIOutputTokens"] = aggregate.OutputTokens;
            values["Custom.AIToolCalls"] = aggregate.LlmCallCount;

            // Surface cache metrics only when caching actually occurred (SC-003) — avoids writing a
            // misleading 0% hit rate on every non-cache run.
            if (aggregate.CacheReadTokens > 0)
            {
                values["Custom.AICacheTokens"] = aggregate.CacheReadTokens;
                if (aggregate.CacheHitRatePct is { } cacheHitRate)
                    values["Custom.AICacheHitRatePct"] = cacheHitRate;
            }

            if (aggregate.DurationSeconds > 0)
                values["Custom.AISessionDurationSec"] = aggregate.DurationSeconds;

            var estimatedCost = ModelPricing.EstimateCostUsd(
                aggregate.ModelName, aggregate.InputTokens, aggregate.OutputTokens,
                aggregate.CacheReadTokens, aggregate.CacheCreationTokens);
            if (estimatedCost is not null)
                values["Custom.AIEstimatedCostUSD"] = estimatedCost;
        }

        return values;
    }

    /// <summary>
    /// Resolves each configured field with a captured value to its ADO target: a direct field patch,
    /// or a pipe-separated key=value pair folded into a single System.Tags entry (Adaptive fallback).
    /// </summary>
    private static (Dictionary<string, object?> Patch, List<string> Written, List<string> Skipped) ResolvePatch(
        IReadOnlyList<AdoTelemetryFieldDefinition> fields,
        IReadOnlyDictionary<string, object?> fieldValues,
        ResolvedTelemetryTargets targets)
    {
        var patch = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var tagPairs = new List<string>();
        var written = new List<string>();
        var skipped = new List<string>();

        foreach (var field in fields)
        {
            if (!fieldValues.TryGetValue(field.ReferenceName, out var value))
                continue; // no captured source — omit rather than write a misleading value

            if (!targets.TargetByFieldRef.TryGetValue(field.ReferenceName, out var targetRef))
            {
                skipped.Add(field.ReferenceName);
                continue;
            }

            if (string.Equals(targetRef, TagsFieldReference, StringComparison.OrdinalIgnoreCase))
                tagPairs.Add($"{ShortFieldName(field.ReferenceName)}={value}");
            else
                patch[targetRef] = value;

            written.Add(field.ReferenceName);
        }

        if (tagPairs.Count > 0)
            patch[TagsFieldReference] = string.Join("|", tagPairs);

        return (patch, written, skipped);
    }

    private async Task<TelemetryWriteBackResult> PatchAsync(
        int workItemId, IReadOnlyDictionary<string, object?> patch, List<string> written,
        List<string> skipped, PreflightMode mode, CancellationToken cancellationToken)
    {
        try
        {
            await _boardsClient.UpdateFieldsAsync(workItemId, patch, cancellationToken);
            _logger.LogInformation(
                "Wrote {Count} telemetry field(s) to work item {WorkItemId} via {Mode} mode.",
                written.Count, workItemId, mode);

            return new TelemetryWriteBackResult
            {
                Attempted = true,
                FieldsWritten = written.Count,
                SkippedFields = skipped,
                Message = $"Wrote {written.Count} telemetry field(s) via {mode} mode.",
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telemetry write-back to work item {WorkItemId} failed.", workItemId);
            return TelemetryWriteBackResult.Skipped($"Telemetry write-back failed: {ex.Message}");
        }
    }

    /// <summary>Matches the work item type to a configured key (e.g. "Task"), or null when unconfigured.</summary>
    private static string? ResolveWorkItemTypeKey(string workItemType, AdoTelemetryFieldConfig config) =>
        config.WorkItemTypes.Keys.FirstOrDefault(
            key => string.Equals(key, workItemType, StringComparison.OrdinalIgnoreCase));

    private static string ShortFieldName(string referenceName) =>
        referenceName.StartsWith(CustomFieldPrefix, StringComparison.OrdinalIgnoreCase)
            ? referenceName[CustomFieldPrefix.Length..]
            : referenceName;
}
