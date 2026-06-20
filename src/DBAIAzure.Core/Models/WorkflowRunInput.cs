// Carries the user-provided test scenario and workflow identity into the execution orchestrator.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Structured input produced by the run-input modal when the user describes a test scenario.
/// Passed to <c>IWorkflowExecutionOrchestrator.StartRunAsync</c> to begin a workflow run.
/// </summary>
public sealed record WorkflowRunInput
{
    /// <summary>Plain-English scenario text entered by the user.</summary>
    public required string ScenarioText { get; init; }

    /// <summary>The workflow this run targets.</summary>
    public required Guid WorkflowId { get; init; }

    /// <summary>UTC timestamp when the user submitted the input.</summary>
    public required DateTimeOffset SubmittedAt { get; init; }

    /// <summary>
    /// Optional key/value map of structured parameters extracted from the scenario text
    /// by <c>WorkflowInputTranslator</c>. Null before translation; empty after
    /// translation when no named parameters were identified.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Parameters { get; init; }
}
