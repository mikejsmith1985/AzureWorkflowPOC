// Persistence for monitoring heartbeats and close-the-loop dedup (feature 013).
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Records per-app monitoring health (heartbeats) and de-duplicates raised issues across cycles so a
/// recurring problem produces one run, not one per cycle (FR-012, FR-013).
/// </summary>
public interface IAppHeartbeatStore
{
    /// <summary>Records the outcome of a monitoring cycle for the app (last cycle time, ok/fail, error).</summary>
    Task RecordCycleAsync(string appId, bool ok, string? error, CancellationToken ct = default);

    /// <summary>Returns the latest heartbeat for the app, or null if it has never been monitored.</summary>
    Task<AppMonitoringHeartbeat?> GetAsync(string appId, CancellationToken ct = default);

    /// <summary>True when an issue with the given signature has already produced a run (cross-cycle dedup).</summary>
    Task<bool> IsRaisedAsync(string signature, CancellationToken ct = default);

    /// <summary>Records that an issue signature was raised (idempotent on the signature).</summary>
    Task RecordRaisedAsync(AppRaisedIssue issue, CancellationToken ct = default);
}
