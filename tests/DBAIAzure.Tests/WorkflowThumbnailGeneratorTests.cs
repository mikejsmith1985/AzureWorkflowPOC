// Tests for WorkflowThumbnailGenerator (Core.Services):
// - SVG contains one <rect> element per node
// - Trigger node uses emerald fill (#10b981)
// - Empty workflow returns null
// - null workflow returns null
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Services;
using Xunit;

namespace DBAIAzure.Tests;

public class WorkflowThumbnailGeneratorTests
{
    private readonly WorkflowThumbnailGenerator _generator = new();

    // ── Helper ──────────────────────────────────────────────────────────────────

    private static WorkflowDefinition BuildWorkflow(params WorkflowNode[] nodes)
    {
        var definition = WorkflowDefinition.CreateNew("Test", "owner1");
        return definition with { Nodes = nodes.ToList().AsReadOnly() };
    }

    // ── Tests ───────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateSvg_ReturnsNull_WhenWorkflowHasNoNodes()
    {
        var emptyWorkflow = BuildWorkflow();

        var svg = _generator.GenerateSvg(emptyWorkflow);

        Assert.Null(svg);
    }

    [Fact]
    public void GenerateSvg_ReturnsNull_WhenWorkflowIsNull()
    {
        var svg = _generator.GenerateSvg(null!);

        Assert.Null(svg);
    }

    [Fact]
    public void GenerateSvg_ContainsOneRect_PerNode()
    {
        var node1 = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Step A");
        var node2 = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Step B");
        var workflow = BuildWorkflow(node1, node2);

        var svg = _generator.GenerateSvg(workflow);

        Assert.NotNull(svg);
        var rectCount = CountOccurrences(svg, "<rect ");
        Assert.Equal(2, rectCount);
    }

    [Fact]
    public void GenerateSvg_UsesEmeraldFill_ForTriggerNode()
    {
        var triggerNode = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start");
        var workflow = BuildWorkflow(triggerNode);

        var svg = _generator.GenerateSvg(workflow);

        Assert.NotNull(svg);
        Assert.Contains("#10b981", svg);
    }

    [Fact]
    public void GenerateSvg_ReturnsSvgElement_WithViewBox()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Step");
        var workflow = BuildWorkflow(node);

        var svg = _generator.GenerateSvg(workflow);

        Assert.NotNull(svg);
        Assert.StartsWith("<svg", svg);
        Assert.Contains("viewBox", svg);
    }

    [Fact]
    public void GenerateSvg_RendersAllNodes_UpToMaximumCap()
    {
        // Generator caps at MaxNodes = 50; all 5 should render.
        var nodes = Enumerable.Range(1, 5)
            .Select(nodeIndex => WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, $"Node {nodeIndex}"))
            .ToArray();
        var workflow = BuildWorkflow(nodes);

        var svg = _generator.GenerateSvg(workflow);

        Assert.NotNull(svg);
        Assert.Equal(5, CountOccurrences(svg, "<rect "));
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var searchOffset = 0;
        while ((searchOffset = haystack.IndexOf(needle, searchOffset, StringComparison.Ordinal)) != -1)
        {
            count++;
            searchOffset += needle.Length;
        }
        return count;
    }
}
