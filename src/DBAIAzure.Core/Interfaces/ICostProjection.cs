// Projects a binding key's cumulative ledger totals onto its work item's cost fields (spec-017).
namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Recomputes the cumulative per-item cost fields (<c>Custom.AIRuntimeCostUSD</c>,
/// <c>Custom.AIDevCostUSD</c>) from the cost ledger so ADO Analytics can sum them up the work
/// hierarchy. Called after each ledger append (runtime or development). Best-effort — never throws.
/// </summary>
public interface ICostProjection
{
    /// <summary>Writes the ledger totals for <paramref name="bindingKey"/> onto the work item's cost fields.</summary>
    Task ProjectAsync(string bindingKey, int workItemId, CancellationToken cancellationToken = default);
}
