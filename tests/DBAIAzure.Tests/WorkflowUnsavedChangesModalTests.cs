// Pure domain tests for the unsaved-changes navigation guard state machine.
// The modal offers three choices: Save, Discard, and Cancel — each drives a
// distinct branch of the dirty-flag lifecycle in WorkflowBuilder.razor.
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Captures the state the guard must track so the WorkflowBuilder page can decide
/// whether to save, abandon, or keep the current session after a navigation attempt.
/// </summary>
public sealed class WorkflowUnsavedChangesGuardState
{
    public bool HasUnsavedChanges { get; private set; }
    public bool IsModalOpen       { get; private set; }

    public void MarkDirty()    => HasUnsavedChanges = true;
    public void OpenModal()    => IsModalOpen = true;
    public void CloseModal()   => IsModalOpen = false;

    /// <summary>
    /// "Save &amp; Continue" — save was invoked externally by the caller; our job is
    /// to clear the dirty flag and close the modal so navigation can proceed.
    /// </summary>
    public void AfterSave()
    {
        HasUnsavedChanges = false;
        IsModalOpen       = false;
    }

    /// <summary>
    /// "Discard Changes" — abandon edits and close so navigation can proceed.
    /// </summary>
    public void Discard()
    {
        HasUnsavedChanges = false;
        IsModalOpen       = false;
    }

    /// <summary>
    /// "Cancel" — the user changed their mind; leave the dirty flag intact and close only the modal.
    /// </summary>
    public void Cancel() => IsModalOpen = false;
}

public class WorkflowUnsavedChangesModalTests
{
    [Fact]
    public void ModalOpensOnlyWhenDirty()
    {
        var guard = new WorkflowUnsavedChangesGuardState();

        // No dirty changes yet — modal should not be opened automatically.
        Assert.False(guard.HasUnsavedChanges);
        Assert.False(guard.IsModalOpen);

        guard.MarkDirty();
        guard.OpenModal();

        Assert.True(guard.HasUnsavedChanges);
        Assert.True(guard.IsModalOpen);
    }

    [Fact]
    public void AfterSave_ClearsDirtyFlagAndClosesModal()
    {
        var guard = new WorkflowUnsavedChangesGuardState();
        guard.MarkDirty();
        guard.OpenModal();

        guard.AfterSave();

        Assert.False(guard.HasUnsavedChanges, "Dirty flag must be cleared after saving");
        Assert.False(guard.IsModalOpen,        "Modal must close after saving");
    }

    [Fact]
    public void Discard_ClearsDirtyFlagAndClosesModal()
    {
        var guard = new WorkflowUnsavedChangesGuardState();
        guard.MarkDirty();
        guard.OpenModal();

        guard.Discard();

        Assert.False(guard.HasUnsavedChanges, "Dirty flag must be cleared after discarding");
        Assert.False(guard.IsModalOpen,        "Modal must close after discarding");
    }

    [Fact]
    public void Cancel_KeepsDirtyFlagAndClosesModal()
    {
        var guard = new WorkflowUnsavedChangesGuardState();
        guard.MarkDirty();
        guard.OpenModal();

        guard.Cancel();

        Assert.True(guard.HasUnsavedChanges, "Dirty flag must remain set when user cancels");
        Assert.False(guard.IsModalOpen,       "Modal itself must close on cancel");
    }

    [Fact]
    public void MultipleMarkDirtyCalls_RemainsConsistent()
    {
        var guard = new WorkflowUnsavedChangesGuardState();

        guard.MarkDirty();
        guard.MarkDirty();
        guard.MarkDirty();

        Assert.True(guard.HasUnsavedChanges);
        Assert.False(guard.IsModalOpen);
    }
}
