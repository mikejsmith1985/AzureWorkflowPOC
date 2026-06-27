// EF Core entity: one row per connector type in the ConnectorConfigs SQLite table.
namespace DBAIAzure.Storage.Entities;

/// <summary>
/// Persists one configuration row per connector type. <c>EncryptedSecretsJson</c> holds an
/// <c>ISecretProtector</c>-encrypted blob and is never returned to any UI layer (FR-019).
/// </summary>
public sealed class ConnectorConfigRecord
{
    /// <summary>Auto-increment primary key.</summary>
    public int Id { get; set; }

    /// <summary>Unique connector identifier; values match <c>ConnectorType</c> enum names.</summary>
    public string ConnectorType { get; set; } = string.Empty;

    /// <summary>Non-secret fields as JSON (instance URL, org URL, model name, etc.). Null when unconfigured.</summary>
    public string? ConfigJson { get; set; }

    /// <summary>All secret fields for this connector encrypted as a single JSON blob. Never returned to UI.</summary>
    public string? EncryptedSecretsJson { get; set; }

    /// <summary>True after the operator has saved at least one configuration via the modal.</summary>
    public bool IsConfigured { get; set; }

    /// <summary>Set on every save — lightweight change audit (FR-020).</summary>
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>"Pass" | "Fail" | null (never tested, or reset after a field edit per FR-017).</summary>
    public string? LastTestResult { get; set; }

    /// <summary>Plain-language diagnostic from the most recent functional test.</summary>
    public string? LastTestMessage { get; set; }

    /// <summary>Timestamp of the most recent functional test (manual or pre-flight).</summary>
    public DateTimeOffset? LastTestedAt { get; set; }
}
