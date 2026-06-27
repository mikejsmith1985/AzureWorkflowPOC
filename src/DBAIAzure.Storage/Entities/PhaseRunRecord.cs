// Persisted audit record for one phase-handler run; the stored work item ids anchor idempotent upsert.
using DBAIAzure.Core.Models;

namespace DBAIAzure.Storage.Entities;

/// <summary>
/// Durable representation of a single phase-handler run. Scalars are mapped to columns for
/// filtering and idempotency; the richer values (gaps, decision, created work items) are stored
/// as JSON. A unique index on (FeatureKey, Phase) enforces one record per phase and backs the
/// idempotent-upsert lookup (FR-013).
/// </summary>
public class PhaseRunRecord
{
    /// <summary>Run identifier (primary key).</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Feature slug — idempotency key part 1 (indexed).</summary>
    public string FeatureKey { get; set; } = string.Empty;

    /// <summary>Completed phase as a string — idempotency key part 2 (indexed).</summary>
    public string Phase { get; set; } = nameof(SpecKitPhase.Unsupported);

    /// <summary><see cref="PhaseRunStatus"/> as a string.</summary>
    public string Status { get; set; } = nameof(PhaseRunStatus.Received);

    /// <summary>Validation summary shown to the reviewer.</summary>
    public string? Summary { get; set; }

    /// <summary>Serialised <c>PhaseValidationGap[]</c>.</summary>
    public string? GapsJson { get; set; }

    /// <summary>Serialised <see cref="ApprovalDecision"/>.</summary>
    public string? DecisionJson { get; set; }

    /// <summary>Serialised <c>CreatedWorkItemRef[]</c> — used for the upsert lookup.</summary>
    public string? WorkItemIdsJson { get; set; }

    /// <summary>Populated on a recorded failure.</summary>
    public string? FailureReason { get; set; }

    /// <summary>When the run started.</summary>
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the run reached a terminal state.</summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
