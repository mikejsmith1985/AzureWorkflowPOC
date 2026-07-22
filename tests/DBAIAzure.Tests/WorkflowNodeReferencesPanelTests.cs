// bUnit tests for the References section of WorkflowNodeConfigPanel (node-based config, step 1).
// Proves every node type can attach typed references, that existing references pre-populate on open,
// and that saving merges them into the node's FunctionConfig blob via NodeReferenceConfig.
using Bunit;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Components.WorkflowBuilder;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Component tests for the per-node References editor. References are owned by the node they are attached
/// to (not a standalone canvas node), so these tests pin that the section renders for a normal node, that
/// rows can be added, that a node opened with stored references shows them, and that Done persists them
/// into the node's configuration where the runtime will later read them.
/// </summary>
public sealed class WorkflowNodeReferencesPanelTests : TestContext
{
    public WorkflowNodeReferencesPanelTests()
    {
        // The panel focuses its first field via JS on open; loose mode lets that no-op under test.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Panel_RendersReferencesSection_WithEmptyState()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Review");

        var cut = RenderComponent<WorkflowNodeConfigPanel>(parameters => parameters
            .Add(panel => panel.Node, node)
            .Add(panel => panel.IsOpen, true));

        Assert.NotNull(cut.Find("[data-testid=\"node-references-section\"]"));
        Assert.Contains("No references attached.", cut.Markup);
    }

    [Fact]
    public void AddReference_AddsAnEditableRow()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Review");

        var cut = RenderComponent<WorkflowNodeConfigPanel>(parameters => parameters
            .Add(panel => panel.Node, node)
            .Add(panel => panel.IsOpen, true));

        Assert.Empty(cut.FindAll("[data-testid=\"reference-row\"]"));
        cut.Find("[data-testid=\"add-reference\"]").Click();
        Assert.Single(cut.FindAll("[data-testid=\"reference-row\"]"));
    }

    [Fact]
    public void Panel_PrePopulatesExistingReferences()
    {
        var config = NodeReferenceConfig.Write(null, new[]
        {
            new NodeReference { Type = NodeReferenceType.Document, Name = "DoR", Value = "# Definition of Ready" },
        });
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Review") with { FunctionConfig = config };

        var cut = RenderComponent<WorkflowNodeConfigPanel>(parameters => parameters
            .Add(panel => panel.Node, node)
            .Add(panel => panel.IsOpen, true));

        Assert.Single(cut.FindAll("[data-testid=\"reference-row\"]"));
        Assert.Equal("DoR", cut.Find("[data-testid=\"reference-name\"]").GetAttribute("value"));
    }

    [Fact]
    public void Save_WithReference_EmitsNodeWithReferenceInFunctionConfig()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.FunctionNotify, "Notify");
        WorkflowNode? saved = null;

        var cut = RenderComponent<WorkflowNodeConfigPanel>(parameters => parameters
            .Add(panel => panel.Node, node)
            .Add(panel => panel.IsOpen, true)
            .Add(panel => panel.NodeUpdated, updated => saved = updated));

        cut.Find("[data-testid=\"add-reference\"]").Click();
        cut.Find("[data-testid=\"reference-name\"]").Input("Definition of Ready");
        cut.Find("[data-testid=\"reference-value\"]").Input("# DoR");
        cut.Find("button[aria-label=\"Confirm node configuration and close panel\"]").Click();

        Assert.NotNull(saved);
        var references = NodeReferenceConfig.Read(saved!.FunctionConfig);
        Assert.Single(references);
        Assert.Equal("Definition of Ready", references[0].Name);
        Assert.Equal(NodeReferenceType.Document, references[0].Type);
        Assert.Equal("# DoR", references[0].Value);
    }
}
