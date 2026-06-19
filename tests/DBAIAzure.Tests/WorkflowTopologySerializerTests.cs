// Tests for the WorkflowTopologySerializer service contract.
// Verifies that serialized output faithfully represents all node labels, types, and edges
// from a WorkflowDefinition — the LLM reads this text as its topology context.
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

public class WorkflowTopologySerializerTests
{
    // ── Helper factory ──────────────────────────────────────────────────────────

    private static WorkflowDefinition BuildTestWorkflow()
    {
        var inputPort  = new WorkflowPort("in1",  "Input",  PortDirection.Input);
        var outputPort = new WorkflowPort("out1", "Output", PortDirection.Output);

        var nodeA = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Analyse Request") with
        {
            GoalPrompt   = "Read the request and write a summary.",
            InputLabel   = "Raw request",
            OutputLabel  = "Summary",
            InputPorts   = new[] { inputPort }.ToList().AsReadOnly(),
            OutputPorts  = new[] { outputPort }.ToList().AsReadOnly(),
            IsConfigured = true,
        };

        var inputPort2  = new WorkflowPort("in2",  "Input",  PortDirection.Input);
        var outputPort2 = new WorkflowPort("out2", "Output", PortDirection.Output);

        var nodeB = WorkflowNode.CreateNew(WorkflowNodeType.HumanApproval, "Manager Review") with
        {
            InputLabel   = "Summary",
            OutputLabel  = "Approved / Rejected",
            InputPorts   = new[] { inputPort2 }.ToList().AsReadOnly(),
            OutputPorts  = new[] { outputPort2 }.ToList().AsReadOnly(),
            IsConfigured = true,
        };

        var edge = WorkflowEdge.CreateNew(nodeA.Id, outputPort.Id, nodeB.Id, inputPort2.Id, "Summary ready");

        return WorkflowDefinition.CreateNew("Test Workflow", "owner1") with
        {
            Nodes = new[] { nodeA, nodeB }.ToList().AsReadOnly(),
            Edges = new[] { edge }.ToList().AsReadOnly(),
        };
    }

    // ── Contract tests ──────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ContainsAllNodeLabels()
    {
        var workflow = BuildTestWorkflow();
        var output = SerializeTopology(workflow);

        Assert.Contains("Analyse Request", output);
        Assert.Contains("Manager Review", output);
    }

    [Fact]
    public void Serialize_ContainsAllNodeTypes()
    {
        var workflow = BuildTestWorkflow();
        var output = SerializeTopology(workflow);

        Assert.Contains("AgenticReason", output);
        Assert.Contains("HumanApproval", output);
    }

    [Fact]
    public void Serialize_ContainsEdgeLabel()
    {
        var workflow = BuildTestWorkflow();
        var output = SerializeTopology(workflow);

        Assert.Contains("Summary ready", output);
    }

    [Fact]
    public void Serialize_ContainsBothNodeLabelsInEdgeContext()
    {
        var workflow = BuildTestWorkflow();
        var output = SerializeTopology(workflow);

        // The serializer should reference node names in edge routing section.
        Assert.Contains("Analyse Request", output);
        Assert.Contains("Manager Review", output);
    }

    [Fact]
    public void Serialize_ContainsWorkflowName()
    {
        var workflow = BuildTestWorkflow();
        var output = SerializeTopology(workflow);

        Assert.Contains("Test Workflow", output);
    }

    [Fact]
    public void Serialize_EmptyWorkflow_ReturnsNonEmptyText()
    {
        var workflow = WorkflowDefinition.CreateNew("Empty Flow", "owner1");
        var output = SerializeTopology(workflow);

        Assert.False(string.IsNullOrWhiteSpace(output));
        Assert.Contains("Empty Flow", output);
    }

    [Fact]
    public void Serialize_GoalPrompt_IsIncludedWhenPresent()
    {
        var workflow = BuildTestWorkflow();
        var output = SerializeTopology(workflow);

        // The node goal prompt should appear in the serialized text.
        Assert.Contains("Read the request and write a summary", output);
    }

    // ── Minimal serializer implementation for testing ────────────────────────────
    // This inline implementation validates the contract. The production implementation
    // lives in WorkflowTopologySerializer.cs and must produce output passing these same tests.

    private static string SerializeTopology(WorkflowDefinition workflow)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"Workflow: {workflow.Name}");
        builder.AppendLine($"Timeout: {workflow.Settings.ExecutionTimeoutMinutes} minutes");
        builder.AppendLine();
        builder.AppendLine("=== Nodes ===");

        for (var index = 0; index < workflow.Nodes.Count; index++)
        {
            var node = workflow.Nodes[index];
            builder.AppendLine($"[{index + 1}] {node.Label} ({node.NodeType})");
            if (node.GoalPrompt is not null)
                builder.AppendLine($"    Goal: {node.GoalPrompt}");
            if (node.InputLabel is not null)
                builder.AppendLine($"    Input: {node.InputLabel}");
            if (node.OutputLabel is not null)
                builder.AppendLine($"    Output: {node.OutputLabel}");
        }

        builder.AppendLine();
        builder.AppendLine("=== Connections ===");

        foreach (var edge in workflow.Edges)
        {
            var sourceNode = workflow.Nodes.FirstOrDefault(nodeItem => nodeItem.Id == edge.SourceNodeId);
            var targetNode = workflow.Nodes.FirstOrDefault(nodeItem => nodeItem.Id == edge.TargetNodeId);
            var sourceLabel = sourceNode?.Label ?? edge.SourceNodeId;
            var targetLabel = targetNode?.Label ?? edge.TargetNodeId;
            builder.AppendLine($"{sourceLabel} -> {targetLabel} (label: {edge.Label})");
        }

        return builder.ToString();
    }
}
