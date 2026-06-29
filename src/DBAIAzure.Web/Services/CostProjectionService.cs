// Projects cumulative cost-ledger totals onto a work item's two cost fields, via the active work tracker.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.WorkTracker;

namespace DBAIAzure.Web.Services;

/// <summary>
/// <see cref="ICostProjection"/>: reads the ledger totals for a binding key and writes them to the work
/// item's <see cref="LogicalField.AIRuntimeCostUSD"/> + <see cref="LogicalField.AIDevCostUSD"/> fields
/// through the active <see cref="IWorkTrackerAdapter"/> (which resolves the native field references). The
/// rollup is then native to each tracker. Best-effort — a projection failure never disrupts the caller (FR-011).
/// </summary>
public sealed class CostProjectionService : ICostProjection
{
    private readonly ICostLedger _ledger;
    private readonly IWorkTrackerAdapterProvider _trackerProvider;
    private readonly ILogger<CostProjectionService> _logger;

    public CostProjectionService(
        ICostLedger ledger, IWorkTrackerAdapterProvider trackerProvider, ILogger<CostProjectionService> logger)
    {
        _ledger = ledger;
        _trackerProvider = trackerProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ProjectAsync(string bindingKey, WorkItemRef workItem, CancellationToken cancellationToken = default)
    {
        try
        {
            var totals = await _ledger.GetTotalsAsync(bindingKey, cancellationToken);
            await _trackerProvider.GetAdapter().SetFieldsAsync(workItem, new Dictionary<string, object?>
            {
                [LogicalField.AIRuntimeCostUSD] = totals.RuntimeUsd,
                [LogicalField.AIDevCostUSD] = totals.DevelopmentUsd,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cost projection failed for binding key {BindingKey} → work item {WorkItem}.",
                bindingKey, workItem.Value);
        }
    }
}
