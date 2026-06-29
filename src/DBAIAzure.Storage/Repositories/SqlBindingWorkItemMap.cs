// EF-backed binding key → work item map (spec-017, C1). Populated at creation; read by dev-usage ingest.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace DBAIAzure.Storage.Repositories;

/// <summary><see cref="IBindingWorkItemMap"/> over the <c>BindingWorkItemMap</c> table.</summary>
public sealed class SqlBindingWorkItemMap : IBindingWorkItemMap
{
    private readonly IDbContextFactory<PipelineDbContext> _contextFactory;

    public SqlBindingWorkItemMap(IDbContextFactory<PipelineDbContext> contextFactory) =>
        _contextFactory = contextFactory;

    /// <inheritdoc/>
    public async Task PutAsync(string bindingKey, int workItemId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.BindingWorkItemMap.FindAsync(new object?[] { bindingKey }, cancellationToken);
        if (existing is null)
            db.BindingWorkItemMap.Add(new BindingWorkItemMapEntity { BindingKey = bindingKey, WorkItemId = workItemId });
        else
            existing.WorkItemId = workItemId;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int?> ResolveAsync(string bindingKey, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.BindingWorkItemMap
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.BindingKey == bindingKey, cancellationToken);
        return row?.WorkItemId;
    }
}
