// Persists user-configurable workflow execution settings across sessions.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Immutable snapshot of user-configurable settings that govern a single workflow execution run.
/// Stored and reloaded between sessions so the user does not need to re-enter preferences.
/// </summary>
public sealed record WorkflowSettings
{
    /// <summary>
    /// Maximum number of minutes the workflow process is allowed to run before being cancelled.
    /// Protects against runaway executions that would otherwise consume Azure DevOps API quota.
    /// </summary>
    public int ExecutionTimeoutMinutes { get; init; } = 5;

    /// <summary>
    /// Answers previously collected by <c>WorkflowDesignSkillService</c> for its clarifying
    /// questions, keyed by question identifier. A value of <c>"user-deferred"</c> means the user
    /// explicitly dismissed that question without providing an answer and it should not be asked
    /// again during the same session.
    /// </summary>
    public IReadOnlyDictionary<string, string> DesignSkillAnswers { get; init; } =
        new Dictionary<string, string>().AsReadOnly();

    /// <summary>
    /// The natural-language description the user provided as input to the most recent workflow run,
    /// pre-populated in the UI so the user can refine rather than retype it.
    /// <c>null</c> when no run has been attempted yet in any session.
    /// </summary>
    public string? LastRunInputDescription { get; init; }
}
