// DoR rule: every non-Trigger node must be realized before the workflow can run (FR-24.2).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Rules;

/// <summary>
/// Blocks a run when any non-Trigger node has not been realized (i.e. <c>IsConfigured</c>
/// is false). Unrealized nodes lack the executable configuration the runtime needs.
/// </summary>
public sealed class AllNodesRealizedRule : IWorkflowReadinessRule
{
    public string RuleName    => "all-nodes-realized";
    public string Description => "Every node in the workflow must be realized before the workflow can run.";

    public Task<DorRuleResult> CheckAsync(
        WorkflowDefinition workflow,
        IReadOnlyList<ConnectorConfig> connectors,
        CancellationToken ct = default)
    {
        var unrealized = workflow.Nodes
            .Where(n => n.NodeType != WorkflowNodeType.Trigger && !n.IsConfigured)
            .Select(n => n.Label ?? n.Id)
            .ToList();

        if (unrealized.Count == 0)
            return Task.FromResult(new DorRuleResult(Passed: true, RuleName: RuleName, FailureReason: null));

        var names = string.Join(", ", unrealized.Take(3));
        var suffix = unrealized.Count > 3 ? $" (+{unrealized.Count - 3} more)" : string.Empty;
        return Task.FromResult(new DorRuleResult(Passed: false, RuleName: RuleName,
            FailureReason: $"The following nodes are not yet realized: {names}{suffix}. Use 'Make it real' on each node first."));
    }
}
