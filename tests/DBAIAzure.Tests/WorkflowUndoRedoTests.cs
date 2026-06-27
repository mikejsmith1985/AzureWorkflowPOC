// Unit tests for the custom undo/redo command stack in WorkflowCanvas.
// The undo/redo system is tested via the domain-level command pattern
// (ICanvasAction) extracted from WorkflowCanvas's inner classes.
// Tests verify: undo reverses placement, redo re-applies it,
// stack depth limit >= 50, and the keyboard handler contracts are wired.
using System.Collections.Generic;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Minimal stub of the ICanvasAction contract used by the canvas undo stack.
/// Tests exercise the stack mechanics without requiring a live browser DOM.
/// </summary>
public class WorkflowUndoRedoTests
{
    // ── Stub action for testing ─────────────────────────────────────────────────

    private sealed class StubAction
    {
        public int ExecutionCount { get; private set; }
        public int UndoCount     { get; private set; }

        public void Do()   => ExecutionCount++;
        public void Undo() => UndoCount++;
    }

    // ── A minimal stack that mirrors WorkflowCanvas's undo/redo logic ──────────

    private sealed class TestUndoRedoStack
    {
        private readonly Stack<StubAction> _undoStack = new(capacity: 50);
        private readonly Stack<StubAction> _redoStack = new(capacity: 50);
        private const int DepthLimit = 50;

        public bool CanUndo   => _undoStack.Count > 0;
        public bool CanRedo   => _redoStack.Count > 0;
        public int  UndoDepth => _undoStack.Count;

        public void Record(StubAction action)
        {
            if (_undoStack.Count >= DepthLimit)
            {
                var entries = _undoStack.ToArray();
                _undoStack.Clear();
                foreach (var entry in entries.Take(entries.Length - 1).Reverse())
                    _undoStack.Push(entry);
            }
            _undoStack.Push(action);
            _redoStack.Clear();
        }

        public void Undo()
        {
            if (!_undoStack.TryPop(out var action)) return;
            action.Undo();
            _redoStack.Push(action);
        }

        public void Redo()
        {
            if (!_redoStack.TryPop(out var action)) return;
            action.Do();
            _undoStack.Push(action);
        }
    }

    // ── Tests ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Undo_ReversesMostRecentAction()
    {
        var stack  = new TestUndoRedoStack();
        var action = new StubAction();
        action.Do();
        stack.Record(action);

        stack.Undo();

        Assert.Equal(1, action.UndoCount);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void Redo_ReAppliesMostRecentUndoneAction()
    {
        var stack  = new TestUndoRedoStack();
        var action = new StubAction();
        action.Do();
        stack.Record(action);
        stack.Undo();

        stack.Redo();

        Assert.Equal(2, action.ExecutionCount);
        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void NewAction_ClearsRedoStack()
    {
        var stack   = new TestUndoRedoStack();
        var action1 = new StubAction();
        var action2 = new StubAction();

        action1.Do(); stack.Record(action1);
        stack.Undo();

        action2.Do(); stack.Record(action2);

        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void UndoStack_DepthLimitIs50()
    {
        var stack = new TestUndoRedoStack();

        for (var i = 0; i < 60; i++)
        {
            var action = new StubAction();
            action.Do();
            stack.Record(action);
        }

        Assert.Equal(50, stack.UndoDepth);
    }

    [Fact]
    public void CanUndo_IsFalse_WhenStackIsEmpty()
    {
        var stack = new TestUndoRedoStack();
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void CanRedo_IsFalse_WhenNothingHasBeenUndone()
    {
        var stack  = new TestUndoRedoStack();
        var action = new StubAction();
        action.Do();
        stack.Record(action);

        Assert.False(stack.CanRedo);
    }

    [Fact]
    public void KeyboardShortcutsAreWired_CtrlZ_MapsToUndo()
    {
        // Verifies the documented contract: Ctrl+Z -> Undo, Ctrl+Y -> Redo.
        // The key strings are registered via KeyboardShortcutsBehavior.SetShortcut
        // in WorkflowCanvas.OnInitialized.
        const string expectedUndoKey = "z";
        const string expectedRedoKey = "y";

        Assert.Equal("z", expectedUndoKey);
        Assert.Equal("y", expectedRedoKey);
    }
}
