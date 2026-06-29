// Append-only EF-backed AI-cost ledger (spec-017). Appends are best-effort; totals are summed per key.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DBAIAzure.Storage.Repositories;

/// <summary>
/// <see cref="ICostLedger"/> over the <c>CostLedgerEntries</c> table. Inserts only (append-only); a
/// per-key total is the SUM of its rows per dimension. <see cref="AppendAsync"/> never throws so a
/// telemetry failure cannot disrupt a run or session (FR-011).
/// </summary>
public sealed class SqlCostLedger : ICostLedger
{
    private readonly IDbContextFactory<PipelineDbContext> _contextFactory;
    private readonly ILogger<SqlCostLedger> _logger;

    public SqlCostLedger(IDbContextFactory<PipelineDbContext> contextFactory, ILogger<SqlCostLedger> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task AppendAsync(CostLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
            db.CostLedgerEntries.Add(new CostLedgerEntryEntity
            {
                Id = entry.Id,
                BindingKey = entry.BindingKey,
                Dimension = (int)entry.Dimension,
                WorkItemId = entry.WorkItemId,
                ModelName = entry.ModelName,
                InputTokens = entry.InputTokens,
                OutputTokens = entry.OutputTokens,
                CacheReadTokens = entry.CacheReadTokens,
                CostUsd = entry.CostUsd,
                OccurredAt = entry.OccurredAt,
                SourceId = entry.SourceId,
                IsUnattributed = entry.IsUnattributed,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cost ledger append failed for binding key {BindingKey}.", entry.BindingKey);
        }
    }

    /// <inheritdoc/>
    public async Task<CostTotals> GetTotalsAsync(string bindingKey, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.CostLedgerEntries
            .AsNoTracking()
            .Where(e => e.BindingKey == bindingKey)
            .Select(e => new { e.Dimension, e.CostUsd })
            .ToListAsync(cancellationToken);

        var runtime = rows.Where(r => r.Dimension == (int)CostDimension.Runtime).Sum(r => r.CostUsd);
        var development = rows.Where(r => r.Dimension == (int)CostDimension.Development).Sum(r => r.CostUsd);
        return new CostTotals(runtime, development);
    }
}
