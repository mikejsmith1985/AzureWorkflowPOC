// Persistence seam for registered apps and their build/run results (feature 013).
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Persists registered apps and their lifecycle. Owner-scoped, async, and backed by the shared
/// SQLite store via <c>IDbContextFactory</c> — mirrors the existing connector/workflow repository
/// conventions. Build/run results persisted here are already secret-redacted (FR-009).
/// </summary>
public interface IAppRegistryRepository
{
    /// <summary>
    /// Registers a new app. Throws <see cref="AppRegistrationException"/> on a duplicate name for the
    /// owner, a non-existent/inaccessible <see cref="MonitoredApp.RepoLocalPath"/>, or a missing
    /// <see cref="MonitoredApp.RunCommand"/> (FR-002). No partial row is left behind on rejection.
    /// </summary>
    Task<MonitoredApp> RegisterAsync(MonitoredApp app, CancellationToken ct = default);

    /// <summary>Returns the app with the given id, or null if none exists.</summary>
    Task<MonitoredApp?> GetAsync(string appId, CancellationToken ct = default);

    /// <summary>Returns every app belonging to the owner, newest first. Empty when none exist.</summary>
    Task<IReadOnlyList<MonitoredApp>> ListByOwnerAsync(string ownerId, CancellationToken ct = default);

    /// <summary>Returns every app that has a linked monitoring workflow, across all owners (for the monitoring loop).</summary>
    Task<IReadOnlyList<MonitoredApp>> ListMonitoredAsync(CancellationToken ct = default);

    /// <summary>
    /// Atomically sets the lifecycle status (and stamps timestamps), enforcing the legal transition
    /// table so an app is never left stuck in Building/Running (FR-003, FR-008).
    /// </summary>
    Task SetStatusAsync(string appId, AppStatus status, CancellationToken ct = default);

    /// <summary>Records a build outcome; sets status Ready (succeeded) or BuildFailed (failed).</summary>
    Task SetBuildResultAsync(string appId, AppBuildResult result, CancellationToken ct = default);

    /// <summary>Records a run outcome and returns the app to Ready regardless of run outcome.</summary>
    Task SetRunResultAsync(string appId, AppRunResult result, CancellationToken ct = default);

    /// <summary>Links (or, with null, unlinks) the chosen monitoring workflow (FR-010).</summary>
    Task SetLinkedWorkflowAsync(string appId, string? workflowId, CancellationToken ct = default);

    /// <summary>Unregisters the app and removes it from the list (FR-016).</summary>
    Task RemoveAsync(string appId, CancellationToken ct = default);

    /// <summary>True when an app with the given name already exists for the owner (FR-002).</summary>
    Task<bool> ExistsByNameAsync(string ownerId, string name, CancellationToken ct = default);
}
