// EF entity for the append-only AI-cost ledger (spec-017). Maps to the CostLedgerEntries table.
namespace DBAIAzure.Storage.Entities;

/// <summary>
/// One immutable cost record. Rows are only ever inserted (append-only); per-ticket totals are SUMs
/// over <see cref="BindingKey"/> + <see cref="Dimension"/>.
/// </summary>
public sealed class CostLedgerEntryEntity
{
    public Guid Id { get; set; }

    /// <summary>Canonical ticket binding key (indexed — the join + totals key).</summary>
    public string BindingKey { get; set; } = string.Empty;

    /// <summary><c>CostDimension</c> stored as its integer ordinal (Runtime=0, Development=1).</summary>
    public int Dimension { get; set; }

    /// <summary>Anchor work item; null when unattributed.</summary>
    public int? WorkItemId { get; set; }

    public string? ModelName { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public double CostUsd { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Run id (runtime) or session id (development).</summary>
    public string? SourceId { get; set; }

    /// <summary>True when the binding key did not resolve (FR-010).</summary>
    public bool IsUnattributed { get; set; }
}
