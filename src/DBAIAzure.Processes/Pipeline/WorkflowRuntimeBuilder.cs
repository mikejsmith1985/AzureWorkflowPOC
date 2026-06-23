// Translates a persisted WorkflowDefinition into a live KernelProcess by mapping each
// canvas node to a typed KernelProcessStep and each canvas edge to an OnEvent wiring call.
#pragma warning disable SKEXP0080

using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Steps;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// Translates a <see cref="WorkflowDefinition"/> into a live <see cref="KernelProcess"/>
/// using SK ProcessBuilder. One step per node; edges become OnEvent wiring.
/// All orchestration state is owned by the SK Process Framework — no custom state machine.
/// </summary>
public sealed class WorkflowRuntimeBuilder
{
    /// <summary>
    /// Port labels keyed by node ID for every <see cref="WorkflowNodeType.FunctionRoute"/> node
    /// in the last <see cref="Build"/> call. The orchestrator uses this map to set
    /// <see cref="FunctionRouteStep.KnownPortLabels"/> on each route step after the process
    /// is created but before execution begins, ensuring the LLM choice is validated against
    /// the exact set of ports the user configured on the canvas.
    /// </summary>
    public Dictionary<string, IReadOnlyList<string>> PortLabelsByNodeId { get; } = [];

    /// <summary>
    /// Builds a <see cref="KernelProcess"/> from the given <paramref name="workflow"/> by
    /// registering one SK process step per canvas node and wiring directed edges as
    /// OnEvent → SendEventTo routes. The first node in <see cref="WorkflowDefinition.Nodes"/>
    /// is designated the entry point and receives the <see cref="WorkflowNodeEvents.NodeStart"/>
    /// external event. HumanApproval nodes additionally get a proxy step that forwards the
    /// <see cref="WorkflowNodeEvents.AwaitHumanApproval"/> event through
    /// <see cref="IExternalKernelProcessMessageChannel"/> so the host runner can detect suspension.
    /// </summary>
    /// <param name="workflow">
    /// The immutable workflow definition authored in the visual builder. Must contain at
    /// least one node; an empty <see cref="WorkflowDefinition.Nodes"/> list throws
    /// <see cref="InvalidOperationException"/> because there is no entry point to wire.
    /// </param>
    /// <returns>
    /// A fully-wired <see cref="KernelProcess"/> ready to be started via
    /// <c>kernel.CreateProcessAsync(…).RunToEndAsync(…)</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="workflow"/> contains no nodes, when a node's
    /// <see cref="WorkflowNode.NodeType"/> is not a recognised <see cref="WorkflowNodeType"/>,
    /// or when an edge references a node ID that was not registered.
    /// </exception>
    public KernelProcess Build(WorkflowDefinition workflow)
    {
        if (workflow.Nodes.Count == 0)
            throw new InvalidOperationException(
                $"WorkflowDefinition '{workflow.Id}' has no nodes and cannot be executed.");

        // Clear any port labels from a prior Build call so this instance stays reusable.
        PortLabelsByNodeId.Clear();

        var builder = new ProcessBuilder("WorkflowProcess_" + workflow.Id);

        // ── Step 1: Register one process step per canvas node ──────────────────────
        // Keep a builder reference keyed by node ID so the edge-wiring pass can
        // look up source and target steps without iterating the node list again.
        var stepBuilderByNodeId = new Dictionary<string, ProcessStepBuilder>(
            capacity: workflow.Nodes.Count);

        foreach (var node in workflow.Nodes)
        {
            // Stateful steps receive their node's realized config as SK step state so each instance
            // executes from its own configuration (Article VII — framework-native per-step state).
            var stepBuilder = node.NodeType switch
            {
                WorkflowNodeType.AgenticReason    => builder.AddStepFromType<AgenticNodeStep, NodeRuntimeConfig>(BuildNodeState(node)),
                WorkflowNodeType.FunctionRoute    => builder.AddStepFromType<FunctionRouteStep, NodeRuntimeConfig>(BuildNodeState(node)),
                WorkflowNodeType.FunctionTransform => builder.AddStepFromType<FunctionTransformStep, NodeRuntimeConfig>(BuildNodeState(node)),
                WorkflowNodeType.FunctionNotify   => builder.AddStepFromType<FunctionNotifyStep, NodeRuntimeConfig>(BuildNodeState(node)),
                WorkflowNodeType.FunctionData     => builder.AddStepFromType<FunctionDataStep, NodeRuntimeConfig>(BuildNodeState(node)),
                WorkflowNodeType.HumanApproval    => builder.AddStepFromType<HumanApprovalStep, NodeRuntimeConfig>(BuildNodeState(node)),

                // Guard: the enum may grow in future. Fail fast rather than silently skip so
                // the caller knows the builder needs updating when a new node type is added.
                _ => throw new InvalidOperationException(
                    $"Node '{node.Id}' has unrecognised NodeType '{node.NodeType}'. " +
                    "Add a case to WorkflowRuntimeBuilder.Build() for the new type.")
            };

            stepBuilderByNodeId[node.Id] = stepBuilder;
        }

        // ── Step 2: Wire the process entry point ────────────────────────────────────
        // The first node is the canonical entry point. External callers start the process
        // by sending WorkflowNodeEvents.NodeStart, which the framework delivers here.
        var firstNode = workflow.Nodes[0];
        var firstStep = stepBuilderByNodeId[firstNode.Id];

        builder
            .OnInputEvent(WorkflowNodeEvents.NodeStart)
            .SendEventTo(new ProcessFunctionTargetBuilder(firstStep));

        // ── Step 3: Wire HumanApproval proxy steps ──────────────────────────────────
        // Each HumanApproval node needs a companion proxy step that surfaces the
        // AwaitHumanApproval event outside the process boundary (same pattern as
        // hitlProxy in IntakePipelineBuilder). The proxy name is scoped to the node ID
        // so multiple approval gates in one workflow each have a distinct proxy.
        var humanApprovalNodes = workflow.Nodes
            .Where(node => node.NodeType == WorkflowNodeType.HumanApproval)
            .ToList();

        var proxyByNodeId = new Dictionary<string, ProcessStepBuilder>(
            capacity: humanApprovalNodes.Count);

        foreach (var approvalNode in humanApprovalNodes)
        {
            var proxyName = $"hitl_proxy_{approvalNode.Id}";

            // The proxy forwards AwaitHumanApproval out; HumanApprovalReceived is the
            // resume event injected externally — it is NOT a proxy output, so the
            // outgoing-events list is empty.
            var proxyStep = builder.AddProxyStep(
                proxyName,
                [WorkflowNodeEvents.AwaitHumanApproval],
                []);

            proxyByNodeId[approvalNode.Id] = proxyStep;

            // Wire the approval node's suspend event through the proxy so the host runner
            // sees it via IExternalKernelProcessMessageChannel and can park the run for review.
            var approvalStepBuilder = stepBuilderByNodeId[approvalNode.Id];
            approvalStepBuilder
                .OnEvent(Events.AwaitHumanApproval)
                .EmitExternalEvent(proxyStep, WorkflowNodeEvents.AwaitHumanApproval);

            // Wire the external resume event back into this node so the orchestrator can
            // restart the run at the approval step rather than re-running the whole pipeline.
            builder
                .OnInputEvent(WorkflowNodeEvents.HumanApprovalReceived)
                .SendEventTo(new ProcessFunctionTargetBuilder(approvalStepBuilder));
        }

        // ── Step 4: Wire canvas edges ────────────────────────────────────────────────
        // Each WorkflowEdge maps to: sourceStep.OnEvent(NodeCompleted) → targetStep.
        // FunctionRouteStep emits the chosen port label as the event ID rather than
        // NodeCompleted, so edges from route nodes use the source port label as the event.
        foreach (var edge in workflow.Edges)
        {
            if (!stepBuilderByNodeId.TryGetValue(edge.SourceNodeId, out var sourceStep))
                throw new InvalidOperationException(
                    $"Edge '{edge.Id}' references source node '{edge.SourceNodeId}' which is not " +
                    "registered in this workflow. Ensure every edge's SourceNodeId matches a node ID.");

            if (!stepBuilderByNodeId.TryGetValue(edge.TargetNodeId, out var targetStep))
                throw new InvalidOperationException(
                    $"Edge '{edge.Id}' references target node '{edge.TargetNodeId}' which is not " +
                    "registered in this workflow. Ensure every edge's TargetNodeId matches a node ID.");

            // Resolve the source node so we can check its type.
            var sourceNode = workflow.Nodes.First(node => node.Id == edge.SourceNodeId);

            // FunctionRouteStep emits the selected port label as the event ID, so each
            // outgoing edge from a route node is keyed by its source port's label rather
            // than the generic NodeCompleted constant. Every other node type emits NodeCompleted.
            var eventId = sourceNode.NodeType == WorkflowNodeType.FunctionRoute
                ? ResolvePortLabel(sourceNode, edge.SourcePortId)
                : WorkflowNodeEvents.NodeCompleted;

            sourceStep
                .OnEvent(eventId)
                .SendEventTo(new ProcessFunctionTargetBuilder(targetStep));
        }

        // ── Step 5: Store FunctionRouteStep port labels in the definition ────────────
        // ProcessBuilder does not support injecting arbitrary property values before build
        // time — KnownPortLabels is a settable property resolved at runtime. The orchestrator
        // that starts the process (PipelineOrchestrator) is responsible for setting
        // KnownPortLabels on each FunctionRouteStep after KernelProcess is created and
        // before RunToEndAsync is called, using the WorkflowDefinition.Nodes collection
        // to look up the port labels by node ID.
        //
        // The _portLabelsByNodeId dictionary is exposed here so the orchestrator does not
        // need to re-walk the definition to compute the same mapping.
        foreach (var routeNode in workflow.Nodes.Where(node => node.NodeType == WorkflowNodeType.FunctionRoute))
        {
            PortLabelsByNodeId[routeNode.Id] = routeNode.OutputPorts
                .Select(port => port.Label)
                .ToList()
                .AsReadOnly();
        }

        return builder.Build();
    }

    // ── Private helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the per-node runtime state handed to a stateful step: its id, plain-language goal,
    /// realized configuration envelope, and output-port labels (used by branch steps for routing).
    /// </summary>
    private static NodeRuntimeConfig BuildNodeState(WorkflowNode node) => new()
    {
        NodeId           = node.Id,
        GoalPrompt       = node.GoalPrompt,
        FunctionConfig   = node.FunctionConfig,
        OutputPortLabels = node.OutputPorts.Select(port => port.Label).ToList(),
    };

    /// <summary>
    /// Resolves the human-readable label for the output port identified by
    /// <paramref name="portId"/> on <paramref name="node"/>. The label is used as the
    /// event ID emitted by <see cref="FunctionRouteStep"/> and as the OnEvent key in
    /// the corresponding edge wiring, so the two must always agree.
    /// </summary>
    /// <param name="node">The route node whose output ports are searched.</param>
    /// <param name="portId">The stable port ID recorded in the edge definition.</param>
    /// <returns>The label string for the matched port.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="portId"/> does not match any output port on <paramref name="node"/>.
    /// </exception>
    private static string ResolvePortLabel(WorkflowNode node, string portId)
    {
        var port = node.OutputPorts.FirstOrDefault(outputPort => outputPort.Id == portId);

        if (port is null)
            throw new InvalidOperationException(
                $"Node '{node.Id}' has no output port with ID '{portId}'. " +
                "Ensure edge SourcePortId values align with the node's OutputPorts collection.");

        return port.Label;
    }
}
