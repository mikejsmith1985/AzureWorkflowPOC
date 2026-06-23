// Contract for one Definition of Ready rule and the validator that aggregates them (FR-24).

using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// A single configurable Definition of Ready rule evaluated before a workflow run starts.
/// Rules are registered via DI and resolved by <see cref="IWorkflowPreRunValidator"/>.
/// Implementations must never throw — return a failing <see cref="DorRuleResult"/> instead.
/// </summary>
public interface IWorkflowReadinessRule
{
    /// <summary>
    /// Short, stable name used in <c>DorRuleSettings.DisabledRuleNames</c> to toggle this rule off.
    /// Must be unique across all registered rules.
    /// </summary>
    string RuleName { get; }

    /// <summary>Human-readable description shown in the settings UI and in the block message.</summary>
    string Description { get; }

    /// <summary>
    /// Evaluates the rule against the workflow definition and current connector health state.
    /// Returns a passing or failing <see cref="DorRuleResult"/> — never throws.
    /// </summary>
    Task<DorRuleResult> CheckAsync(
        WorkflowDefinition workflow,
        IReadOnlyList<ConnectorConfig> connectors,
        CancellationToken ct = default);
}
