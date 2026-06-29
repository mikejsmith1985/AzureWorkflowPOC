// Ambient run id for the currently executing run, so LLM usage events can be correlated to it.
namespace DBAIAzure.Core.Diagnostics;

/// <summary>
/// Carries the current run id across the async call chain. Set by the workflow runner and the
/// phase-handler orchestrator immediately before their kernels execute, so the connector's usage
/// reporter can tag each LLM call with the run that made it. Unset → callers treat it as "unknown".
/// </summary>
public static class LlmRunContext
{
    /// <summary>The run id flowing through the current async context (null when no run is active).</summary>
    public static readonly AsyncLocal<string?> CurrentRunId = new();
}
