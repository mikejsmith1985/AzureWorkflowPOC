// IWorkflowObserver implementation that writes events to Azure SQL / SQLite (FR-21.3).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Persists every <see cref="WorkflowExecutionEvent"/> to the <c>WorkflowExecutionEvents</c>
/// database table. Never throws — all errors are logged and swallowed so that a transient
/// DB outage cannot disrupt the executing workflow or other observers.
/// </summary>
public sealed class SqlWorkflowObserver : IWorkflowObserver
{
    private readonly IDbContextFactory<PipelineDbContext> _factory;
    private readonly ILogger<SqlWorkflowObserver> _logger;

    public SqlWorkflowObserver(
        IDbContextFactory<PipelineDbContext> factory,
        ILogger<SqlWorkflowObserver> logger)
    {
        _factory = factory;
        _logger  = logger;
    }

    /// <inheritdoc />
    public async Task RecordEventAsync(WorkflowExecutionEvent evt, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            db.WorkflowExecutionEvents.Add(new WorkflowExecutionEventEntity
            {
                EventId        = evt.EventId,
                RunId          = evt.RunId,
                NodeId         = evt.NodeId,
                NodeLabel      = evt.NodeLabel,
                EventType      = (int)evt.EventType,
                OccurredAt     = evt.OccurredAt,
                DurationMs     = evt.DurationMs,
                Outcome        = evt.Outcome,
                LlmModelName   = evt.LlmModelName,
                LlmInputTokens = evt.LlmInputTokens,
                LlmOutputTokens = evt.LlmOutputTokens,
                LlmCacheReadTokens = evt.LlmCacheReadTokens,
                LlmCacheCreationTokens = evt.LlmCacheCreationTokens,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SqlWorkflowObserver failed to persist event {EventId} for run {RunId}",
                evt.EventId, evt.RunId);
        }
    }
}
