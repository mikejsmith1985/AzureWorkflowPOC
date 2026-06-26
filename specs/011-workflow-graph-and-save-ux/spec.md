# Feature Specification: Per-Workflow Graph View & Trustworthy Node Editing

**Feature Branch**: `feature/workflow-graph-and-save-ux`

**Created**: 2026-06-25

**Status**: Draft

**Input**: User description: "This UX is terrible — the save button is nowhere near the place where I'm making the update, and the new text still isn't being saved. The Graph view is great but it is hardcoded instead of actually coming from the built workflows. Can you generate a workflow that actually matches this topology? With the workflow builder I think the Graph view becomes useless — do you agree, or is there still an important purpose it serves? I'd be completely OK just having it as another view of each workflow inside the Workflows tab if you think it still provides value."

---

## Clarifications

### Session 2026-06-25

- Q: With the Workflow Builder in place, is the standalone Graph view still useful, or should it be removed? → A: **The capability is valuable, but not as a standalone hardcoded tab.** Retire the standalone top-level Graph view and resurrect the diagram as a **read-only per-workflow Graph view inside the Workflows tab**, generated from each workflow's real nodes and edges.
- Q: What should "generate a workflow that matches this topology" produce? → A: **Seed a real, persisted workflow** (the "Intake Pipeline") in the Workflows gallery whose nodes and edges reproduce the topology the old hardcoded Graph displayed, so the new per-workflow diagram renders from genuine data.
- Q: Where should the save action live for node edits? → A: **Co-located with the edit** — the action that commits a node's edited text must be in or immediately adjacent to the node editor panel where the user is typing, not only in a far-away top toolbar.
- Q: When the user edits a node's text, must it actually persist? → A: **Yes** — edited node text (label, goal/prompt, input/output descriptions) MUST survive committing the edit, navigating away, reloading the workflow, and the standard auto-save, with no silent loss.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Edit a node and trust the change is saved (Priority: P1)

A user opens a workflow in the builder, clicks a node, and edits its text in the editor panel
(e.g. what starts the workflow, a step's goal, or its input/output description). They want a
clear, nearby way to commit that change, and they want absolute confidence that the new text is
actually stored — that it is still there after they click away, reload the workflow, or come back
tomorrow.

**Why this priority**: This is the user's most acute, blocking frustration — edits that silently
vanish make the builder untrustworthy and unusable for real work. Nothing else matters if the
core "type something and it sticks" loop is broken.

**Independent Test**: Open a workflow, edit a node's text, commit the edit, fully reload the
workflow from storage, and confirm the new text is present on the node and in its editor panel.

**Acceptance Scenarios**:

1. **Given** a node's editor panel is open, **When** the user changes a text field and commits the
   edit, **Then** the new text is reflected on the node immediately and is persisted to storage.
2. **Given** a node whose text was edited and committed, **When** the workflow is reloaded from
   storage (fresh navigation / page reload), **Then** the edited text is shown — not the previous
   value and not a default/fallback value.
3. **Given** a node's editor panel is open with an in-progress edit, **When** the user commits via
   the editor panel's own commit action (located with the fields, not only in the top toolbar),
   **Then** the edit is captured without the user having to hunt for a distant Save control.
4. **Given** an edited but not-yet-committed value, **When** the canvas or panel re-renders for an
   unrelated reason, **Then** the in-progress text is not discarded or reset to a stale value.
5. **Given** an edited and committed node, **When** the workflow's auto-save fires, **Then** the
   edited text is included in what is saved (the edit is not lost between commit and auto-save).

---

### User Story 2 — See any workflow as a clean read-only diagram from the Workflows tab (Priority: P1)

From the Workflows tab, a user picks any saved workflow and opens a read-only "Graph" view of it:
a clean, automatically laid-out diagram of that workflow's actual steps and connections. They use
it to understand the flow at a glance and to present it, without the editing chrome of the builder
canvas. The standalone, hardcoded top-level Graph view is gone; the diagram now always reflects a
real workflow.

**Why this priority**: This resolves the "the Graph is hardcoded and disconnected from reality"
complaint and repurposes a valuable capability instead of discarding it. It is the structural
heart of the request.

**Independent Test**: From the Workflows tab, open the Graph view for a specific saved workflow,
then change that workflow in the builder (add/rename a step), reopen the Graph view, and confirm
the diagram reflects the change — proving it is generated from real workflow data, not a fixed
picture.

**Acceptance Scenarios**:

1. **Given** the Workflows tab listing saved workflows, **When** the user chooses to view a
   workflow's graph, **Then** a read-only diagram of that workflow's real nodes and connections is
   shown, automatically laid out and labelled.
2. **Given** a workflow is edited in the builder (a step added, removed, renamed, or reconnected),
   **When** its Graph view is opened afterward, **Then** the diagram reflects the current state of
   that workflow.
3. **Given** the application after this change, **When** the user looks for the old standalone
   top-level Graph navigation entry, **Then** it is gone, and the Workflows tab is reachable from
   the primary navigation.
4. **Given** a workflow's Graph view, **When** the user wants to make a change, **Then** the view
   offers a clear path to open that same workflow in the editing builder (read-only view ↔ editor).
5. **Given** a workflow with a human-approval step, a branch, or a notification step, **When** its
   Graph view is shown, **Then** each step is represented with a label a non-technical reader can
   understand and connections are labelled with what passes between steps.

---

### User Story 3 — A real seeded workflow reproduces the former hardcoded topology (Priority: P2)

So that the new per-workflow Graph view has meaningful real data to show — and so the topology the
old hardcoded diagram documented is not lost — a real, saved workflow (the "Intake Pipeline")
exists in the Workflows gallery reproducing that topology: the intake/validation/gap-analysis/
human-pause/estimation/action flow, including its branch and its human-in-the-loop pause.

**Why this priority**: It provides genuine data behind US2 and preserves institutional knowledge
the hardcoded graph captured, but it is supporting content rather than the core capability.

**Independent Test**: Open the Workflows tab, find the seeded "Intake Pipeline" workflow, open its
Graph view, and confirm the diagram matches the intake-pipeline topology (sources → intake →
validation → branch to gap-analysis/estimation, human pause, → action → done).

**Acceptance Scenarios**:

1. **Given** the Workflows gallery, **When** the user looks for the seeded Intake Pipeline
   workflow, **Then** it is present as a real, openable, saved workflow (not a hardcoded picture).
2. **Given** the seeded Intake Pipeline workflow, **When** its Graph view is opened, **Then** the
   diagram shows the same step set and connections the previous hardcoded Graph documented,
   including the validation branch and the human-in-the-loop pause.
3. **Given** the seeded workflow, **When** the user opens it in the builder, **Then** it behaves
   like any other user workflow — its steps can be inspected and (US1) edited and saved.

---

### Edge Cases

- **Empty / single-node workflow** → the Graph view renders without error, showing whatever
  step(s) exist (or a clear empty-state message) rather than failing.
- **Workflow with disconnected nodes** (a step with no connections) → the node still appears in the
  diagram; it is not silently dropped.
- **Very large workflow** → the diagram remains legible (auto-layout and scroll/zoom) rather than
  overflowing or truncating nodes.
- **Node with an empty label** → the Graph view shows a sensible fallback label, consistent with
  how the builder canvas labels an unnamed node, never a blank box.
- **Edit committed but storage write fails** → the user is told the save did not succeed rather
  than being shown a success state over lost data.
- **Rapid successive edits to the same field** → the final committed value wins; no earlier
  keystroke "snaps back" over a later one.
- **Switching between nodes mid-edit** → committing or abandoning the first node's edit is
  unambiguous; the second node's panel never shows the first node's in-progress text.
- **Deep-linking to a deleted/again-unknown workflow's Graph view** → a clear not-found state, not
  a crash or a blank diagram.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Editing a node's text fields (label, goal/prompt, input description, output
  description, start-trigger description) MUST persist the new values to the workflow's stored
  definition; the edited text MUST survive committing the edit, reloading the workflow from
  storage, and the standard auto-save.
- **FR-002**: The action that commits a node's edit MUST be available within, or immediately
  adjacent to, the node editor panel where the user is typing — the user MUST NOT be required to
  travel to a distant top-toolbar control to capture an edit they made in the side panel.
- **FR-003**: An in-progress edit MUST NOT be discarded or reverted by unrelated re-renders of the
  canvas or panel; only an explicit commit or explicit abandon changes the stored value.
- **FR-004**: After a node edit is committed, the new text MUST be visible both on the canvas node
  and in the editor panel for that node, with no reversion to the prior or default value.
- **FR-005**: The standalone, hardcoded top-level Graph navigation entry and view MUST be removed.
- **FR-006**: The Workflows tab MUST be reachable from the primary navigation, and MUST offer, per
  saved workflow, a read-only Graph view of that workflow.
- **FR-007**: The per-workflow Graph view MUST be generated from that workflow's actual stored
  nodes and connections — never from a hardcoded or static topology — and MUST reflect subsequent
  edits to that workflow when reopened.
- **FR-008**: The per-workflow Graph view MUST be read-only (no node creation, deletion, or
  repositioning), automatically laid out, with every step labelled understandably and every
  connection labelled with what passes between steps.
- **FR-009**: The Graph view MUST provide a clear path to open the same workflow in the editing
  builder, and the builder MUST provide a path to view the workflow's graph (round-trip between
  read-only view and editor).
- **FR-010**: A real, persisted "Intake Pipeline" workflow MUST exist in the Workflows gallery
  reproducing the topology the former hardcoded Graph documented — including the validation branch
  and the human-in-the-loop pause — openable and editable like any other workflow.
- **FR-011**: The Graph view MUST handle empty, single-node, disconnected-node, unnamed-node, and
  not-found cases gracefully (clear empty/fallback/not-found states, never a crash or a blank box).
- **FR-012**: If committing an edit fails to persist, the user MUST be informed of the failure
  rather than shown a success state, so no edit is silently lost.

### Key Entities *(include if feature involves data)*

- **Workflow**: A saved, user-authored (or seeded) pipeline of steps and connections — the single
  source of truth both the builder and the per-workflow Graph view render from.
- **Workflow Step (Node)**: An individual step in a workflow with an editable label and editable
  descriptive text (goal/prompt, input description, output description). Its committed text is what
  both the canvas and the Graph view display.
- **Workflow Connection (Edge)**: A directed, labelled link between two steps describing what
  passes from one to the next; rendered as a labelled arrow in the Graph view.
- **Seeded Intake Pipeline Workflow**: A real, persisted workflow reproducing the former hardcoded
  Graph topology, present so the per-workflow Graph view has authentic data and the documented
  intake topology is preserved.
- **Graph View**: A read-only, automatically laid-out diagram of one workflow, generated on demand
  from that workflow's current stored steps and connections.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can edit a node's text, commit it, fully reload the workflow, and find the
  edited text intact — in 100% of attempts, with zero silent reversions to a prior or default value.
- **SC-002**: The control that commits a node edit is reachable without leaving the node editor
  panel's immediate area — the user never has to cross the screen to a separate toolbar to capture
  a side-panel edit.
- **SC-003**: Opening any saved workflow's Graph view shows a diagram whose steps and connections
  match that workflow's current definition; editing the workflow and reopening the view reflects
  the change in 100% of attempts.
- **SC-004**: After this change, there is no standalone hardcoded Graph view anywhere in the
  product; every diagram shown is derived from a real workflow.
- **SC-005**: The seeded Intake Pipeline workflow is present in the gallery and its Graph view
  reproduces the previously documented intake topology, including the branch and the human pause.
- **SC-006**: The Graph view renders without error for empty, single-node, disconnected-node, and
  large workflows, and shows a clear not-found state for an unknown workflow.
- **SC-007**: A first-time viewer can read a workflow's Graph view and correctly describe the flow
  (start → steps → branch/human-pause → end) without consulting the builder or any documentation.

## Assumptions

- The existing saved-workflow storage (the same store the builder reads and writes, with its
  steps, connections, labels, and per-step text) is the single source of truth the Graph view
  reads from; no new parallel store is introduced.
- The diagram-rendering capability used by the former hardcoded Graph, and the existing logic that
  describes a workflow's steps and connections in human-readable form, are reusable to render the
  per-workflow Graph view from real data (framework-first; no new diagram engine is built).
- The Workflows gallery already exists as the list of saved workflows; this feature makes it
  reachable from the primary navigation and adds the per-workflow Graph view to it.
- "Commit an edit" refers to the deliberate confirm action in/near the editor panel; transient
  keystrokes need not each be persisted, but the committed value must be.
- The seeded Intake Pipeline workflow is owned consistently with how other example/seeded
  workflows are owned, and is created idempotently (re-seeding does not create duplicates).
- The intent of "matches this topology" is structural fidelity (the same steps, branch, human
  pause, and connections), not a pixel-identical reproduction of the old hardcoded layout.

## Dependencies

- The existing Workflow Builder canvas, node editor panel, and node-edit commit/auto-save path
  (the subject of the US1 persistence fix and the co-located commit control).
- The existing saved-workflow storage and the Workflows gallery list page.
- The existing diagram-rendering capability and the existing workflow-to-readable-topology
  description logic, reused to generate the per-workflow Graph view.
- The primary navigation, which loses the standalone Graph entry and gains/keeps a reachable
  Workflows entry.
- Out of scope (explicit non-dependencies): editing a workflow from within the Graph view itself
  (it is read-only); changing the underlying workflow data model; redesigning the builder canvas
  beyond the node-edit commit control; and any change to how workflows are executed/"made real".
