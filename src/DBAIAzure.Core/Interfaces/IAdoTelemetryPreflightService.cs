// Primary seam between the SK step and the ADO bootstrap logic (spec-009, T011).
using DBAIAzure.Core.Models.AdoTelemetry;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Runs the ADO telemetry preflight: detects whether the target ADO organization supports
/// custom field creation, creates the required fields if it can (Bootstrap Mode), or builds
/// a native-field fallback mapping if it cannot (Adaptive Mode).
/// Callers: <c>AdoTelemetryPreflightStep</c> (SK step) and the "Test Connection" Blazor button.
/// </summary>
public interface IAdoTelemetryPreflightService
{
    /// <summary>
    /// Executes the preflight against the ADO organization resolved from
    /// <c>IConnectorConfigRepository</c> / <c>IOptions&lt;AzureDevOpsOptions&gt;</c>.
    /// Writes a manifest JSON file to the active feature's spec directory on success.
    /// </summary>
    /// <param name="overrideConfig">
    /// Custom field configuration to use instead of the embedded default. Pass null
    /// to use the default 14-field config baked into the assembly.
    /// </param>
    /// <param name="cancellationToken">Propagated to all ADO HTTP calls and file I/O.</param>
    /// <returns>
    /// A <see cref="PreflightResult"/> where <see cref="PreflightResult.IsSuccess"/> is true
    /// for both Bootstrap and Adaptive completions, and false only for blocking failures
    /// (missing config, unreachable ADO, unsupported process).
    /// </returns>
    Task<PreflightResult> RunPreflightAsync(
        AdoTelemetryFieldConfig? overrideConfig,
        CancellationToken cancellationToken = default);
}
