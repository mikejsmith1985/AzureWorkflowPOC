// Translates a persisted WorkflowDefinition into a live MAF Workflow (spec-019 T021) — the GA
// replacement for the SK WorkflowRuntimeBuilder. One executor per node (id == node id); a FunctionRoute
// node routes by directing the run to the chosen port's target node.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Executors;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// Builds a runtime MAF <see cref="Workflow"/> from a visual <see cref="WorkflowDefinition"/>: one
/// <see cref="Executor"/> per canvas node (executor id == node id) and one edge per canvas edge. A
/// <see cref="WorkflowNodeType.FunctionRoute"/> node chooses an output port via the model and then
/// <em>directs</em> the run to that port's target node — the MAF equivalent of the SK route step's
/// port-label event routing (there is no <c>AddSwitch</c> in the GA API). Replaces
/// <c>WorkflowRuntimeBuilder</c>.
/// </summary>
public sealed class MafWorkflowRuntimeFactory
{
    /// <summary>
    /// Output-port labels keyed by node id for every <see cref="WorkflowNodeType.FunctionRoute"/> node in
    /// the last <see cref="Build"/> call — the label set each route node's decision is validated against.
    /// </summary>
    public Dictionary<string, IReadOnlyList<string>> PortLabelsByNodeId { get; } = [];

    /// <inheritdoc cref="MafWorkflowRuntimeFactory"/>
    public Workflow Build(WorkflowDefinition workflow, IChatClient chatClient, IServiceProvider? services = null)
    {
        if (workflow.Nodes.Count == 0)
        {
            throw new InvalidOperationException($"WorkflowDefinition '{workflow.Id}' has no nodes and cannot be executed.");
        }

        PortLabelsByNodeId.Clear();
        var connectorRepository = services?.GetService(typeof(IConnectorConfigRepository)) as IConnectorConfigRepository;

        // A node is terminal when no edge leaves it — terminal nodes yield the run's output.
        var nodesWithOutgoingEdges = workflow.Edges.Select(edge => edge.SourceNodeId).ToHashSet(StringComparer.Ordinal);
        bool IsTerminal(string nodeId) => !nodesWithOutgoingEdges.Contains(nodeId);

        // One executor per node, keyed by node id so the edge pass can look sources/targets up directly. A
        // HumanApproval node maps to a RequestPort (it suspends for review); the rest map to executors.
        var bindingByNodeId = new Dictionary<string, ExecutorBinding>(workflow.Nodes.Count, StringComparer.Ordinal);
        foreach (var node in workflow.Nodes)
        {
            var config = BuildNodeConfig(node);
            bindingByNodeId[node.Id] = node.NodeType switch
            {
                // The Trigger node marks the entry point and carries no runnable logic — forward the seed
                // straight to its successor (a transform with no mappings is a pass-through).
                WorkflowNodeType.Trigger => new FunctionTransformExecutor(
                    node.Id, config, IsTerminal(node.Id)).BindExecutor(),
                WorkflowNodeType.AgenticReason => new AgenticNodeExecutor(
                    node.Id, chatClient, config, IsTerminal(node.Id)).BindExecutor(),
                WorkflowNodeType.FunctionRoute => new FunctionRouteExecutor(
                    node.Id, chatClient, config.OutputPortLabels, BuildRouteTargets(node, workflow)).BindExecutor(),
                WorkflowNodeType.FunctionTransform => new FunctionTransformExecutor(
                    node.Id, config, IsTerminal(node.Id)).BindExecutor(),
                WorkflowNodeType.FunctionNotify => new FunctionNotifyExecutor(
                    node.Id, config, IsTerminal(node.Id), connectorRepository).BindExecutor(),
                WorkflowNodeType.FunctionData => new FunctionDataExecutor(
                    node.Id, config, IsTerminal(node.Id), connectorRepository).BindExecutor(),
                WorkflowNodeType.HumanApproval => RequestPort
                    .Create<WorkflowStepData, WorkflowStepData>(node.Id).BindAsExecutor(allowWrappedRequests: false),

                _ => throw new InvalidOperationException(
                    $"Node '{node.Id}' has unsupported NodeType '{node.NodeType}' for the MAF runtime."),
            };
        }

        // The first node is the entry point (parity with the SK builder).
        var builder = new WorkflowBuilder(bindingByNodeId[workflow.Nodes[0].Id]);

        foreach (var edge in workflow.Edges)
        {
            builder.AddEdge(bindingByNodeId[edge.SourceNodeId], bindingByNodeId[edge.TargetNodeId]);
        }

        // Terminal executor nodes yield the workflow output. A terminal HumanApproval node is a RequestPort
        // (it suspends rather than yielding), so it is not declared as an output.
        var terminalBindings = workflow.Nodes
            .Where(node => IsTerminal(node.Id) && node.NodeType != WorkflowNodeType.HumanApproval)
            .Select(node => bindingByNodeId[node.Id])
            .ToArray();
        if (terminalBindings.Length > 0)
        {
            builder.WithOutputFrom(terminalBindings);
        }

        // Record each route node's port labels (parity with the SK builder's KnownPortLabels).
        foreach (var routeNode in workflow.Nodes.Where(node => node.NodeType == WorkflowNodeType.FunctionRoute))
        {
            PortLabelsByNodeId[routeNode.Id] = routeNode.OutputPorts.Select(port => port.Label).ToList().AsReadOnly();
        }

        return builder.Build(validateOrphans: true);
    }

    /// <summary>Builds the per-node runtime state: identity, goal, realized config, and output-port labels.</summary>
    private static NodeRuntimeConfig BuildNodeConfig(WorkflowNode node) => new()
    {
        NodeId = node.Id,
        GoalPrompt = node.GoalPrompt,
        FunctionConfig = node.FunctionConfig,
        OutputPortLabels = node.OutputPorts.Select(port => port.Label).ToList(),
    };

    /// <summary>Maps each of a route node's output-port labels to the id of the node its edge leads to.</summary>
    private static IReadOnlyDictionary<string, string> BuildRouteTargets(WorkflowNode node, WorkflowDefinition workflow)
    {
        var targetsByLabel = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var edge in workflow.Edges.Where(edge => edge.SourceNodeId == node.Id))
        {
            var port = node.OutputPorts.FirstOrDefault(outputPort => outputPort.Id == edge.SourcePortId);
            if (port is not null)
            {
                targetsByLabel[port.Label] = edge.TargetNodeId;
            }
        }
        return targetsByLabel;
    }
}
