// Realized configuration for a HumanApproval node: who is asked, what they see, and their choices.

namespace DBAIAzure.Core.Models.NodeConfig;

/// <summary>
/// The executable configuration for a human-in-the-loop approval step. Binds to the framework's
/// existing pause/resume: the runtime suspends, presents <see cref="PromptShown"/> with
/// <see cref="DecisionOptions"/> to the configured approver chain, and resumes on their choice.
/// </summary>
public sealed record ApprovalNodeConfig
{
    /// <summary>
    /// Single-approver shorthand (legacy / realization back-compat). When <see cref="ApproverChain"/>
    /// is non-empty it takes precedence and this field is ignored by the orchestrator.
    /// </summary>
    public string? Approver { get; init; }

    /// <summary>
    /// Priority-ordered list of approver UPNs/email addresses. The first entry is notified
    /// immediately; subsequent entries are notified on escalation when the previous approver
    /// does not respond within <see cref="TimeoutMinutes"/> (FR-19.4).
    /// </summary>
    public IReadOnlyList<string> ApproverChain { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Per-approver timeout in minutes. When elapsed and the policy is "escalate",
    /// the next approver in <see cref="ApproverChain"/> is notified. 0 means no timeout.
    /// </summary>
    public int TimeoutMinutes { get; init; }

    /// <summary>
    /// Action taken when the current approver's timeout elapses and the chain is exhausted:
    /// "escalate" (notify next) | "auto-approve" | "auto-reject".
    /// Defaults to "auto-reject" to fail safely.
    /// </summary>
    public string EscalationPolicy { get; init; } = "auto-reject";

    /// <summary>What the approver is shown when the workflow pauses.</summary>
    public required string PromptShown { get; init; }

    /// <summary>The decision options offered (at least two, e.g. Approve / Reject).</summary>
    public IReadOnlyList<string> DecisionOptions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Returns the effective first approver: first entry in <see cref="ApproverChain"/>
    /// if non-empty, otherwise <see cref="Approver"/>. Returns null if neither is configured.
    /// </summary>
    public string? EffectiveFirstApprover =>
        ApproverChain.Count > 0 ? ApproverChain[0] : Approver;

    /// <summary>True when at least one approver is configured.</summary>
    public bool IsApproverConfigured =>
        !string.IsNullOrWhiteSpace(EffectiveFirstApprover);
}
