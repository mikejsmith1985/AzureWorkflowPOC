// Domain projection of a connector's configuration — never exposes secret values.
namespace DBAIAzure.Core.Models;

/// <summary>
/// Domain view of a connector's current configuration, returned by <c>IConnectorConfigRepository</c>.
/// Secret values are never included; use <c>HasSecrets</c> to determine whether credentials are stored.
/// </summary>
public record ConnectorConfig(
    /// <summary>Which connector this record describes.</summary>
    ConnectorType Type,

    /// <summary>Non-secret fields as raw JSON (instance URL, org URL, model name, etc.). Null when unconfigured.</summary>
    string? NonSecretConfig,

    /// <summary>True when encrypted secrets are stored — the secret value itself is never exposed.</summary>
    bool HasSecrets,

    /// <summary>True after at least one successful save.</summary>
    bool IsConfigured,

    /// <summary>Timestamp of the most recent settings save (lightweight audit trail, FR-020).</summary>
    DateTimeOffset LastUpdatedAt,

    /// <summary>
    /// Most recent functional test result, or null if the connector has never been tested or the
    /// result was invalidated by a field edit (FR-017).
    /// </summary>
    ConnectorTestResult? LastTestResult);
