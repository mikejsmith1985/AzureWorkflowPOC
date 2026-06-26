// Unit tests (T021 + folded T022): the readiness service reports production-ready ONLY when every node
// is realized, intrinsically valid, cross-node consistent (VAL-007), and its connectors are healthy
// (VAL-006). Uses the real structural validator + a fake health checker — no I/O (Article V).

using System.Collections.Generic;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Core.Validation;
using DBAIAzure.Web.Services;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class WorkflowReadinessServiceTests
{
    private static WorkflowReadinessService BuildService(params ConnectorType[] healthyConnectors) =>
        new(new WorkflowValidator(), new FakeConnectorHealthChecker(healthyConnectors));

    /// <summary>Realizes all three nodes of the Trigger→Agent→Notify workflow with valid configs.</summary>
    private static WorkflowDefinition FullyRealized()
    {
        var workflow = RealizationTestWorkflows.TriggerAgentNotify();
        var trigger  = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.Trigger);
        var agent    = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.AgenticReason);
        var notify   = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.FunctionNotify);

        return workflow
            .WithRealizedNode(trigger.Id, WorkflowNodeType.Trigger,
                new TriggerNodeConfig { InitialDataDescription = "A new ServiceNow ticket is created." })
            .WithRealizedNode(agent.Id, WorkflowNodeType.AgenticReason,
                new AgentNodeConfig { Instruction = "Classify urgency.", ModelRef = "claude-test-model" })
            .WithRealizedNode(notify.Id, WorkflowNodeType.FunctionNotify,
                new NotifyNodeConfig { Connector = ConnectorType.Messaging, RecipientMap = "team", MessageTemplate = "urgent" });
    }

    [Fact]
    public async Task Fully_realized_workflow_with_healthy_connectors_is_production_ready()
    {
        var report = await BuildService(ConnectorType.Messaging).EvaluateAsync(FullyRealized());

        Assert.True(report.IsProductionReady);
        Assert.All(report.Nodes, node => Assert.Equal(NodeRealizationStatus.Realized, node.Status));
        Assert.Empty(report.BlockingSummary);
    }

    [Fact]
    public async Task Unrealized_node_keeps_workflow_not_ready_and_reports_draft()
    {
        // Realize only the trigger; the agent and notify nodes stay plain-language drafts.
        var workflow = RealizationTestWorkflows.TriggerAgentNotify();
        var trigger  = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.Trigger);
        var realized = workflow.WithRealizedNode(trigger.Id, WorkflowNodeType.Trigger,
            new TriggerNodeConfig { InitialDataDescription = "A ticket is created." });

        var report = await BuildService(ConnectorType.Messaging).EvaluateAsync(realized);

        Assert.False(report.IsProductionReady);
        Assert.Contains(report.Nodes, node => node.Status == NodeRealizationStatus.Draft);
    }

    [Fact]
    public async Task Node_bound_to_unhealthy_connector_is_blocked()
    {
        // VAL-006: the Notify node binds Teams, but Teams is not healthy → Blocked, workflow not ready.
        var workflow = FullyRealized();
        var notifyId = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.FunctionNotify).Id;

        var report = await BuildService(/* nothing healthy */).EvaluateAsync(workflow);

        Assert.False(report.IsProductionReady);
        Assert.Equal(NodeRealizationStatus.Blocked, report.Nodes.Single(node => node.NodeId == notifyId).Status);
    }

    [Fact]
    public async Task Intrinsically_invalid_config_reports_needs_input()
    {
        // VAL-005: realize the notify node with an empty message → intrinsic failure → NeedsInput.
        var workflow = FullyRealized();
        var notifyId = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.FunctionNotify).Id;
        var broken = workflow.WithRealizedNode(notifyId, WorkflowNodeType.FunctionNotify,
            new NotifyNodeConfig { Connector = ConnectorType.Messaging, RecipientMap = "team", MessageTemplate = "" });

        var report = await BuildService(ConnectorType.Messaging).EvaluateAsync(broken);

        Assert.False(report.IsProductionReady);
        Assert.Equal(NodeRealizationStatus.NeedsInput, report.Nodes.Single(node => node.NodeId == notifyId).Status);
    }

    [Fact]
    public async Task Blocked_node_reason_names_the_missing_connector()
    {
        // T044: the blocking reason surfaced to the user must name the specific connector so they know what
        // to configure — "Messaging" must appear in the reason text, not a generic "a connector is missing".
        var workflow = FullyRealized();
        var notifyId = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.FunctionNotify).Id;

        var report = await BuildService(/* nothing healthy */).EvaluateAsync(workflow);

        var notifyReason = report.Nodes.Single(node => node.NodeId == notifyId).Reasons.Single();
        Assert.Contains("Messaging", notifyReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(notifyReason, report.BlockingSummary);
    }

    [Fact]
    public async Task Realized_node_flips_to_out_of_date_when_its_intent_changes()
    {
        // R6 / Scenario E: a node realized against one intent, then re-worded, is reported out-of-date.
        var workflow = FullyRealized();
        var agentId  = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.AgenticReason).Id;
        var agent    = workflow.Nodes.Single(node => node.Id == agentId);

        // Record provenance matching the node's CURRENT intent, exactly as AcceptProposal would.
        var recordedHash = WorkflowIntentHasher.ComputeIntentHash(agent, workflow);
        var withProvenance = workflow with
        {
            Settings = workflow.Settings with
            {
                RealizationProvenance = new Dictionary<string, string> { [agentId] = recordedHash },
            },
        };

        // Baseline: intent unchanged → the node is Realized, not out-of-date.
        var baseline = await BuildService(ConnectorType.Messaging).EvaluateAsync(withProvenance);
        Assert.Equal(NodeRealizationStatus.Realized, baseline.Nodes.Single(node => node.NodeId == agentId).Status);

        // Re-word the node's goal → its intent hash changes → it must flip to OutOfDate.
        var mutatedNodes = withProvenance.Nodes
            .Select(node => node.Id == agentId ? node with { GoalPrompt = "A completely different goal." } : node)
            .ToList();
        var mutated = withProvenance with { Nodes = mutatedNodes };

        var report = await BuildService(ConnectorType.Messaging).EvaluateAsync(mutated);

        Assert.Equal(NodeRealizationStatus.OutOfDate, report.Nodes.Single(node => node.NodeId == agentId).Status);
        Assert.False(report.IsProductionReady);
    }

    [Fact]
    public async Task Route_with_fewer_conditions_than_outgoing_edges_reports_needs_input()
    {
        // VAL-007 cross-node: a branch with two outgoing edges but only one condition is inconsistent.
        var workflow = BuildRouteWorkflowWithTwoBranches(out var routeId);
        var report   = await BuildService(ConnectorType.Messaging).EvaluateAsync(workflow);

        Assert.False(report.IsProductionReady);
        Assert.Equal(NodeRealizationStatus.NeedsInput, report.Nodes.Single(node => node.NodeId == routeId).Status);
    }

    /// <summary>Trigger → Route, where Route has two outgoing edges but only one realized condition.</summary>
    private static WorkflowDefinition BuildRouteWorkflowWithTwoBranches(out string routeId)
    {
        var triggerOut = new WorkflowPort("begin", "Begin", PortDirection.Output);
        var trigger = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start") with
        {
            OutputPorts = new[] { triggerOut }.ToList().AsReadOnly(),
        };

        var routeIn   = new WorkflowPort("in", "Input", PortDirection.Input);
        var routeHigh = new WorkflowPort("high", "High", PortDirection.Output);
        var routeLow  = new WorkflowPort("low", "Low", PortDirection.Output);
        var route = WorkflowNode.CreateNew(WorkflowNodeType.FunctionRoute, "Branch by urgency") with
        {
            InputPorts  = new[] { routeIn }.ToList().AsReadOnly(),
            OutputPorts = new[] { routeHigh, routeLow }.ToList().AsReadOnly(),
        };
        routeId = route.Id;

        var sinkHighIn = new WorkflowPort("in", "Input", PortDirection.Input);
        var sinkHigh = WorkflowNode.CreateNew(WorkflowNodeType.FunctionNotify, "Page on-call") with
        {
            InputPorts = new[] { sinkHighIn }.ToList().AsReadOnly(),
        };
        var sinkLowIn = new WorkflowPort("in", "Input", PortDirection.Input);
        var sinkLow = WorkflowNode.CreateNew(WorkflowNodeType.FunctionNotify, "Log it") with
        {
            InputPorts = new[] { sinkLowIn }.ToList().AsReadOnly(),
        };

        var edges = new[]
        {
            WorkflowEdge.CreateNew(trigger.Id, triggerOut.Id, route.Id, routeIn.Id, "Begin"),
            WorkflowEdge.CreateNew(route.Id, routeHigh.Id, sinkHigh.Id, sinkHighIn.Id, "High"),
            WorkflowEdge.CreateNew(route.Id, routeLow.Id, sinkLow.Id, sinkLowIn.Id, "Low"),
        };

        var workflow = new WorkflowDefinition
        {
            Id             = Guid.NewGuid(),
            Name           = "Routing",
            OwnerId        = "owner1",
            Nodes          = new[] { trigger, route, sinkHigh, sinkLow }.ToList().AsReadOnly(),
            Edges          = edges.ToList().AsReadOnly(),
            Settings       = new WorkflowSettings(),
            ChatHistory    = Array.Empty<WorkflowChatMessage>(),
            CreatedAt      = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow,
        };

        // Realize every node; the route gets only ONE condition though it has TWO outgoing edges.
        return workflow
            .WithRealizedNode(trigger.Id, WorkflowNodeType.Trigger,
                new TriggerNodeConfig { InitialDataDescription = "A ticket is created." })
            .WithRealizedNode(route.Id, WorkflowNodeType.FunctionRoute,
                new RouteNodeConfig
                {
                    Conditions    = new[] { new RouteCondition("high", "urgency == 'high'") },
                    DefaultPortId = "low",
                })
            .WithRealizedNode(sinkHigh.Id, WorkflowNodeType.FunctionNotify,
                new NotifyNodeConfig { Connector = ConnectorType.Messaging, RecipientMap = "oncall", MessageTemplate = "page" })
            .WithRealizedNode(sinkLow.Id, WorkflowNodeType.FunctionNotify,
                new NotifyNodeConfig { Connector = ConnectorType.Messaging, RecipientMap = "log", MessageTemplate = "logged" });
    }
}
