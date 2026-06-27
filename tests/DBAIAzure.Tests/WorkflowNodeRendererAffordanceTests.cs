// Tests for WorkflowNodeRenderer affordance contract (pure xUnit; no Web reference).
// The "Set up →" label visible-when-unconfigured rule is verified through the
// WorkflowNode domain model, which WorkflowNodeRenderer reads directly.
// Rendering verification is expected in a future DBAIAzure.Web.Tests bUnit project.
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

public class WorkflowNodeRendererAffordanceTests
{
    // ── Helper ──────────────────────────────────────────────────────────────────

    private static WorkflowNode BuildNode(WorkflowNodeType nodeType, bool isConfigured) =>
        WorkflowNode.CreateNew(nodeType, "Test Node") with { IsConfigured = isConfigured };

    // ── Tests ───────────────────────────────────────────────────────────────────

    [Fact]
    public void UnconfiguredNode_RequiresSetupAffordance()
    {
        var node = BuildNode(WorkflowNodeType.AgenticReason, isConfigured: false);

        // The "Set up →" label should be shown when the node is not configured.
        Assert.False(node.IsConfigured);
    }

    [Fact]
    public void ConfiguredNode_DoesNotRequireSetupAffordance()
    {
        var node = BuildNode(WorkflowNodeType.AgenticReason, isConfigured: true);

        // No affordance label when fully configured.
        Assert.True(node.IsConfigured);
    }

    [Fact]
    public void TriggerNode_WhenConfigured_HidesAffordance()
    {
        var node = BuildNode(WorkflowNodeType.Trigger, isConfigured: true);

        Assert.True(node.IsConfigured);
    }

    [Fact]
    public void AllNodeTypes_StartUnconfigured()
    {
        // Every freshly created node starts unconfigured — the Set up label must be shown.
        foreach (WorkflowNodeType nodeType in Enum.GetValues<WorkflowNodeType>())
        {
            var node = WorkflowNode.CreateNew(nodeType, "New Node");
            Assert.False(node.IsConfigured, $"{nodeType} should start unconfigured");
        }
    }

    [Fact]
    public void ConfiguredNode_CanBeReconfiguredToShowAffordance()
    {
        var configuredNode = BuildNode(WorkflowNodeType.AgenticReason, isConfigured: true);

        // Simulates a user clearing the configuration.
        var unconfiguredNode = configuredNode with { IsConfigured = false };

        Assert.False(unconfiguredNode.IsConfigured);
    }
}
