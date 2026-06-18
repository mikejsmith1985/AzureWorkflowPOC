// Typed result carrying the set of connectors that blocked a pipeline run pre-flight (FR-018).
namespace DBAIAzure.Core.Models;

/// <summary>
/// Returned internally by <c>PipelineOrchestrator</c> and <c>PhaseHandlerOrchestrator</c> when the
/// live pre-flight check blocks a run (FR-018, SC-008). Carries every failing test result so the
/// orchestrator can build a precise diagnostic without additional round-trips. This is a data record,
/// not an exception — orchestrators inspect it and mark the run as <c>Failed</c>.
/// </summary>
public record PipelinePreflightFailure(
    /// <summary>
    /// All connectors whose live test returned <c>IsSuccess = false</c> during the pre-flight
    /// <c>CheckAllAsync</c> call. At least one entry is always present.
    /// </summary>
    IReadOnlyList<ConnectorTestResult> FailingConnectors);
