// Persistence seam for connector configuration (FR-001 through FR-007, FR-019).
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Persists and retrieves connector settings. Abstracts the storage layer from Blazor components,
/// connector clients, and orchestrators. Secret values are encrypted at rest and never returned
/// to callers except via <see cref="GetDecryptedSecretsAsync"/>.
/// </summary>
public interface IConnectorConfigRepository
{
    /// <summary>
    /// Returns the current configuration for <paramref name="type"/>, or null if no settings
    /// have been saved yet (IsConfigured will be false for an unconfigured connector).
    /// </summary>
    Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default);

    /// <summary>
    /// Returns a configuration record for every connector type. Connectors that have never been
    /// configured are present with <c>IsConfigured = false</c>.
    /// </summary>
    Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists non-secret fields and, when <paramref name="plaintextSecretsJson"/> is not null,
    /// replaces the encrypted secret blob. Passing null for <paramref name="plaintextSecretsJson"/>
    /// leaves any existing encrypted secret unchanged. Always sets <c>IsConfigured = true</c> and
    /// resets <c>LastTestResult</c> to null (FR-017 — field edits invalidate the prior test result).
    /// </summary>
    Task SaveAsync(
        ConnectorType type,
        string? nonSecretConfigJson,
        string? plaintextSecretsJson,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the decrypted secrets JSON for <paramref name="type"/>, for use by connector clients
    /// executing a live connection. Returns null if no secrets are stored. MUST NOT be called from
    /// Blazor UI code — only from server-side service and connector classes (FR-004, Article IX).
    /// </summary>
    Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default);

    /// <summary>
    /// Records the outcome of a functional test against <paramref name="type"/>. Updates
    /// <c>LastTestResult</c>, <c>LastTestMessage</c>, and <c>LastTestedAt</c>. Does not affect
    /// <c>IsConfigured</c> or <c>LastUpdatedAt</c>.
    /// </summary>
    Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default);
}
