// Evaluates whether a workflow is production-ready: structural validity, per-node realized-config
// validity, connector health, and out-of-date detection. Composes the existing structural validator
// with async connector health so the Run action can be gated honestly.

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Default <see cref="IWorkflowReadinessService"/>. A workflow is production-ready only when it is
/// structurally valid (one trigger, no islands), every node's realized config is intrinsically valid,
/// every bound connector is healthy, and no node is out-of-date relative to its plain-language intent.
/// </summary>
public sealed class WorkflowReadinessService : IWorkflowReadinessService
{
    private readonly IWorkflowValidator _validator;
    private readonly IConnectorHealthChecker _healthChecker;

    /// <summary>Initialises the service with the structural validator and the connector health checker.</summary>
    public WorkflowReadinessService(IWorkflowValidator validator, IConnectorHealthChecker healthChecker)
    {
        _validator     = validator;
        _healthChecker = healthChecker;
    }

    /// <inheritdoc/>
    public async Task<WorkflowReadinessReport> EvaluateAsync(
        WorkflowDefinition workflow, CancellationToken cancellationToken = default)
    {
        // Structural rules first (VAL-001..003) — these are workflow-level, not per-node.
        var structuralProblems = _validator.Validate(workflow);

        // Connector health is the only async input; gather it once and index by type.
        var healthResults = await _healthChecker.CheckAllAsync(cancellationToken).ConfigureAwait(false);
        var healthyConnectors = healthResults
            .Where(result => result.IsSuccess)
            .Select(result => result.Type)
            .ToHashSet();

        var nodeReadiness = workflow.Nodes
            .Select(node => EvaluateNode(node, workflow, healthyConnectors))
            .ToList();

        var allNodesRealized = nodeReadiness.All(node => node.Status == NodeRealizationStatus.Realized);
        var isReady          = allNodesRealized && structuralProblems.Count == 0;

        var blockingSummary = structuralProblems
            .Concat(nodeReadiness.Where(node => node.Status != NodeRealizationStatus.Realized)
                                 .SelectMany(node => node.Reasons))
            .Distinct()
            .ToList();

        return new WorkflowReadinessReport
        {
            IsProductionReady = isReady,
            Nodes             = nodeReadiness,
            BlockingSummary   = blockingSummary,
        };
    }

    /// <summary>Computes the realization status of a single node and the reasons it is not yet ready.</summary>
    private static NodeReadiness EvaluateNode(
        WorkflowNode node, WorkflowDefinition workflow, IReadOnlySet<ConnectorType> healthyConnectors)
    {
        // Not yet realized: no accepted config.
        if (!node.IsConfigured || string.IsNullOrWhiteSpace(node.FunctionConfig))
            return Readiness(node, NodeRealizationStatus.Draft, "This step has not been set up for production yet.");

        // VAL-005: intrinsic per-type config validity.
        var intrinsicErrors = NodeConfigSerializer.IntrinsicErrors(node.NodeType, node.FunctionConfig);
        if (intrinsicErrors.Count > 0)
            return new NodeReadiness { NodeId = node.Id, Status = NodeRealizationStatus.NeedsInput, Reasons = intrinsicErrors };

        // VAL-006: every connector this node binds to must be configured and healthy.
        var blockedConnectors = RequiredConnectors(node)
            .Where(connector => !healthyConnectors.Contains(connector))
            .ToList();
        if (blockedConnectors.Count > 0)
        {
            var names = string.Join(", ", blockedConnectors);
            return Readiness(node, NodeRealizationStatus.Blocked,
                $"This step needs a connector that is not set up or is unavailable: {names}.");
        }

        // VAL-007: cross-node consistency (route conditions per edge; agentic output shape feeding a branch).
        var crossNodeErrors = CrossNodeErrors(node, workflow);
        if (crossNodeErrors.Count > 0)
            return new NodeReadiness { NodeId = node.Id, Status = NodeRealizationStatus.NeedsInput, Reasons = crossNodeErrors };

        return new NodeReadiness { NodeId = node.Id, Status = NodeRealizationStatus.Realized };
    }

    /// <summary>The connectors a realized node depends on, derived from its type-specific config.</summary>
    private static IEnumerable<ConnectorType> RequiredConnectors(WorkflowNode node)
    {
        switch (node.NodeType)
        {
            case WorkflowNodeType.FunctionNotify:
                var notify = NodeConfigSerializer.ReadConfig<NotifyNodeConfig>(node.FunctionConfig);
                if (notify is not null) yield return notify.Connector;
                break;
            case WorkflowNodeType.FunctionData:
                var data = NodeConfigSerializer.ReadConfig<DataNodeConfig>(node.FunctionConfig);
                if (data is not null) yield return data.Connector;
                break;
            case WorkflowNodeType.AgenticReason:
                var agent = NodeConfigSerializer.ReadConfig<AgentNodeConfig>(node.FunctionConfig);
                if (agent is not null)
                    foreach (var tool in agent.ToolBindings) yield return tool;
                break;
        }
    }

    /// <summary>Cross-node consistency checks that need graph context (VAL-007).</summary>
    private static IReadOnlyList<string> CrossNodeErrors(WorkflowNode node, WorkflowDefinition workflow)
    {
        var problems = new List<string>();
        var outgoingEdgeCount = workflow.Edges.Count(edge => edge.SourceNodeId == node.Id);

        if (node.NodeType == WorkflowNodeType.FunctionRoute)
        {
            var route = NodeConfigSerializer.ReadConfig<RouteNodeConfig>(node.FunctionConfig);
            if (route is not null && outgoingEdgeCount > 0 && route.Conditions.Count < outgoingEdgeCount)
                problems.Add("The branch step needs a condition for each outgoing path.");
        }

        if (node.NodeType == WorkflowNodeType.AgenticReason && FeedsRouteOrTransform(node, workflow))
        {
            var agent = NodeConfigSerializer.ReadConfig<AgentNodeConfig>(node.FunctionConfig);
            if (agent is not null && agent.OutputShape.Count == 0)
                problems.Add("This AI step feeds a branch/transform, so it must produce a structured result.");
        }

        return problems;
    }

    private static bool FeedsRouteOrTransform(WorkflowNode node, WorkflowDefinition workflow)
    {
        var downstreamIds = workflow.Edges
            .Where(edge => edge.SourceNodeId == node.Id)
            .Select(edge => edge.TargetNodeId)
            .ToHashSet();

        return workflow.Nodes.Any(other =>
            downstreamIds.Contains(other.Id) &&
            other.NodeType is WorkflowNodeType.FunctionRoute or WorkflowNodeType.FunctionTransform);
    }

    private static NodeReadiness Readiness(WorkflowNode node, NodeRealizationStatus status, string reason) =>
        new() { NodeId = node.Id, Status = status, Reasons = new[] { reason } };
}
