// Pure domain tests for the inline workflow-name editing behaviour in WorkflowToolbar.
// Tests verify the state machine that governs when edits commit, revert, or are rejected.
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Lightweight state model mirroring the name-edit fields inside WorkflowToolbar.razor,
/// allowing the business rules to be verified without UI infrastructure.
/// </summary>
public sealed class WorkflowNameEditState
{
    public string CurrentName       { get; private set; } = string.Empty;
    public bool   IsEditing         { get; private set; }
    public bool   ShowBlankTooltip  { get; private set; }
    private string _previousName    = string.Empty;

    public void SetInitialName(string name) => CurrentName = name;

    public void StartEdit()
    {
        _previousName  = CurrentName;
        IsEditing      = true;
        ShowBlankTooltip = false;
    }

    /// <summary>
    /// Commit with the supplied value. Rejects blank input by reverting and flashing tooltip.
    /// </summary>
    public void Commit(string enteredValue)
    {
        if (string.IsNullOrWhiteSpace(enteredValue))
        {
            // Blank: revert to previous and flash tooltip.
            CurrentName      = _previousName;
            ShowBlankTooltip = true;
            IsEditing        = false;
            return;
        }

        CurrentName      = enteredValue;
        IsEditing        = false;
        ShowBlankTooltip = false;
    }

    public void Cancel()
    {
        CurrentName      = _previousName;
        IsEditing        = false;
        ShowBlankTooltip = false;
    }
}

public class WorkflowToolbarNameEditTests
{
    [Fact]
    public void StartEdit_TransitionsToEditingState()
    {
        var state = new WorkflowNameEditState();
        state.SetInitialName("My Workflow");

        state.StartEdit();

        Assert.True(state.IsEditing);
        Assert.False(state.ShowBlankTooltip);
    }

    [Fact]
    public void Commit_WithValidName_UpdatesNameAndExitsEdit()
    {
        var state = new WorkflowNameEditState();
        state.SetInitialName("Old Name");
        state.StartEdit();

        state.Commit("New Name");

        Assert.Equal("New Name", state.CurrentName);
        Assert.False(state.IsEditing);
    }

    [Fact]
    public void Commit_WithBlankName_RevertsToOldNameAndShowsTooltip()
    {
        var state = new WorkflowNameEditState();
        state.SetInitialName("Previous Name");
        state.StartEdit();

        state.Commit("   ");

        Assert.Equal("Previous Name", state.CurrentName);
        Assert.False(state.IsEditing, "Edit mode should exit even on blank commit");
        Assert.True(state.ShowBlankTooltip, "Blank tooltip must appear for empty input");
    }

    [Fact]
    public void Cancel_RevertsNameWithoutTooltip()
    {
        var state = new WorkflowNameEditState();
        state.SetInitialName("Stable Name");
        state.StartEdit();

        state.Commit("In Progress");
        state.StartEdit();
        state.Cancel();

        Assert.Equal("In Progress", state.CurrentName);
        Assert.False(state.IsEditing);
        Assert.False(state.ShowBlankTooltip);
    }

    [Fact]
    public void Commit_AfterBlankThenValidName_TooltipClears()
    {
        var state = new WorkflowNameEditState();
        state.SetInitialName("Baseline");
        state.StartEdit();
        state.Commit("   ");    // triggers tooltip
        state.StartEdit();
        state.Commit("Good Name");  // valid — tooltip should clear

        Assert.Equal("Good Name", state.CurrentName);
        Assert.False(state.ShowBlankTooltip);
    }
}
