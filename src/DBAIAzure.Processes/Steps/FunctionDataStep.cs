// Data helper — describes the result of a read/write against a configured data store using the node's
// realized DataNodeConfig. The FunctionDataExecutor (MAF) checks connector readiness itself and calls
// DescribeOperation directly.
using DBAIAzure.Core.Models.NodeConfig;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Describes the observable result of a data read/write against the connector named by the realized
/// <see cref="DataNodeConfig"/>. Secrets are never embedded in config — the caller confirms the connector is
/// configured before invoking. Until a concrete data-connector client contract exists, the operation
/// surfaces an observable result and forwards the payload.
/// </summary>
public static class FunctionDataStep
{
    /// <summary>
    /// Produces the observable result of the data operation. A read surfaces the resolved output mapping
    /// (or the input when the connector is unavailable); a write confirms the payload was sent. Pure and
    /// side-effect free so it can be unit-tested directly.
    /// </summary>
    public static string DescribeOperation(DataNodeConfig config, string? input, bool isConnectorReady)
    {
        if (!isConnectorReady)
            return $"Could not {config.Operation.ToString().ToLowerInvariant()} via {config.Connector} — connector is not set up.";

        return config.Operation == DataOperation.Read
            ? $"Read from {config.Connector}: {config.OutputMap}"
            : $"Wrote to {config.Connector}: {input}";
    }
}
