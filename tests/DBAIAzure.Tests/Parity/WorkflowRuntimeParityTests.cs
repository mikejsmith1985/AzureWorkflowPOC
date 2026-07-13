// Parity test for the visual (runtime) workflow migrated onto MAF Workflows (spec-019 T016). Asserts a
// canvas WorkflowDefinition translates to a MAF graph whose FunctionRoute node routes by the chosen port
// label via a conditional edge. FAILING first (Red): MafWorkflowRuntimeFactory.Build is not yet built.
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Xunit;

namespace DBAIAzure.Tests.Parity;

/// <summary>
/// Framework-equivalence test for the visual workflow runtime. Baseline (SK) behaviour: one step per
/// node, edges as event routes, and a FunctionRoute node that emits the chosen port label to select the
/// next step. The migrated MAF runtime must map node → executor (executor id == node id) and route a
/// FunctionRoute node's decision along the matching <b>conditional edge</b> (there is no <c>AddSwitch</c>
/// in the GA API — routing is a labelled conditional edge). Model output is pinned by
/// <see cref="RecordedChatClient"/> so the route decision is deterministic.
/// </summary>
[Trait("Category", "US1Parity")]
public sealed class WorkflowRuntimeParityTests
{
    [Fact]
    public async Task RouteNode_RoutesAlongChosenPortLabel()
    {
        // A router with two labelled output ports; the pinned model chooses "approve".
        var approvePort = new WorkflowPort("p-approve", "approve", PortDirection.Output);
        var rejectPort = new WorkflowPort("p-reject", "reject", PortDirection.Output);

        var router = WorkflowNode.CreateNew(WorkflowNodeType.FunctionRoute, "Router") with
        {
            GoalPrompt = "Route the item to approve or reject.",
            IsConfigured = true,
            OutputPorts = new[] { approvePort, rejectPort }.ToList().AsReadOnly(),
        };
        var approveNode = WorkflowNode.CreateNew(WorkflowNodeType.FunctionNotify, "Approve") with { IsConfigured = true };
        var rejectNode = WorkflowNode.CreateNew(WorkflowNodeType.FunctionNotify, "Reject") with { IsConfigured = true };

        var approveEdge = WorkflowEdge.CreateNew(router.Id, approvePort.Id, approveNode.Id, "in", "edge-approve");
        var rejectEdge = WorkflowEdge.CreateNew(router.Id, rejectPort.Id, rejectNode.Id, "in", "edge-reject");

        var definition = new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Route Parity",
            OwnerId = "owner1",
            Nodes = new[] { router, approveNode, rejectNode }.ToList().AsReadOnly(),
            Edges = new[] { approveEdge, rejectEdge }.ToList().AsReadOnly(),
            Settings = new WorkflowSettings(),
            ChatHistory = Array.Empty<WorkflowChatMessage>(),
            CreatedAt = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow,
        };

        // The route executor parses a schema-bound RouteDecision, so the pinned model returns that JSON.
        var chatClient = new RecordedChatClient(
            RecordedTurn.With("{\"SelectedPortLabel\":\"approve\"}", inputTokens: 30, outputTokens: 6));

        var seed = new WorkflowStepData { RunId = "run-1", NodeId = router.Id, InputPayload = "the item to route" };

        var factory = new MafWorkflowRuntimeFactory();
        var workflow = factory.Build(definition, chatClient);
        var observation = await MafWorkflowRunner.RunAsync(workflow, seed);

        // The router runs, then only the "approve" branch — the "reject" node is never invoked.
        Assert.Equal(new[] { router.Id, approveNode.Id }, observation.ExecutorSequence);
        Assert.DoesNotContain(rejectNode.Id, observation.ExecutorSequence);

        // The route node's port labels are captured for decision validation (parity with the SK builder).
        Assert.Equal(new[] { "approve", "reject" }, factory.PortLabelsByNodeId[router.Id]);
    }

    // spec-019 T018: a chain of the remaining node types (Agentic → Transform → Data → Notify) runs to
    // completion on MAF — one executor per node, the terminal node yielding the run's output.
    [Fact]
    public async Task NodeChain_AgenticTransformDataNotify_RunsToCompletion()
    {
        var agentic = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Reason") with { GoalPrompt = "Summarise.", IsConfigured = true };
        var transform = WorkflowNode.CreateNew(WorkflowNodeType.FunctionTransform, "Reshape") with { IsConfigured = true };
        var data = WorkflowNode.CreateNew(WorkflowNodeType.FunctionData, "Store") with { IsConfigured = true };
        var notify = WorkflowNode.CreateNew(WorkflowNodeType.FunctionNotify, "Notify") with { IsConfigured = true };

        var edges = new[]
        {
            WorkflowEdge.CreateNew(agentic.Id, "out", transform.Id, "in", "e1"),
            WorkflowEdge.CreateNew(transform.Id, "out", data.Id, "in", "e2"),
            WorkflowEdge.CreateNew(data.Id, "out", notify.Id, "in", "e3"),
        };

        var definition = new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Node Chain",
            OwnerId = "owner1",
            Nodes = new[] { agentic, transform, data, notify }.ToList().AsReadOnly(),
            Edges = edges.ToList().AsReadOnly(),
            Settings = new WorkflowSettings(),
            ChatHistory = Array.Empty<WorkflowChatMessage>(),
            CreatedAt = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow,
        };

        // The single model call is the agentic node; the other nodes are deterministic pass-throughs.
        var chatClient = new RecordedChatClient(RecordedTurn.With("a concise summary", inputTokens: 20, outputTokens: 6));
        var seed = new WorkflowStepData { RunId = "run-2", NodeId = agentic.Id, InputPayload = "raw input" };

        var workflow = new MafWorkflowRuntimeFactory().Build(definition, chatClient);
        var observation = await MafWorkflowRunner.RunAsync(workflow, seed);

        Assert.Equal(new[] { agentic.Id, transform.Id, data.Id, notify.Id }, observation.ExecutorSequence);
        Assert.Single(observation.Outputs); // the terminal notify node yielded the run output
    }
}
