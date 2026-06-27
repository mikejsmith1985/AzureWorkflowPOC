// DoR rule: every approval node must have at least one approver configured (FR-24.2).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;

namespace DBAIAzure.Web.Rules;

/// <summary>
/// Blocks a run when any HumanApproval node lacks an approver configuration.
/// Without an approver the run would park indefinitely with no notification sent.
/// </summary>
public sealed class ApprovalNodesConfiguredRule : IWorkflowReadinessRule
{
    public string RuleName    => "approval-nodes-configured";
    public string Description => "Every Human Approval node must have at least one approver configured.";

    public Task<DorRuleResult> CheckAsync(
        WorkflowDefinition workflow,
        IReadOnlyList<ConnectorConfig> connectors,
        CancellationToken ct = default)
    {
        var unconfigured = workflow.Nodes
            .Where(n => n.NodeType == WorkflowNodeType.HumanApproval)
            .Where(n =>
            {
                var config = NodeConfigSerializer.ReadConfig<ApprovalNodeConfig>(n.FunctionConfig);
                return config is null || !config.IsApproverConfigured;
            })
            .Select(n => n.Label ?? n.Id)
            .ToList();

        if (unconfigured.Count == 0)
            return Task.FromResult(new DorRuleResult(Passed: true, RuleName: RuleName, FailureReason: null));

        var names = string.Join(", ", unconfigured);
        return Task.FromResult(new DorRuleResult(Passed: false, RuleName: RuleName,
            FailureReason: $"The following approval nodes have no approver: {names}. Realize each node and configure the approver chain."));
    }
}
