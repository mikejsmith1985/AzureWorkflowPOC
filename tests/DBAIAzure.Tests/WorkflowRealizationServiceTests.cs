// Unit tests (T020): the realization service proposes one valid config per node in graph order without
// mutating the workflow, and AcceptProposal applies an accepted config to exactly one node + records
// provenance. Uses a fake structured-completion service so no network call is made (Article V).

using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Web.Services;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class WorkflowRealizationServiceTests
{
    // Canned, snake_case tool responses — one per node type in the test workflow.
    private static readonly Dictionary<string, string> CannedResponses = new()
    {
        ["propose_trigger_config"] = """
            { "initial_data_description": "A new ServiceNow ticket is created.",
              "output_shape": [ { "name": "ticket_id", "kind": "Text" } ] }
            """,
        ["propose_agent_config"] = """
            { "instruction": "Classify the ticket urgency.",
              "output_shape": [ { "name": "urgency", "kind": "Enum", "allowed_values": ["low", "high"] } ],
              "tool_bindings": [] }
            """,
        ["propose_notify_config"] = """
            { "connector": "Teams", "recipient_map": "on-call channel",
              "message_template": "Ticket is urgent." }
            """,
    };

    private static WorkflowRealizationService BuildService(out CannedStructuredCompletionService fakeLlm)
    {
        fakeLlm = new CannedStructuredCompletionService(CannedResponses);
        return new WorkflowRealizationService(fakeLlm, new FakeConnectorConfigRepository());
    }

    [Fact]
    public async Task ProposeAllAsync_returns_one_proposal_per_node_in_graph_order()
    {
        var workflow = RealizationTestWorkflows.TriggerAgentNotify();
        var service  = BuildService(out var fakeLlm);
        var streamed = new List<RealizationProposal>();

        var proposals = await service.ProposeAllAsync(workflow, streamed.Add);

        Assert.Equal(3, proposals.Count);
        // Graph order is trigger → agent → notify, matching the wired edges.
        Assert.Equal(
            new[] { "propose_trigger_config", "propose_agent_config", "propose_notify_config" },
            fakeLlm.ToolCalls);
        // Every node produced a usable proposal.
        Assert.All(proposals, proposal => Assert.Equal(NodeRealizationStatus.Proposed, proposal.Status));
        Assert.All(proposals, proposal => Assert.NotNull(proposal.ProposedConfig));
        // The streamed callback saw the same proposals as the returned list.
        Assert.Equal(proposals.Count, streamed.Count);
    }

    [Fact]
    public async Task ProposeAllAsync_does_not_mutate_the_workflow()
    {
        var workflow = RealizationTestWorkflows.TriggerAgentNotify();
        var service  = BuildService(out _);

        await service.ProposeAllAsync(workflow, _ => { });

        // Proposing is read-only: no node becomes configured and no FunctionConfig is rewritten.
        Assert.All(workflow.Nodes, node => Assert.False(node.IsConfigured));
        Assert.Empty(workflow.Settings.RealizationProvenance);
    }

    [Fact]
    public async Task ProposeNodeAsync_proposes_only_the_requested_node()
    {
        var workflow = RealizationTestWorkflows.TriggerAgentNotify();
        var service  = BuildService(out var fakeLlm);
        var agentId  = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.AgenticReason).Id;

        var proposal = await service.ProposeNodeAsync(workflow, agentId);

        Assert.Equal(agentId, proposal.NodeId);
        Assert.Equal(WorkflowNodeType.AgenticReason, proposal.NodeType);
        Assert.Equal(new[] { "propose_agent_config" }, fakeLlm.ToolCalls);
    }

    [Fact]
    public async Task AcceptProposal_applies_config_to_exactly_one_node_and_records_provenance()
    {
        var workflow = RealizationTestWorkflows.TriggerAgentNotify();
        var service  = BuildService(out _);
        var agentId  = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.AgenticReason).Id;
        var proposal = await service.ProposeNodeAsync(workflow, agentId);

        var accepted = service.AcceptProposal(workflow, proposal, proposal.ProposedConfig!);

        var acceptedAgent = accepted.Nodes.Single(node => node.Id == agentId);
        Assert.True(acceptedAgent.IsConfigured);
        Assert.False(string.IsNullOrWhiteSpace(acceptedAgent.FunctionConfig));
        Assert.True(accepted.Settings.RealizationProvenance.ContainsKey(agentId));

        // Every other node is byte-identical to its original (single-node mutation guarantee).
        foreach (var original in workflow.Nodes.Where(node => node.Id != agentId))
        {
            var unchanged = accepted.Nodes.Single(node => node.Id == original.Id);
            Assert.False(unchanged.IsConfigured);
            Assert.Equal(original.FunctionConfig, unchanged.FunctionConfig);
        }
    }

    [Fact]
    public async Task AcceptProposal_applies_an_edited_config_to_only_the_target_node()
    {
        // US2 edit-then-accept: an edited envelope is what gets persisted, and only on the target node.
        var workflow = RealizationTestWorkflows.TriggerAgentNotify();
        var service  = BuildService(out _);
        var agentId  = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.AgenticReason).Id;
        var proposal = await service.ProposeNodeAsync(workflow, agentId);

        // The user edits the instruction in plain language before accepting.
        var editedJson = NodeConfigSerializer.ToFunctionConfig(
            WorkflowNodeType.AgenticReason,
            new AgentNodeConfig { Instruction = "EDITED_INSTRUCTION", ModelRef = "claude-test-model" });
        var editedEnvelope = NodeConfigSerializer.ReadEnvelope(editedJson)!;

        var accepted = service.AcceptProposal(workflow, proposal, editedEnvelope);

        var restored = NodeConfigSerializer.ReadConfig<AgentNodeConfig>(
            accepted.Nodes.Single(node => node.Id == agentId).FunctionConfig);
        Assert.Equal("EDITED_INSTRUCTION", restored!.Instruction);

        // No other node was touched.
        foreach (var original in workflow.Nodes.Where(node => node.Id != agentId))
            Assert.Equal(original.FunctionConfig,
                accepted.Nodes.Single(node => node.Id == original.Id).FunctionConfig);
    }

    [Fact]
    public async Task AcceptProposal_rejects_an_intrinsically_invalid_config()
    {
        var workflow = RealizationTestWorkflows.TriggerAgentNotify();
        var service  = BuildService(out _);
        var agentId  = workflow.Nodes.Single(node => node.NodeType == WorkflowNodeType.AgenticReason).Id;
        var proposal = await service.ProposeNodeAsync(workflow, agentId);

        // An empty instruction/model is intrinsically invalid and must be refused on acceptance.
        var invalidConfig = NodeConfigSerializer.ToFunctionConfig(
            WorkflowNodeType.AgenticReason, new AgentNodeConfig { Instruction = "", ModelRef = "" });
        var invalidEnvelope = NodeConfigSerializer.ReadEnvelope(invalidConfig)!;

        Assert.Throws<ArgumentException>(() => service.AcceptProposal(workflow, proposal, invalidEnvelope));
    }
}
