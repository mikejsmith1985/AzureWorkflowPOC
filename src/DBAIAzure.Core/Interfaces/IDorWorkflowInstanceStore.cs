// Durable persistence for DoR workflow instances (spec-021): the queryable lifecycle + SLA record for each
// ticket's run, separate from the opaque MAF checkpoint. Idempotency and the SLA sweeper both depend on this.
using DBAIAzure.Core.Models.DorWorkflow;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Stores and queries <see cref="DorWorkflowInstance"/> rows. Persisted after every state transition (FR-031)
/// so an instance can resume after a restart, and queried by SLA deadline so the background sweeper can drive
/// escalation/manual-exit without deserializing MAF checkpoints.
/// </summary>
public interface IDorWorkflowInstanceStore
{
    /// <summary>
    /// Creates the initial instance for a ticket. Enforces idempotency (FR-004): if an active (non-terminal)
    /// instance already exists for the ticket, this returns <c>false</c> (via the unique-index constraint) so
    /// the caller discards the duplicate trigger. Returns <c>true</c> when a new instance was created.
    /// </summary>
    Task<bool> TryCreateAsync(DorWorkflowInstance instance, CancellationToken ct = default);

    /// <summary>Loads an instance by run id, or null when absent.</summary>
    Task<DorWorkflowInstance?> GetAsync(string runId, CancellationToken ct = default);

    /// <summary>Persists the current state of an instance (called after every transition).</summary>
    Task UpdateAsync(DorWorkflowInstance instance, CancellationToken ct = default);

    /// <summary>Lists all non-terminal instances — used by the reply-pump and by restart rehydration.</summary>
    Task<IReadOnlyList<DorWorkflowInstance>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists instances awaiting a human whose SLA deadline is at or before <paramref name="asOf"/> — the
    /// sweeper's escalation/manual-exit work queue.
    /// </summary>
    Task<IReadOnlyList<DorWorkflowInstance>> ListDueSlaAsync(DateTimeOffset asOf, CancellationToken ct = default);
}
