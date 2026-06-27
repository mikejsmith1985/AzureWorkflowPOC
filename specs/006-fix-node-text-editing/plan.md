# Implementation Plan: Fix Node Text Editing in Workflow Builder

**Branch**: `fix/node-text-editing` | **Date**: 2026-06-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/006-fix-node-text-editing/spec.md`

---

## Summary

Two-part fix targeting a Blazor Server reactive-binding regression:

**Part 1 (Bug Fix)** — `WorkflowNodeConfigPanel.razor`: The `OnParametersSet()` override
unconditionally resets `_goalPrompt`, `_inputLabel`, and `_outputLabel` from the `Node`
parameter on every parent re-render. Because the 200 ms goal-preview debounce triggers a
parent `StateHasChanged()` call, any user keystroke in the config panel is followed 200 ms
later by a silent field reset to the pre-edit value. Fix: guard re-initialisation behind a
`_lastInitialisedNodeId` check — only reset fields when a different node is opened.

**Part 2 (New Feature)** — `WorkflowNodeRenderer.razor`: The node label is currently a
read-only `<span>`. Spec FR-12.1–12.10 require inline label editing directly on the canvas
node, with the double-click gesture, Escape/Enter commit/cancel semantics, undo stack
integration, and keyboard-only accessibility. No new service interfaces are needed; all
changes are to Blazor component state.

One new record struct (`LabelCommitArgs`) and one new `ICanvasAction` implementation
(`RenameLabelAction`) are added. All other changes modify existing files. No migration,
no new NuGet packages, no new projects.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8 (Blazor Server)

**Primary Dependencies**:
- Z.Blazor.Diagrams v3.0.4.1 — canvas; `WorkflowNodeModel.DoubleClicked` event is the
  existing double-click channel; `_diagram.Refresh()` triggers Blazor re-renders that were
  causing the bug; `@ondblclick:stopPropagation` on the label div prevents the label
  double-click from also firing the node-level DoubleClicked event
- Tailwind CSS (CDN) — inline input styled with existing utility classes
- xUnit 2.9.0 + bUnit 1.37.7 — component-level unit tests for the panel fix and renderer

**Storage**: No changes. `WorkflowNode.Label` (existing `string` property on the immutable
record) is the label storage target. Renamed labels are persisted at the next explicit save,
same as all other node mutations.

**Testing**: xUnit unit tests for the `RenameLabelAction`; bUnit tests for the panel fix and
renderer; new Playwright E2E tests in `WorkflowNodeLabelEditTests.cs`.

**Target Platform**: ASP.NET Core 8, Blazor Server, browser-rendered via SignalR

**Performance Goals**:
- Label commit: < 16 ms (synchronous Blazor state update, no I/O)
- Edit mode activation: < 32 ms (one `StateHasChanged()` + one `FocusAsync()` round-trip)
- No change to existing debounce timing (200 ms goal-preview debounce unchanged)

**Constraints**:
- The `@ondblclick:stopPropagation="true"` attribute on the label container `<div>` must
  prevent the existing `Node.RaiseDoubleClicked()` call in `OnDoubleClick` from firing when
  the user double-clicks the label — so the config panel does not open simultaneously with
  the inline edit input
- `_labelBuffer` must never be written by `OnParametersSet` or any external state update
  while `_isEditingLabel` is true — isolation is the core correctness guarantee
- `LabelCommitArgs.NewLabel` is never null; empty string is the empty-label signal
- The `RenameLabelAction` stores only two strings and a node ID (not a full node snapshot)
  to keep the 50-step undo stack memory-efficient

**Scale/Scope**: Single-user Blazor Server session; same scope as prior canvas work

---

## Constitution Check

| Article | Gate | Status |
|---------|------|--------|
| I — Prime Directive | Root cause fixed at source; not worked around. Inline editing is the idiomatic solution used by draw.io, Miro, Figma. | ✓ PASS |
| II — Process Protection | No process management needed | ✓ N/A |
| III — Branching | New branch `fix/node-text-editing` off main | ✓ PASS |
| IV — Code Quality | `_isEditingLabel`, `_labelBuffer`, `_lastInitialisedNodeId` are self-documenting. `LabelCommitArgs` is a readonly record struct with XML doc. `RenameLabelAction` carries XML doc on all members. No magic strings. | ✓ PASS |
| V — Testing (3-layer) | bUnit: panel reset guard; renderer edit/commit/cancel/undo. E2E Playwright: Scenarios 1–8 in quickstart.md. Red → Green → Refactor required. | ✓ PASS |
| VI — Docs Discipline | CHANGELOG.md updated in PR. No auxiliary docs beyond spec artifacts. | ✓ PASS |
| VII — Framework-First | See analysis below | ✓ PASS |
| VIII — Release | Not a release sprint | ✓ N/A |
| IX — Secrets | No secrets touched | ✓ N/A |
| X — Verification | Passing bUnit tests + Playwright E2E scenarios + observed browser behaviour for each scenario in quickstart.md | ✓ PASS |
| XI — Output Restraint | Plan artifacts in `specs/006/`; no ad-hoc status docs | ✓ PASS |

### Article VII — Framework-First Analysis

**Blazor Server provides natively — use these**:
- `@ondblclick:stopPropagation="true"` — prevents label double-click from bubbling to the
  node-level handler. No JS interop needed.
- `value="@_labelBuffer" @oninput="OnLabelInput"` one-way + event pattern — correct Blazor
  approach for inputs where the field must not be reset externally during editing. No library.
- `@onkeydown="OnLabelKeyDown"` with `e.Key == "Enter"` / `"Escape"` — native Blazor keyboard
  event handling. No JS shortcut library.
- `tabindex="0"` HTML attribute on the node outer div — standard browser focus management;
  makes nodes reachable via Tab. No JS.
- `ElementReference.FocusAsync()` — Blazor built-in to auto-focus the label input when edit
  mode starts. One line, no JS interop beyond what Blazor provides.
- `WorkflowNode with { Label = newLabel }` C# record `with`-expression — updates the immutable
  domain model. No new infrastructure.
- Existing `ICanvasAction` / `RecordAction()` / undo stack — already in `WorkflowCanvas.razor`;
  `RenameLabelAction` is a new implementation, not new infrastructure.

**Documented gaps requiring custom code**:
- **`LabelCommitArgs` record struct**: No library type for conveying structured before/after-label
  context from a node model event to the canvas handler. *Custom: trivial 3-property record
  struct in `WorkflowDiagramModels.cs`; zero runtime overhead.*
- **`RenameLabelAction`**: The existing `ICanvasAction` pattern has no generic "rename" action.
  *Custom: new implementation of the existing interface, 30 lines; no new infrastructure.*

Both custom items are minimal additions justified by documented gaps against the existing
framework primitives.

---

## Project Structure

### Documentation (this feature)

```text
specs/006-fix-node-text-editing/
├── plan.md              ← this file
├── research.md          ← Phase 0 output (root cause confirmed)
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output (8 validation scenarios)
├── contracts/
│   └── LabelCommitArgs.md
└── tasks.md             ← Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code Changes

```text
src/DBAIAzure.Web/Components/WorkflowBuilder/
├── WorkflowNodeRenderer.razor         [MODIFY]
│   • Add _isEditingLabel bool, _labelBuffer string, _labelInputRef ElementReference
│   • Replace label <span> with conditional span/input block
│   • Add @ondblclick:stopPropagation on label container div
│   • Add StartLabelEdit(), OnLabelInput(), OnLabelKeyDown(), CommitLabel(), CancelLabel()
│   • Add OnNodeKeyDown() for keyboard-only Enter → StartLabelEdit()
│   • Add tabindex="0" on outer div and label span
│   • Add _previousLabelAtEditStart string field; CommitLabel() calls
│     Node.RaiseLabelCommitted(_previousLabelAtEditStart, _labelBuffer)
│   • No [Parameter] EventCallback added — renderer signals canvas via WorkflowNodeModel event
│   • Update aria-label to remove "double-click to configure" (now ambiguous)
│
├── WorkflowNodeConfigPanel.razor      [MODIFY]
│   • Add _lastInitialisedNodeId string? field
│   • Guard OnParametersSet() to skip field reset when same node is still open
│   • Reset _lastInitialisedNodeId = null in OnCloseAsync()
│
└── WorkflowCanvas.razor               [MODIFY]
    • Add RenameLabelAction : ICanvasAction inner class (stores nodeId, prev, next labels)
    • Add ApplyLabelChange(string nodeId, string label) method
    • Add OnLabelCommitted(LabelCommitArgs args) handler (no-op guard + Do() + RecordAction())
    • Subscribe nodeModel.LabelCommitted += handler in each node-addition path
      (mirrors existing DoubleClicked and ContextMenuRequested subscription pattern —
      no razor markup change needed; renderer is registered via RegisterComponent, not
      instantiated directly in markup)

src/DBAIAzure.Web/Components/WorkflowBuilder/WorkflowDiagramModels.cs [MODIFY]
    • Add LabelCommitArgs readonly record struct
    • Add LabelCommitted event (Action<string, string>?) and RaiseLabelCommitted method
      to WorkflowNodeModel (enables renderer → canvas signalling without EventCallback)

tests/DBAIAzure.Tests/
├── WorkflowNodeConfigPanelResetGuardTests.cs  [NEW]
│   Unit/bUnit: panel does not reset fields on re-render while same node is open;
│   does reset fields when a different node is passed; resets after close
│
└── WorkflowNodeLabelEditTests.cs              [NEW] (bUnit + Playwright)
    bUnit: StartLabelEdit sets _isEditingLabel; CommitLabel raises LabelCommitted with
    correct args; Escape cancels; empty commit stores empty string; double-click on
    non-label area still fires Node.RaiseDoubleClicked()
    Playwright: Scenarios 1–8 from quickstart.md
```

**No new projects. No new NuGet packages. No migration.**

---

## Complexity Tracking

No constitution violations. Two custom items (both minimal) documented against the
Article VII Framework-First gate above.
