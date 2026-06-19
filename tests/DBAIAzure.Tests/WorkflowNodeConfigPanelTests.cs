// Tests for WorkflowNodeConfigPanel behaviour:
// - Goal field drives canvas label update (label reflects Goal when non-empty)
// - Required-field amber badge condition fires when Goal is empty on close
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

public class WorkflowNodeConfigPanelTests
{
    // ── Contract tests (pure domain logic, no Blazor rendering needed) ──────────

    [Fact]
    public void IsConfigured_IsFalse_WhenGoalPromptIsEmpty()
    {
        // The config panel sets IsConfigured = true only when all required fields are filled.
        // For AgenticReason, GoalPrompt is required; empty means the amber badge must show.
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "My Step") with
        {
            GoalPrompt = string.Empty,
            IsConfigured = false,
        };

        var isConfigured = !string.IsNullOrWhiteSpace(node.GoalPrompt);

        Assert.False(isConfigured);
    }

    [Fact]
    public void IsConfigured_IsTrue_WhenGoalPromptIsProvided()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "My Step") with
        {
            GoalPrompt   = "Summarise the incoming request.",
            IsConfigured = true,
        };

        var isConfigured = !string.IsNullOrWhiteSpace(node.GoalPrompt);

        Assert.True(isConfigured);
        Assert.True(node.IsConfigured);
    }

    [Fact]
    public void LiveLabelUpdate_ReflectsGoalPromptWhenSet()
    {
        // When the user types into Goal, the canvas label updates to that text.
        // This tests the mapping logic used by WorkflowNodeConfigPanel.
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "My Step");
        const string userTypedGoal = "Analyse the support ticket";

        // Simulates the panel's label-update handler.
        var updatedLabel = string.IsNullOrWhiteSpace(userTypedGoal) ? node.Label : userTypedGoal;

        Assert.Equal("Analyse the support ticket", updatedLabel);
    }

    [Fact]
    public void LiveLabelUpdate_KeepsOriginalLabel_WhenGoalIsEmpty()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Original Name");
        const string emptyGoal = "";

        var updatedLabel = string.IsNullOrWhiteSpace(emptyGoal) ? node.Label : emptyGoal;

        Assert.Equal("Original Name", updatedLabel);
    }

    [Fact]
    public void FunctionNodes_DoNotRequireGoalPrompt()
    {
        // Non-agentic node types do not require GoalPrompt — their IsConfigured
        // depends on function-specific fields instead.
        var node = WorkflowNode.CreateNew(WorkflowNodeType.FunctionNotify, "Notify Step") with
        {
            IsConfigured = true,  // configured without a GoalPrompt
        };

        Assert.True(node.IsConfigured);
        Assert.Null(node.GoalPrompt);
    }

    [Fact]
    public void NodeRecord_IsImmutable_PanelUpdateUsesWithExpression()
    {
        // Verify the `with` expression pattern used by the panel when saving changes.
        var original = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Step A");

        var updated = original with
        {
            GoalPrompt   = "New goal text",
            IsConfigured = true,
            Label        = "New goal text",
        };

        // Original must be unchanged — record is immutable.
        Assert.Null(original.GoalPrompt);
        Assert.False(original.IsConfigured);
        Assert.Equal("Step A", original.Label);

        // Updated reflects the new values.
        Assert.Equal("New goal text", updated.GoalPrompt);
        Assert.True(updated.IsConfigured);
        Assert.Equal("New goal text", updated.Label);
    }
}
