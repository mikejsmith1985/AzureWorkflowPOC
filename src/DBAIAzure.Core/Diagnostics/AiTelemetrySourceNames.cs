// The OpenTelemetry source names for the modernized (Microsoft Agent Framework / Microsoft.Extensions.AI)
// telemetry, registered with the tracer + meter providers in place of the old Semantic Kernel source.
namespace DBAIAzure.Core.Diagnostics;

/// <summary>
/// OpenTelemetry ActivitySource / Meter names for the modernized AI telemetry (spec-019 D9). These are
/// passed to <c>.UseOpenTelemetry(sourceName)</c> / <c>.WithOpenTelemetry(sourceName)</c> and registered
/// on both the tracer and meter providers — replacing the Semantic Kernel source
/// (<c>"Microsoft.SemanticKernel*"</c>) so traces keep reaching Azure Monitor with no coverage gap.
/// </summary>
public static class AiTelemetrySourceNames
{
    /// <summary>Source name for model-call (chat client) activities and metrics.</summary>
    public const string ChatClient = "AzureWorkflowPOC.Ai";

    /// <summary>Source name for agent / workflow-level activities.</summary>
    public const string Agents = "AzureWorkflowPOC.Agents";
}
