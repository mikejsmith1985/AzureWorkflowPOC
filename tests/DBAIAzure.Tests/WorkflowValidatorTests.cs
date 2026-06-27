// Unit tests for WorkflowValidator — domain-only, no I/O, no mocks needed.

using DBAIAzure.Core.Models;
using DBAIAzure.Core.Validation;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class WorkflowValidatorTests
{
    private static readonly WorkflowValidator Validator = new();

    // ── Helper: build a minimal valid workflow (one Trigger + one connected node) ───

    private static WorkflowDefinition BuildValid()
    {
        var triggerOut = new WorkflowPort("begin", "Begin", PortDirection.Output);
        var triggerNode = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start") with
        {
            OutputPorts  = new[] { triggerOut }.ToList().AsReadOnly(),
            IsConfigured = true,
        };

        var nextIn = new WorkflowPort("in1", "Input", PortDirection.Input);
        var nextNode = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Reason") with
        {
            InputPorts   = new[] { nextIn }.ToList().AsReadOnly(),
            IsConfigured = true,
        };

        var edge = WorkflowEdge.CreateNew(triggerNode.Id, triggerOut.Id, nextNode.Id, nextIn.Id, "Begin");

        return new WorkflowDefinition
        {
            Id             = Guid.NewGuid(),
            Name           = "Test",
            OwnerId        = "user1",
            Nodes          = new[] { triggerNode, nextNode }.ToList().AsReadOnly(),
            Edges          = new[] { edge }.ToList().AsReadOnly(),
            Settings       = new WorkflowSettings(),
            ChatHistory    = Array.Empty<WorkflowChatMessage>(),
            CreatedAt      = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow,
        };
    }

    // ── VAL-001: no trigger ────────────────────────────────────────────────────────

    [Fact]
    public void Empty_node_list_returns_VAL001_message()
    {
        var definition = BuildValid() with
        {
            Nodes = Array.Empty<WorkflowNode>(),
            Edges = Array.Empty<WorkflowEdge>(),
        };

        var messages = Validator.Validate(definition);

        Assert.Contains(messages, m => m.Contains("Add a starting trigger"));
    }

    [Fact]
    public void One_non_trigger_node_returns_VAL001_message()
    {
        var reasonNode = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Reason");
        var definition = BuildValid() with
        {
            Nodes = new[] { reasonNode }.ToList().AsReadOnly(),
            Edges = Array.Empty<WorkflowEdge>(),
        };

        var messages = Validator.Validate(definition);

        Assert.Contains(messages, m => m.Contains("Add a starting trigger"));
    }

    // ── VAL-002: two triggers ──────────────────────────────────────────────────────

    [Fact]
    public void Two_trigger_nodes_returns_VAL002_message()
    {
        var trigger1 = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "T1");
        var trigger2 = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "T2");
        var definition = BuildValid() with
        {
            Nodes = new[] { trigger1, trigger2 }.ToList().AsReadOnly(),
            Edges = Array.Empty<WorkflowEdge>(),
        };

        var messages = Validator.Validate(definition);

        Assert.Contains(messages, m => m.Contains("only one starting trigger"));
    }

    // ── VAL-003: island node ───────────────────────────────────────────────────────

    [Fact]
    public void Trigger_with_unconnected_second_node_returns_VAL003_message()
    {
        var trigger = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start");
        var island  = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Orphan");

        var definition = BuildValid() with
        {
            Nodes = new[] { trigger, island }.ToList().AsReadOnly(),
            Edges = Array.Empty<WorkflowEdge>(),
        };

        var messages = Validator.Validate(definition);

        Assert.Contains(messages, m => m.Contains("not connected to anything"));
    }

    // ── Valid workflow ─────────────────────────────────────────────────────────────

    [Fact]
    public void Valid_one_trigger_two_connected_nodes_returns_empty_list()
    {
        var messages = Validator.Validate(BuildValid());
        Assert.Empty(messages);
    }
}
