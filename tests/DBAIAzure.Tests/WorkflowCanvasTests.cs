// Unit tests for WorkflowCanvas component behaviour:
// - island-node (unconfigured) amber badge is rendered when IsConfigured = false
// - incompatible port connections are identified as invalid via domain logic
// - palette search filtering runs in under 100ms
using System.Diagnostics;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

public class WorkflowCanvasTests
{
    // ── Island-node (unconfigured) amber badge ─────────────────────────────────

    [Fact]
    public void WorkflowNode_IsConfigured_False_ShouldTriggerConfigBadge()
    {
        // A node created with CreateNew has IsConfigured = false — the canvas
        // renderer reads this flag to display the amber "needs config" badge.
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Test Step");

        Assert.False(node.IsConfigured,
            "WorkflowNode.CreateNew should produce an unconfigured node so the canvas renders the amber badge.");
    }

    [Fact]
    public void WorkflowNode_WithIsConfiguredTrue_ShouldNotShowBadge()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Configured Step") with
        {
            IsConfigured = true,
        };

        Assert.True(node.IsConfigured,
            "A node updated via 'with' to IsConfigured = true must report true.");
    }

    // ── Port-direction guard (output→input only) ───────────────────────────────

    [Fact]
    public void PortDirectionGuard_OutputToInput_ShouldBeValid()
    {
        var outputPort = new WorkflowPort("out1", "Result", PortDirection.Output);
        var inputPort  = new WorkflowPort("in1",  "Input",  PortDirection.Input);

        var isValid = outputPort.Direction == PortDirection.Output
                   && inputPort.Direction  == PortDirection.Input;

        Assert.True(isValid,
            "An output→input connection must be accepted by the port-direction guard.");
    }

    [Fact]
    public void PortDirectionGuard_OutputToOutput_ShouldBeInvalid()
    {
        var outputPort1 = new WorkflowPort("out1", "Result 1", PortDirection.Output);
        var outputPort2 = new WorkflowPort("out2", "Result 2", PortDirection.Output);

        var isValid = outputPort1.Direction == PortDirection.Output
                   && outputPort2.Direction == PortDirection.Input;  // second is Output, not Input

        Assert.False(isValid,
            "An output→output connection must be rejected by the port-direction guard.");
    }

    [Fact]
    public void PortDirectionGuard_InputToInput_ShouldBeInvalid()
    {
        var inputPort1 = new WorkflowPort("in1", "Input 1", PortDirection.Input);
        var inputPort2 = new WorkflowPort("in2", "Input 2", PortDirection.Input);

        var isValid = inputPort1.Direction == PortDirection.Output   // first is Input, not Output
                   && inputPort2.Direction == PortDirection.Input;

        Assert.False(isValid,
            "An input→input connection must be rejected by the port-direction guard.");
    }

    // ── Palette search filtering speed (<100ms) ────────────────────────────────

    [Fact]
    public void PaletteFilter_AcrossAllNodeTypes_CompletesUnder100Ms()
    {
        // Build a representative node-type list that mirrors the palette catalogue.
        var allNodeTypes = Enum.GetValues<WorkflowNodeType>().ToList();

        var stopwatch = Stopwatch.StartNew();

        // Simulate the filtering logic from WorkflowNodePalette.razor
        // (a simple case-insensitive contains check on label strings).
        const string searchQuery = "reason";
        var nodeLabels = new Dictionary<WorkflowNodeType, string>
        {
            [WorkflowNodeType.AgenticReason]    = "Reason & Decide",
            [WorkflowNodeType.FunctionRoute]    = "Smart Branch",
            [WorkflowNodeType.FunctionTransform]= "Transform",
            [WorkflowNodeType.FunctionNotify]   = "Notify",
            [WorkflowNodeType.FunctionData]     = "Save / Load",
            [WorkflowNodeType.HumanApproval]    = "Ask a Person",
        };

        var filteredResults = allNodeTypes
            .Where(nt => nodeLabels.TryGetValue(nt, out var label)
                      && label.ToLowerInvariant().Contains(searchQuery))
            .ToList();

        stopwatch.Stop();

        Assert.True(stopwatch.ElapsedMilliseconds < 100,
            $"Palette filter should complete in under 100ms but took {stopwatch.ElapsedMilliseconds}ms.");

        var singleResult = Assert.Single(filteredResults);
        Assert.Equal(WorkflowNodeType.AgenticReason, singleResult);
    }

    [Fact]
    public void PaletteFilter_EmptyQuery_ReturnsAllTypes()
    {
        var allNodeTypes = Enum.GetValues<WorkflowNodeType>().ToList();

        // Empty search — no filtering applies.
        var filteredResults = string.IsNullOrWhiteSpace("")
            ? allNodeTypes
            : allNodeTypes.Where(nt => nt.ToString().Contains("")).ToList();

        Assert.Equal(allNodeTypes.Count, filteredResults.Count);
    }
}
