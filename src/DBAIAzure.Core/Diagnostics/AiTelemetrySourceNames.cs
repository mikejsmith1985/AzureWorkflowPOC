// The OpenTelemetry source names for the Microsoft Agent Framework / Microsoft.Extensions.AI telemetry,
// registered with the tracer + meter providers.
namespace DBAIAzure.Core.Diagnostics;

/// <summary>
/// OpenTelemetry ActivitySource / Meter names for the AI telemetry (spec-019 D9). These are passed to
/// <c>.UseOpenTelemetry(sourceName)</c> / <c>.WithOpenTelemetry(sourceName)</c> and registered on both the
/// tracer and meter providers so model-call and workflow spans reach Azure Monitor.
/// </summary>
public static class AiTelemetrySourceNames
{
    /// <summary>Source name for model-call (chat client) activities and metrics.</summary>
    public const string ChatClient = "AzureWorkflowPOC.Ai";

    /// <summary>Source name for agent / workflow-level activities.</summary>
    public const string Agents = "AzureWorkflowPOC.Agents";
}
