// Resolves the active Work Tracking System connector (provider + credentials) from the store per run (spec-020).
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// The single place that reads the generic <see cref="ConnectorType.WorkTracker"/> connector row, decrypts its
/// secret, and reports which provider is active. Every downstream consumer dispatches on the result so no
/// tracker-specific row parsing is duplicated. Resolution happens on every call (per run) so UI-entered
/// changes take effect without an application restart (spec-020 FR-004/FR-005).
/// </summary>
public interface IWorkTrackerConfigResolver
{
    /// <summary>
    /// Reads the active Work Tracking System connector from the store. Returns
    /// <see cref="ResolvedWorkTrackerConfig.IsConfigured"/> = false when no connector row exists or no
    /// provider is set. Best-effort: a store or decryption error resolves to unconfigured rather than throwing.
    /// </summary>
    Task<ResolvedWorkTrackerConfig> ResolveActiveAsync(CancellationToken ct = default);
}
