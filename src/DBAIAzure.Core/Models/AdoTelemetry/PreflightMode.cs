// Indicates which preflight execution path was chosen based on ADO admin rights.
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// Determines whether the preflight created custom fields (Bootstrap) or
/// fell back to mapping native ADO fields (Adaptive) due to insufficient admin rights.
/// </summary>
public enum PreflightMode
{
    /// <summary>
    /// Admin rights were confirmed — the service created or verified all 14 custom telemetry fields.
    /// </summary>
    Bootstrap,

    /// <summary>
    /// Admin rights were absent — the service built a native-field fallback mapping
    /// without attempting any field creation.
    /// </summary>
    Adaptive,
}
