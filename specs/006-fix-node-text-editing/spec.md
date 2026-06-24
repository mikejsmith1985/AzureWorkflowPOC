# Feature Specification: Fix Node Text Editing in Workflow Builder

**Feature Branch**: `fix/node-text-editing`

**Created**: 2026-06-21

**Status**: Draft

**Input**: User description: "I can't actually type anything into the nodes in workflow builder
the default text immediately reapplies/displays if I try to delete it or replace it."

---

## Clarifications

### Session 2026-06-21

- Q: Should committing a label change be undoable via Ctrl+Z on the canvas (workflow-level undo stack)? → A: **Yes.** Label commits are added to the workflow undo stack. Pressing Ctrl+Z after committing a label change restores the previous label value, consistent with the undo model for node deletion (spec 003 FR-01.4, spec 004 FR-11.4).
- Q: What gesture activates a node label for editing? → A: **Double-click.** A single-click selects the node; a double-click on the label region enters edit mode. This is the standard graph-editor convention (draw.io, Miro, Figma) and prevents accidental edit activation during selection or drag.
- Q: Must label editing be operable via keyboard alone (no mouse)? → A: **Yes.** A keyboard-only user must be able to Tab to a node, press Enter to activate the label input (equivalent to double-click), type their label, and press Enter to commit or Escape to cancel — without touching a mouse.

---

## Overview

Node labels in the Workflow Builder are currently non-functional for editing. When a user
focuses a node's text field and tries to delete or replace its default placeholder text, the
original default text snaps back instantly — making it impossible to give nodes meaningful,
user-defined names. This is a correctness blocker: a workflow whose nodes all display generic
default labels cannot be understood, saved, or generated into meaningful code.

This fix restores the expected behaviour: a user can click into any node's text field, clear
the default text, type their own label, and have that label persist for the lifetime of the
node.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — User renames a node to describe what it does (Priority: P1)

A business analyst places an "AI Agent" node on the canvas. The default label reads
"AI Agent." They want to rename it to "Triage Incoming Ticket" so the diagram makes sense
to their team. They click the label, delete it, type the new name, and press Enter — the
node should immediately display "Triage Incoming Ticket" and continue to do so after
any subsequent click elsewhere on the canvas.

**Why this priority**: This is a regression that blocks all meaningful use of the builder.
Every workflow node must be nameable. Generic default labels make generated code unreadable
and the canvas illegible for review or collaboration.

**Independent Test**: Place a node. Click its label. Press Ctrl+A and then Delete. Confirm
the field is empty. Type "My Custom Step." Press Enter (or click elsewhere). Confirm the
node displays "My Custom Step" — not the former default — and continues to do so after
clicking elsewhere on the canvas and after a page reload (if state is persisted).

**Acceptance Scenarios**:

1. **Given** a node with a default label is on the canvas, **When** the user double-clicks
   the node label area, **Then** the label becomes an active text input and the cursor is
   placed inside it — the default text is selected or the cursor is at the end, ready for
   the user to begin typing or selecting all. A single-click selects the node without
   entering edit mode.
2. **Given** the label input is active, **When** the user presses Delete, Backspace, Ctrl+A
   followed by Delete, or any other standard text-clearing action, **Then** the field becomes
   empty — the default text does not reappear while the field has focus.
3. **Given** an empty label input, **When** the user types new text, **Then** the typed text
   appears in the field character-by-character without the default text re-inserting itself at
   any point.
4. **Given** the user has typed a new label and commits by pressing Enter or clicking
   anywhere outside the node, **Then** the node displays the user's typed label (not the
   default) from that point forward — including after the user clicks back onto the canvas,
   scrolls, and zooms.
5. **Given** a node that was successfully renamed, **When** the user clicks the label again,
   **Then** the active input shows the previously saved label — not the original default —
   so the user can further edit the name without it resetting.
6. **Given** the user activates a node label input and then presses Escape without typing,
   **Then** the node retains whatever label it had before the edit began (either the default
   or a previously saved custom label) — Escape cancels the edit, it does not reset to default.

---

### User Story 2 — User corrects a typo in a label they already set (Priority: P1)

A user previously named a node "Triage Incomin Ticket" (typo). They click the label to fix
the spelling. The input should show the current saved label "Triage Incomin Ticket" — not
reset to the default — and the user should be able to correct just that one word.

**Why this priority**: Same root cause as User Story 1. The fix must handle re-editing a
previously saved label as correctly as it handles editing a default label for the first time.

**Independent Test**: Rename a node, commit, then click the label again. Verify the active
input contains the previously typed text (not the default), make a change, commit again,
and verify the updated text is shown.

**Acceptance Scenarios**:

1. **Given** a node whose label was previously changed from default to "My Step," **When**
   the user clicks the label, **Then** the active input contains "My Step" — not the
   original default label.
2. **Given** the user edits an existing custom label and commits, **Then** the node
   displays the latest committed value — changes are not lost on the next interaction.

---

### User Story 3 — User leaves a node label empty (Priority: P2)

A user deletes all text from a node label and commits without typing a replacement. The
builder should either (a) restore the default label as a fallback so the node is never
visually blank, or (b) display a clear placeholder hint like "Untitled node" in a distinct
style (e.g. greyed-out italic) indicating the field is empty — in either case the empty
state must be intentional and legible, not a symptom of the editing bug.

**Why this priority**: The empty-label behaviour is secondary to fixing the core input bug.
A clear, consistent empty state is required for usability but is not as critical as allowing
editing in the first place.

**Independent Test**: Clear a node label completely and commit. Verify the node displays
either its default label or a styled "Untitled node" placeholder — not a blank rectangle.

**Acceptance Scenarios**:

1. **Given** the user clears all text from a node label and commits (Enter or blur), **Then**
   the node displays either its type-default label or a visually distinct "Untitled node"
   placeholder — the canvas never shows a node with no visible label.
2. **Given** a node in the empty-label state, **When** the user clicks the label again,
   **Then** the input is empty (ready to type), not prefilled with "Untitled node" — the
   placeholder text is display-only, not injected into the editable field.

---

## Functional Requirements

### FR-12 Node Label Editing

- **FR-12.1** Every node on the canvas must expose a label region that enters edit mode on
  **double-click**. A single-click on a node selects it (normal selection behaviour) without
  entering edit mode. Double-clicking the label region must place the text cursor inside an
  editable text input that contains the node's current label value — not a read-only display
  element. This gesture must be consistent across all node types.
- **FR-12.2** While the label input has focus, no external process (Blazor state update,
  re-render cycle, SignalR event, or timer) may overwrite or re-apply the field's displayed
  value. The input must be isolated from reactive data binding while it is in an active
  editing state.
- **FR-12.3** The user must be able to clear the entire label text using any standard
  keyboard gesture (Delete, Backspace, Ctrl+A + Delete, Ctrl+X) without the original
  value re-inserting itself.
- **FR-12.4** Changes are committed — written to the node's in-memory state — when the
  user presses Enter or moves focus away from the input (blur). Until committed, changes
  exist only in the input field and do not affect the underlying node model.
- **FR-12.5** Pressing Escape while a label input is active must discard uncommitted
  changes and restore the input to the value the node held before editing began. It must
  not reset to the node's type-default label unless that was already the active label.
- **FR-12.6** After committing a new label, re-activating the same node's label input
  must display the committed value — not the type-default — confirming the value was
  persisted in the node's state.
- **FR-12.7** If the user commits an empty label (all text deleted), the node model stores
  an explicit empty string. The node's visual component must then display either the
  type-default label text or a styled "Untitled node" placeholder, chosen at render time,
  never stored as the label value itself.
- **FR-12.8** The fix must not regress any existing node interaction: port clicking,
  node dragging, right-click context menu, and node selection must all continue to function
  when the label is in its resting (non-editing) state.
- **FR-12.9** Label editing must be fully operable without a mouse. A keyboard-only user
  must be able to: Tab to a node to give it focus, press Enter to activate the label input
  (equivalent to double-click), type a new label, and press Enter to commit or Escape to
  cancel. The Tab order must be deterministic and follow the visual left-to-right,
  top-to-bottom reading order of nodes on the canvas.
- **FR-12.10** Every label commit (pressing Enter or blurring the input with a changed value)
  must register an entry on the workflow's undo stack. Pressing the undo shortcut (Ctrl+Z /
  ⌘Z) from the canvas after a label commit must restore the node's label to the value it
  held immediately before that commit. Multiple consecutive label commits must each produce
  a discrete undo step so the user can walk back through label history one change at a time.
  (Renamed from FR-12.9 after keyboard-accessibility requirement was inserted.)

---

## Success Criteria

1. **Label persistence**: After a user types a new node label and commits, the node displays
   that label on every subsequent view of the canvas — verified across at least 10 consecutive
   interactions (click away, scroll, zoom, click back) without a reset.
2. **No mid-edit reset**: During an active label edit, the default text never re-appears in
   the input field — verified by automated UI test that types into a node label and asserts
   the field value is not overwritten by the default within a 3-second observation window.
3. **Escape cancels correctly**: Pressing Escape restores the pre-edit label value in 100 %
   of tested cases — neither resetting to default (when a custom label was set) nor losing
   the text typed during the edit.
4. **All node types editable**: Every node type in the palette (AI Agent, Function, Smart
   Branch, Trigger, and any others) passes the same edit-commit-verify test with no type-
   specific failures.
5. **No regression to other node interactions**: Port connections, node dragging, and context
   menus continue to function correctly after the fix — verified by the full existing
   Playwright E2E suite passing without new failures.
6. **Empty-label handling**: Committing an empty label results in a non-blank node display
   (either type-default or styled placeholder) — the canvas contains no invisible or blank
   node rectangles.
7. **Label undo fidelity**: After committing a label change, a single Ctrl+Z restores the
   node's previous label — verified for at least 3 consecutive commit-then-undo cycles on
   the same node with no label corruption or default-reset.
8. **Keyboard-only editing**: A user who never touches a mouse can Tab to a node, press
   Enter, type a label, and press Enter to commit — verified by a Playwright test that
   uses only keyboard events (no mouse) end-to-end.

---

## Key Entities

| Entity | Description |
|--------|-------------|
| **Node Label** | The user-visible name displayed on a canvas node. Can be edited by the user to give the node a meaningful, context-specific name. |
| **Type-Default Label** | The generic name a node displays when first placed (e.g. "AI Agent," "Function Step"). Used as a fallback only, never re-imposed once the user has committed a custom label. |
| **Label Input** | The editable text field that appears when the user activates a node's label region. Isolated from reactive re-render while it has focus. |
| **Committed Value** | The label text that has been written to the node's in-memory (and persisted) state after the user presses Enter or blurs the input. |
| **Placeholder Display** | A visually distinct (greyed-out, italic) rendition of "Untitled node" shown on a node whose committed label is empty — it is a display artifact, not stored in the label field. |

---

## Assumptions

1. The bug is caused by Blazor's two-way data binding (or an equivalent reactive update
   cycle) overwriting the DOM input's value while the user is mid-edit. The fix targets
   the binding strategy for the label field; it does not require changes to the node graph
   data model.
2. Node label state is stored in the workflow's in-memory `WorkflowDefinition` model and
   is persisted whenever the workflow is saved. No new persistence layer is needed.
3. The fix applies to all node types equally — there are no node types where a locked or
   read-only label is an intended behaviour in the current release.
4. The existing Playwright E2E test suite (specs/003–005 tests) provides the regression
   baseline. No new test infrastructure is required beyond new test cases for the
   label-editing scenarios above.
5. The edit-activation gesture is **double-click** on the label region (clarified 2026-06-21).
   Single-click selects the node; double-click enters label edit mode. This is consistent
   with the standard convention used by draw.io, Miro, and Figma.

---

## Out of Scope

- Rich-text or multi-line node labels (single-line plain text only in this release)
- Node label character limits or validation rules (no length constraints imposed by this fix)
- Collaborative / multi-user editing conflicts (single-user session only)
- Label auto-suggest or AI-generated label recommendations
- Renaming nodes from outside the canvas (e.g. via a properties panel or sidebar)
