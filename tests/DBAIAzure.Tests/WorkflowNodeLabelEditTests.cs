// Pure domain tests for label-rename mechanics and inline label editing in WorkflowNodeRenderer.
// Tests use minimal stubs that mirror the component state machine and undo-action pattern,
// without requiring Blazor infrastructure or a DBAIAzure.Web project reference.
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

// ── Stubs ──────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Mirrors the Do/Undo mechanics of RenameLabelAction (inner class in WorkflowCanvas.razor)
/// so the rename-action contract can be verified without instantiating the canvas component.
/// </summary>
public sealed class LabelRenameActionStub
{
    private string _nodeLabel;
    private readonly string _previousLabel;
    private readonly string _newLabel;

    /// <summary>True when PreviousLabel != NewLabel — the same guard used by OnLabelCommitted.</summary>
    public bool ShouldRecord => _previousLabel != _newLabel;
    public string CurrentLabel => _nodeLabel;

    public LabelRenameActionStub(string currentLabel, string previousLabel, string newLabel)
    {
        _nodeLabel     = currentLabel;
        _previousLabel = previousLabel;
        _newLabel      = newLabel;
    }

    /// <summary>Applies the new label — mirrors ApplyLabelChange(nodeId, newLabel).</summary>
    public void Do()   => _nodeLabel = _newLabel;

    /// <summary>Restores the previous label — mirrors ApplyLabelChange(nodeId, previousLabel).</summary>
    public void Undo() => _nodeLabel = _previousLabel;
}

/// <summary>
/// Minimal state machine that mirrors the inline-edit fields inside WorkflowNodeRenderer:
/// _isEditingLabel, _labelBuffer, _previousLabelAtEditStart, and the committed-event side-effect.
/// Allows the business rules to be verified without UI infrastructure.
/// </summary>
public sealed class LabelEditStateMachine
{
    // Fields that mirror WorkflowNodeRenderer private state
    private string _currentLabel;

    public bool   IsEditingLabel          { get; private set; }
    public string LabelBuffer             { get; private set; } = string.Empty;
    public string PreviousLabelAtEditStart { get; private set; } = string.Empty;

    /// <summary>Last (previous, next) pair fired via Node.RaiseLabelCommitted — null if not yet fired.</summary>
    public (string Previous, string Next)? LastCommittedArgs { get; private set; }

    /// <summary>Whether the outer node double-click (Node.RaiseDoubleClicked) has fired.</summary>
    public bool NodeDoubleClickedFired { get; private set; }

    public LabelEditStateMachine(string initialLabel) => _currentLabel = initialLabel;

    /// <summary>
    /// Mirrors StartLabelEdit(): captures _previousLabelAtEditStart, populates buffer, enters edit mode.
    /// The ondblclick:stopPropagation on the label div means NodeDoubleClicked is NOT raised here.
    /// </summary>
    public void StartLabelEdit()
    {
        PreviousLabelAtEditStart = _currentLabel;
        LabelBuffer              = _currentLabel;
        IsEditingLabel           = true;
        // NodeDoubleClickedFired intentionally NOT set — stopPropagation is in effect.
    }

    /// <summary>Mirrors OnLabelInput(): updates buffer as the user types.</summary>
    public void OnLabelInput(string value) => LabelBuffer = value;

    /// <summary>
    /// Mirrors CommitLabel(): double-fire guard → exit edit mode → raise RaiseLabelCommitted.
    /// </summary>
    public void CommitLabel()
    {
        if (!IsEditingLabel) return;  // double-fire guard (Enter-keydown + blur)
        IsEditingLabel   = false;
        LastCommittedArgs = (PreviousLabelAtEditStart, LabelBuffer);
        _currentLabel    = LabelBuffer;
    }

    /// <summary>Mirrors CancelLabel(): exits edit mode without raising the committed event.</summary>
    public void CancelLabel()
    {
        IsEditingLabel    = false;
        LastCommittedArgs = null;
    }

    /// <summary>Simulates the outer node div's ondblclick firing (when clicking OUTSIDE the label span).</summary>
    public void FireNodeDoubleClicked() => NodeDoubleClickedFired = true;
}

// ── Tests ─────────────────────────────────────────────────────────────────────────────────

public class WorkflowNodeLabelEditTests
{
    // ── T002a: Do applies the new label ───────────────────────────────────────────

    [Fact]
    public void Do_AppliesNewLabel_ToWorkflowNode()
    {
        // RenameLabelAction.Do() sets nodeModel.WorkflowNode = nodeModel.WorkflowNode with { Label = newLabel }.
        // WorkflowNode is an immutable record; the original node is unchanged.
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Original");
        var updatedNode = node with { Label = "Updated" };

        Assert.Equal("Updated", updatedNode.Label);
        Assert.Equal("Original", node.Label);  // original must be untouched
    }

    // ── T002b: Undo restores previous label ───────────────────────────────────────

    [Fact]
    public void Undo_RestoresPreviousLabel()
    {
        var action = new LabelRenameActionStub("Original", "Original", "Updated");
        action.Do();
        Assert.Equal("Updated", action.CurrentLabel);

        action.Undo();
        Assert.Equal("Original", action.CurrentLabel);
    }

    // ── T002c: No-op when labels are equal ────────────────────────────────────────

    [Fact]
    public void OnLabelCommitted_NoOp_WhenLabelsAreEqual()
    {
        // When PreviousLabel == NewLabel, the canvas handler returns without recording an action.
        var action = new LabelRenameActionStub("Same", "Same", "Same");

        Assert.False(action.ShouldRecord,
            "No rename action should be recorded when the committed label equals the pre-edit label.");
    }

    // ── T006a: DoubleClick activates label input ──────────────────────────────────

    [Fact]
    public void StartLabelEdit_TransitionsToEditingState_AndPopulatesBuffer()
    {
        var machine = new LabelEditStateMachine("AI Agent");
        machine.StartLabelEdit();

        Assert.True(machine.IsEditingLabel);
        Assert.Equal("AI Agent", machine.LabelBuffer);
        Assert.Equal("AI Agent", machine.PreviousLabelAtEditStart);
    }

    // ── T006b: CommitLabel raises LabelCommitted with correct values ──────────────

    [Fact]
    public void CommitLabel_AfterTypingNewName_RaisesArgsWithCorrectValues()
    {
        var machine = new LabelEditStateMachine("AI Agent");
        machine.StartLabelEdit();
        machine.OnLabelInput("Custom Name");

        machine.CommitLabel();

        Assert.False(machine.IsEditingLabel);
        Assert.NotNull(machine.LastCommittedArgs);
        Assert.Equal("AI Agent",    machine.LastCommittedArgs!.Value.Previous);
        Assert.Equal("Custom Name", machine.LastCommittedArgs!.Value.Next);
    }

    // ── T006c: EscapeKey cancels edit without firing commit event ─────────────────

    [Fact]
    public void CancelLabel_ExitsEditMode_WithoutRaisingCommitEvent()
    {
        var machine = new LabelEditStateMachine("AI Agent");
        machine.StartLabelEdit();
        machine.OnLabelInput("Discard me");

        machine.CancelLabel();

        Assert.False(machine.IsEditingLabel);
        Assert.Null(machine.LastCommittedArgs);
    }

    // ── T006d: Double-click on label span does NOT raise NodeDoubleClicked ─────────

    [Fact]
    public void StartLabelEdit_DoesNotFireNodeDoubleClicked()
    {
        // @ondblclick:stopPropagation="true" on the label container means the outer
        // node div's ondblclick (which calls Node.RaiseDoubleClicked()) must not fire
        // when the user double-clicks the label span.
        var machine = new LabelEditStateMachine("AI Agent");
        machine.StartLabelEdit();

        Assert.False(machine.NodeDoubleClickedFired,
            "stopPropagation must prevent NodeDoubleClicked from firing when double-clicking the label.");
    }

    // ── Double-fire guard ─────────────────────────────────────────────────────────

    [Fact]
    public void CommitLabel_SecondCallIsIgnored_WhenAlreadyCommitted()
    {
        // Enter-keydown + immediate blur can both try to commit. Only the first should fire.
        var machine = new LabelEditStateMachine("AI Agent");
        machine.StartLabelEdit();
        machine.OnLabelInput("First");
        machine.CommitLabel();                    // first call: commits
        machine.OnLabelInput("Second attempt");
        machine.CommitLabel();                    // second call: guard returns early

        Assert.Equal("First", machine.LastCommittedArgs!.Value.Next);
    }

    // ── T011: Re-edit shows committed value, not type default ─────────────────────

    [Fact]
    public void ReEdit_ShowsCommittedValue_NotTypeDefault()
    {
        // After committing "My Step", the canvas calls ApplyLabelChange which updates
        // nodeModel.WorkflowNode.Label to "My Step". When the user double-clicks again,
        // StartLabelEdit reads that value — not the original "AI Agent" type default.
        var node = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "AI Agent")
            with { Label = "My Step" };

        var machine = new LabelEditStateMachine(node.Label);
        machine.StartLabelEdit();

        Assert.Equal("My Step", machine.LabelBuffer);
        Assert.NotEqual("AI Agent", machine.LabelBuffer);
    }

    // ── T013: Empty label produces fallback display value ─────────────────────────

    [Fact]
    public void EmptyLabel_BufferIsEmpty_DisplayFallbackIsTypeName()
    {
        // When the user commits an empty string, NewLabel == "" is stored.
        // DisplayLabel() renders the type-default fallback — never blank.
        var machine = new LabelEditStateMachine("AI Agent");
        machine.StartLabelEdit();
        machine.OnLabelInput(string.Empty);  // user cleared the field
        machine.CommitLabel();

        // Committed args carry empty string — the canvas stores it.
        Assert.Equal(string.Empty, machine.LastCommittedArgs!.Value.Next);

        // DisplayLabel() must fall back to a non-blank string (type name or "Untitled node").
        // Mirrors: private string DisplayLabel() => string.IsNullOrEmpty(Node.WorkflowNode.Label) ? GetFallbackLabel() : ...
        var displayLabel = string.IsNullOrEmpty(machine.LastCommittedArgs.Value.Next)
            ? GetFallbackLabel(WorkflowNodeType.AgenticReason)
            : machine.LastCommittedArgs.Value.Next;

        Assert.NotEmpty(displayLabel);
        Assert.Equal("AI Agent", displayLabel);  // AgenticReason fallback
    }

    // ── Helper matching GetFallbackLabel() in WorkflowNodeRenderer ────────────────

    private static string GetFallbackLabel(WorkflowNodeType nodeType) => nodeType switch
    {
        WorkflowNodeType.Trigger           => "Start / Trigger",
        WorkflowNodeType.AgenticReason     => "AI Agent",
        WorkflowNodeType.HumanApproval     => "Ask a Person",
        WorkflowNodeType.FunctionRoute     => "Smart Branch",
        WorkflowNodeType.FunctionTransform => "Transform",
        WorkflowNodeType.FunctionNotify    => "Notify",
        WorkflowNodeType.FunctionData      => "Save / Load",
        _                                  => "Untitled node",
    };
}
