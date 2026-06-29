// Projects cumulative cost-ledger totals onto a work item's two cost fields for ADO Analytics rollup.
using DBAIAzure.Core.Interfaces;

namespace DBAIAzure.Web.Services;

/// <summary>
/// <see cref="ICostProjection"/>: reads the ledger totals for a binding key and writes them to the
/// work item's <c>Custom.AIRuntimeCostUSD</c> + <c>Custom.AIDevCostUSD</c> fields (the numeric fields
/// ADO Analytics sums up the tree). Best-effort — a projection failure never disrupts the caller (FR-011).
/// </summary>
public sealed class CostProjectionService : ICostProjection
{
    private readonly ICostLedger _ledger;
    private readonly IBoardsClient _boardsClient;
    private readonly ILogger<CostProjectionService> _logger;

    public CostProjectionService(ICostLedger ledger, IBoardsClient boardsClient, ILogger<CostProjectionService> logger)
    {
        _ledger = ledger;
        _boardsClient = boardsClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ProjectAsync(string bindingKey, int workItemId, CancellationToken cancellationToken = default)
    {
        try
        {
            var totals = await _ledger.GetTotalsAsync(bindingKey, cancellationToken);
            await _boardsClient.UpdateFieldsAsync(workItemId, new Dictionary<string, object?>
            {
                ["Custom.AIRuntimeCostUSD"] = totals.RuntimeUsd,
                ["Custom.AIDevCostUSD"] = totals.DevelopmentUsd,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cost projection failed for binding key {BindingKey} → work item {WorkItemId}.",
                bindingKey, workItemId);
        }
    }
}
