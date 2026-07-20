// Resolves the active DoR Validation Workflow configuration (six namespaces + secrets) from the connector store
// per run (spec-021), mirroring IWorkTrackerConfigResolver so UI changes apply without a restart.
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// The single place that reads the <c>ConnectorType.DorWorkflow</c> connector row, parses the six configuration
/// namespaces, and decrypts the referenced secrets. Resolution happens on every call (per run) so operator
/// changes take effect without an application restart (FR-025), consistent with the LLM/work-tracker hot-reload.
/// </summary>
public interface IDorConfigResolver
{
    /// <summary>
    /// Reads and parses the active DoR workflow configuration. Returns
    /// <see cref="DorWorkflowConfig.Unconfigured"/> when no row exists or it cannot be parsed. Best-effort — a
    /// store or decryption error resolves to unconfigured rather than throwing.
    /// </summary>
    Task<DorWorkflowConfig> ResolveActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves the decrypted secrets for the active DoR connector (server-side only). Returns a record of nulls
    /// when unconfigured. The values MUST NOT be logged or returned to the UI (Article IX / FR-026).
    /// </summary>
    Task<DorWorkflowSecrets> ResolveSecretsAsync(CancellationToken ct = default);
}
