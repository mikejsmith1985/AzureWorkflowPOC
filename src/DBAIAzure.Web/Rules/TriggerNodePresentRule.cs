// DoR rule: at least one Trigger node must be present (FR-24.2).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Rules;

/// <summary>
/// Blocks a run when the workflow has no Trigger node. A workflow without a trigger
/// has no defined entry point and the orchestrator cannot determine where to start.
/// </summary>
public sealed class TriggerNodePresentRule : IWorkflowReadinessRule
{
    public string RuleName    => "trigger-node-present";
    public string Description => "The workflow must contain exactly one Trigger node that defines the entry point.";

    public Task<DorRuleResult> CheckAsync(
        WorkflowDefinition workflow,
        IReadOnlyList<ConnectorConfig> connectors,
        CancellationToken ct = default)
    {
        var hasTrigger = workflow.Nodes.Any(n => n.NodeType == WorkflowNodeType.Trigger);
        return Task.FromResult(hasTrigger
            ? new DorRuleResult(Passed: true, RuleName: RuleName, FailureReason: null)
            : new DorRuleResult(Passed: false, RuleName: RuleName,
                FailureReason: "No Trigger node found. Add a Trigger to define when the workflow should start."));
    }
}
