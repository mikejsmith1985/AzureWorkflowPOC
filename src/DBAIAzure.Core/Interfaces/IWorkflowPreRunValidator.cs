// Contract for the aggregating Definition of Ready validator (FR-24).

using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Evaluates all registered <see cref="IWorkflowReadinessRule"/> implementations
/// and returns an ordered result set for the WorkflowBuilder UI.
/// Rules listed in <c>DorRuleSettings.DisabledRuleNames</c> are skipped silently.
/// </summary>
public interface IWorkflowPreRunValidator
{
    /// <summary>
    /// Returns results for all enabled rules. A workflow may run only when every result
    /// has <c>Passed = true</c>. Failing rules appear first in the returned list
    /// so the UI can display blockers at the top without re-sorting.
    /// </summary>
    Task<IReadOnlyList<DorRuleResult>> ValidateAsync(
        WorkflowDefinition workflow,
        CancellationToken ct = default);
}
