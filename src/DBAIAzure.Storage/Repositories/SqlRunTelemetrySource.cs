// Reads a run's recorded LLM execution events from the database and aggregates them for write-back.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Storage.Repositories;

/// <summary>
/// <see cref="IRunTelemetrySource"/> over the persisted <c>WorkflowExecutionEvents</c>. Selects the
/// run's events that carry LLM usage and folds them into a <see cref="RunTelemetryAggregate"/>.
/// Rows are materialized before aggregating so ordering happens in memory — SQLite cannot translate
/// ordering over <see cref="DateTimeOffset"/>.
/// </summary>
public sealed class SqlRunTelemetrySource : IRunTelemetrySource
{
    private readonly IDbContextFactory<PipelineDbContext> _contextFactory;

    public SqlRunTelemetrySource(IDbContextFactory<PipelineDbContext> contextFactory) =>
        _contextFactory = contextFactory;

    /// <inheritdoc/>
    public async Task<RunTelemetryAggregate> GetAggregateAsync(
        string runId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // All LLM-call events for the run — including failures (Outcome="error", null tokens) so they
        // are counted. The integer compare is translatable; the error/cache mapping happens in memory.
        var llmCallCompleted = (int)WorkflowEventType.LlmCallCompleted;
        var rows = await db.WorkflowExecutionEvents
            .AsNoTracking()
            .Where(evt => evt.RunId == runId && evt.EventType == llmCallCompleted)
            .Select(evt => new
            {
                evt.OccurredAt,
                evt.DurationMs,
                evt.LlmModelName,
                evt.LlmInputTokens,
                evt.LlmOutputTokens,
                evt.LlmCacheReadTokens,
                evt.LlmCacheCreationTokens,
                evt.Outcome,
            })
            .ToListAsync(cancellationToken);

        var samples = rows
            .Select(row => new LlmTelemetrySample(
                row.OccurredAt, row.DurationMs, row.LlmModelName, row.LlmInputTokens, row.LlmOutputTokens,
                CacheReadTokens: row.LlmCacheReadTokens ?? 0,
                CacheCreationTokens: row.LlmCacheCreationTokens ?? 0,
                IsError: string.Equals(row.Outcome, "error", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return RunTelemetryAggregate.FromSamples(runId, samples);
    }
}
