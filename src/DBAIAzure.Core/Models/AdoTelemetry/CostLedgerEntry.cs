// Append-only cost record — the source of truth for AI spend, summed per binding key + dimension.
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>The two AI-spend dimensions tracked separately yet rolled up the same work hierarchy.</summary>
public enum CostDimension
{
    /// <summary>AI the product pipeline consumes when it runs (validation, workflow execution).</summary>
    Runtime,

    /// <summary>AI engineers consume building the work (coding-agent sessions).</summary>
    Development,
}

/// <summary>
/// One immutable cost record. Per-ticket totals are derived by summing entries over a binding key and
/// dimension — never overwritten — so cost is cumulative by construction (FR-007) and a single run
/// contributes exactly once (FR-008). An entry whose binding key did not resolve is flagged
/// <see cref="IsUnattributed"/> rather than dropped (FR-010).
/// </summary>
public sealed record CostLedgerEntry
{
    /// <summary>Surrogate id (assigned on append).</summary>
    public required Guid Id { get; init; }

    /// <summary>The canonical ticket binding key this cost is attributed to.</summary>
    public required string BindingKey { get; init; }

    /// <summary>Runtime vs Development.</summary>
    public required CostDimension Dimension { get; init; }

    /// <summary>The anchor work item the cost lands on; null when unattributed.</summary>
    public int? WorkItemId { get; init; }

    /// <summary>Model that produced the usage (null when unknown).</summary>
    public string? ModelName { get; init; }

    /// <summary>Token usage backing the cost.</summary>
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int CacheReadTokens { get; init; }

    /// <summary>Estimated USD cost (priced via ModelPricing).</summary>
    public required double CostUsd { get; init; }

    /// <summary>When the underlying usage occurred.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Run id (runtime) or session id (development) that produced this entry.</summary>
    public string? SourceId { get; init; }

    /// <summary>True when the binding key did not resolve to a known ticket (FR-010).</summary>
    public bool IsUnattributed { get; init; }
}
