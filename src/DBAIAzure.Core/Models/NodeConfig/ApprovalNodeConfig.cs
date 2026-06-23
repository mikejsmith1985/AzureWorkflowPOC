// Realized configuration for a HumanApproval node: who is asked, what they see, and their choices.

namespace DBAIAzure.Core.Models.NodeConfig;

/// <summary>
/// The executable configuration for a human-in-the-loop approval step. Binds to the framework's
/// existing pause/resume: the runtime suspends, presents <see cref="PromptShown"/> with
/// <see cref="DecisionOptions"/> to <see cref="Approver"/>, and resumes on their choice (FR-15.5).
/// </summary>
public sealed record ApprovalNodeConfig
{
    /// <summary>Who is asked to make the decision.</summary>
    public required string Approver { get; init; }

    /// <summary>What the approver is shown when the workflow pauses.</summary>
    public required string PromptShown { get; init; }

    /// <summary>The decision options offered (at least two, e.g. Approve / Reject).</summary>
    public IReadOnlyList<string> DecisionOptions { get; init; } = Array.Empty<string>();
}
