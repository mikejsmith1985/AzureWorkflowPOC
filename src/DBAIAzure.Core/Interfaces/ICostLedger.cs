// Append-only AI-cost ledger — both cost dimensions write here; per-ticket totals are sums over it.
using DBAIAzure.Core.Models.AdoTelemetry;

namespace DBAIAzure.Core.Interfaces;

/// <summary>Cumulative cost totals for a binding key, split by dimension.</summary>
public sealed record CostTotals(double RuntimeUsd, double DevelopmentUsd);

/// <summary>
/// The cost ledger: append-only storage of <see cref="CostLedgerEntry"/> and a totals query. Appends
/// MUST be best-effort (never throw) so a telemetry failure cannot disrupt a run, validation, board
/// write, or a developer's session (FR-011).
/// </summary>
public interface ICostLedger
{
    /// <summary>Appends one cost record. Never throws — failures are logged and swallowed.</summary>
    Task AppendAsync(CostLedgerEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Returns the cumulative runtime + development totals for a binding key.</summary>
    Task<CostTotals> GetTotalsAsync(string bindingKey, CancellationToken cancellationToken = default);
}
