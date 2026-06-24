// Pure domain tests for the OnParametersSet re-initialisation guard in WorkflowNodeConfigPanel.
// Tests use a minimal stub that mirrors the guard fields and logic, without Blazor rendering.
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Minimal stub mirroring the reset-guard added to WorkflowNodeConfigPanel.razor:
/// _lastInitialisedNodeId, the guard in OnParametersSet(), and the reset in OnCloseAsync().
/// </summary>
public sealed class ConfigPanelResetState
{
    private string? _lastInitialisedNodeId;

    public string GoalPrompt { get; private set; } = string.Empty;
    public string InputLabel { get; private set; } = string.Empty;

    /// <summary>
    /// Mirrors the guarded OnParametersSet(): only resets bound fields when a different node
    /// (or a newly re-opened panel) is presented — preserves in-progress edits otherwise.
    /// </summary>
    public void OnParametersSet(WorkflowNode? node)
    {
        if (node is null) return;
        if (node.Id == _lastInitialisedNodeId) return;  // guard: same node is already open

        GoalPrompt            = node.GoalPrompt  ?? string.Empty;
        InputLabel            = node.InputLabel  ?? string.Empty;
        _lastInitialisedNodeId = node.Id;
    }

    /// <summary>Simulates the user typing into the Goal textarea without triggering a save.</summary>
    public void SimulateUserTyping(string typedGoal) => GoalPrompt = typedGoal;

    /// <summary>
    /// Mirrors OnCloseAsync(): clears _lastInitialisedNodeId so that re-opening the panel
    /// for the same node reinitialises the fields from the saved node record.
    /// </summary>
    public void OnClose() => _lastInitialisedNodeId = null;
}

public class WorkflowNodeConfigPanelResetGuardTests
{
    // ── Core bug regression ────────────────────────────────────────────────────────

    [Fact]
    public void OnParametersSet_SameNode_DoesNotResetInProgressGoal()
    {
        // Bug scenario: user types text, the 200 ms goal-preview debounce fires,
        // parent calls StateHasChanged(), OnParametersSet() runs again with the SAME
        // node parameter — without the guard this would silently discard the typed text.
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Step") with
        {
            GoalPrompt = "original goal",
        };

        var state = new ConfigPanelResetState();
        state.OnParametersSet(node);               // initial open — fields initialised
        state.SimulateUserTyping("typed in progress");
        state.OnParametersSet(node);               // re-render with SAME node — must not reset

        Assert.Equal("typed in progress", state.GoalPrompt);
    }

    // ── Different node opens panel ─────────────────────────────────────────────────

    [Fact]
    public void OnParametersSet_DifferentNode_ResetsFieldsFromNewNode()
    {
        var node1 = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Step 1") with
        {
            GoalPrompt = "goal for node 1",
        };
        var node2 = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Step 2") with
        {
            GoalPrompt = "goal for node 2",
        };

        var state = new ConfigPanelResetState();
        state.OnParametersSet(node1);
        state.SimulateUserTyping("in-progress text");
        state.OnParametersSet(node2);              // different node — guard ALLOWS reset

        Assert.Equal("goal for node 2", state.GoalPrompt);
    }

    // ── Panel close then reopen ────────────────────────────────────────────────────

    [Fact]
    public void OnParametersSet_AfterClose_ReinitialisesFieldsFromSavedNode()
    {
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Step") with
        {
            GoalPrompt = "original goal",
        };

        var state = new ConfigPanelResetState();
        state.OnParametersSet(node);
        state.SimulateUserTyping("in-progress");
        state.OnClose();                           // panel closed → _lastInitialisedNodeId = null
        state.OnParametersSet(node);              // panel reopened → guard now ALLOWS reset

        // After close + reopen, the saved GoalPrompt must be shown (not the abandoned in-progress text).
        Assert.Equal("original goal", state.GoalPrompt);
    }

    // ── Null node is a no-op ──────────────────────────────────────────────────────

    [Fact]
    public void OnParametersSet_NullNode_DoesNothing()
    {
        var state = new ConfigPanelResetState();
        state.OnParametersSet(null);  // must not throw

        Assert.Equal(string.Empty, state.GoalPrompt);
    }

    // ── Undo verification: label undo order ───────────────────────────────────────

    [Fact]
    public void Undo_RestoresLabelInCommitOrder()
    {
        // Mirrors the undo stack behaviour expected by T016:
        // commit "Step A" then "Step B" — Ctrl+Z twice should restore A then original.
        var firstAction  = new LabelRenameActionStub("Original", "Original", "Step A");
        var secondAction = new LabelRenameActionStub("Step A",   "Step A",   "Step B");

        firstAction.Do();
        secondAction.Do();
        Assert.Equal("Step B", secondAction.CurrentLabel);

        secondAction.Undo();
        Assert.Equal("Step A", secondAction.CurrentLabel);

        firstAction.Undo();
        Assert.Equal("Original", firstAction.CurrentLabel);
    }
}
