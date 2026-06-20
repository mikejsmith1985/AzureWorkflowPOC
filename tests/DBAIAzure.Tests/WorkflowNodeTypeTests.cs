// Unit tests for WorkflowNodeType enum and WorkflowNode.CreateNew factory — domain-only, no I/O.

using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class WorkflowNodeTypeTests
{
    [Fact]
    public void Trigger_has_numeric_value_zero()
    {
        Assert.Equal(0, (int)WorkflowNodeType.Trigger);
    }

    [Fact]
    public void Trigger_sorts_before_all_other_node_types()
    {
        var allTypes = Enum.GetValues<WorkflowNodeType>();
        var triggerValue = (int)WorkflowNodeType.Trigger;
        Assert.All(allTypes.Where(t => t != WorkflowNodeType.Trigger),
            otherType => Assert.True(triggerValue < (int)otherType,
                $"Trigger ({triggerValue}) should be less than {otherType} ({(int)otherType})"));
    }

    [Fact]
    public void CreateNew_Trigger_returns_zero_input_ports()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start / Trigger");
        Assert.Empty(node.InputPorts);
    }

    [Fact]
    public void CreateNew_Trigger_returns_one_output_port_labelled_Begin()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start / Trigger");
        Assert.Single(node.OutputPorts);
        Assert.Equal("Begin", node.OutputPorts[0].Label);
    }

    [Fact]
    public void CreateNew_Trigger_output_port_direction_is_Output()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start / Trigger");
        Assert.Equal(PortDirection.Output, node.OutputPorts[0].Direction);
    }

    [Fact]
    public void CreateNew_Trigger_IsConfigured_is_false()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start / Trigger");
        Assert.False(node.IsConfigured);
    }

    [Fact]
    public void CreateNew_Trigger_FunctionConfig_contains_initialDataDescription_key()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start / Trigger");
        Assert.NotNull(node.FunctionConfig);
        Assert.Contains("initialDataDescription", node.FunctionConfig);
    }

    [Fact]
    public void CreateNew_non_Trigger_returns_empty_port_lists()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Reason");
        Assert.Empty(node.InputPorts);
        Assert.Empty(node.OutputPorts);
    }

    [Fact]
    public void CreateNew_assigns_unique_ids_for_each_call()
    {
        var node1 = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "T1");
        var node2 = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "T2");
        Assert.NotEqual(node1.Id, node2.Id);
    }
}
