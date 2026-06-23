// Outcome record for a single Definition of Ready rule evaluation.

namespace DBAIAzure.Core.Models;

/// <summary>
/// The outcome of evaluating one <c>IWorkflowReadinessRule</c> against a workflow definition.
/// A workflow may run only when every result has <see cref="Passed"/> set to true.
/// </summary>
public sealed record DorRuleResult(
    /// <summary>Whether the rule passed. False means the run must be blocked.</summary>
    bool Passed,

    /// <summary>The stable rule name from <c>IWorkflowReadinessRule.RuleName</c>.</summary>
    string RuleName,

    /// <summary>Plain-language reason the rule failed; null when <see cref="Passed"/> is true.</summary>
    string? FailureReason
);
