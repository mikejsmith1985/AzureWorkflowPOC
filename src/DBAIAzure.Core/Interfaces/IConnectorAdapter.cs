// Abstraction for a connector that can execute workflow-node actions (T091).
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Represents a concrete integration adapter that can be driven from a workflow node.
/// Each adapter handles one <see cref="ConnectorType"/> and exposes two operations:
/// <see cref="ExecuteAsync"/> (action execution) and <see cref="HealthCheckAsync"/> (liveness probe).
///
/// Connectors are resolved by <see cref="ConnectorType"/> at run-time; the workflow node's
/// realized configuration supplies the <paramref name="payload"/>.
/// </summary>
public interface IConnectorAdapter
{
    /// <summary>Which connector this adapter drives.</summary>
    ConnectorType ConnectorType { get; }

    /// <summary>
    /// Executes the connector action described by <paramref name="payload"/> — for example,
    /// creating a work item or sending a Teams message. Returns a human-readable outcome string.
    /// </summary>
    Task<string> ExecuteAsync(string payload, CancellationToken ct = default);

    /// <summary>
    /// Performs a lightweight liveness check against the connector's remote endpoint.
    /// Returns a <see cref="ConnectorTestResult"/> that may be surfaced in the Settings UI.
    /// </summary>
    Task<ConnectorTestResult> HealthCheckAsync(CancellationToken ct = default);
}
