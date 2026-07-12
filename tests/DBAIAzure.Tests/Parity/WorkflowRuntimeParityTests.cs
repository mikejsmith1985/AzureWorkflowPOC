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
}
