# Feature Specification: Workflow Builder UX Master Review

**Feature Branch**: `feature/visual-workflow-builder`

**Created**: 2026-06-20

**Status**: Draft

**Input**: User description: "I would like you to give the UX a master UX designer review and make
sure that the ability to build workflows is as straightforward and user friendly as possible."

---

## Clarifications

### Session 2026-06-20

- Q: When does the "Start from scratch / Try the example" entry choice screen appear? → A: Only when the user has zero saved workflows. Once at least one workflow exists, the choice is skipped and the builder opens an empty canvas directly.
- Q: If SVG thumbnail generation fails at save time, what happens? → A: The save proceeds silently without a thumbnail; the gallery card shows the existing "No preview" fallback. Thumbnail failure never blocks or warns the user.
- Q: Should the gallery search input appear only above a workflow count threshold or always? → A: Always visible once there is at least one workflow — no count threshold. Eliminates layout shift and the arbitrary cut-off.
- Q: When does typing in the node config panel set the unsaved-changes flag? → A: Only when the user clicks Done — committed changes only. Typing in the panel before clicking Done does not set the flag, preventing false-positive navigation warnings when the user explores config without saving.
- Q: What format should the chat panel use when showing regenerated code after a canvas change? → A: Compact diff — only changed lines ± 3 lines of context, colour-coded green (added) and red (removed). The full file is not shown unless the user explicitly requests it.

---

## UX Audit Context

This specification was derived from a complete code-level UX audit of every implemented component
in the Visual Workflow Builder (specs/003 and specs/004). The audit reviewed:

- `WorkflowBuilder.razor` — page shell and state management
- `WorkflowCanvas.razor` — canvas, drag/drop, undo/redo, deletion
- `WorkflowNodePalette.razor` — node catalogue with search and categories
- `WorkflowNodeRenderer.razor` — on-canvas node tiles
- `WorkflowToolbar.razor` — top action bar
- `WorkflowNodeConfigPanel.razor` — node configuration sidebar
- `WorkflowGalleryCard.razor` — gallery grid cards
- `WorkflowGallery.razor` — gallery page

The audit identified **20 distinct UX issues** across three priority levels. This specification
converts every finding into a verifiable, user-centred requirement.

---

## Overview

The Visual Workflow Builder has a strong UX foundation — plain-language labels, colour-coded node
types, dual-mode placement (click or drag), hover tooltips with I/O examples, and contextual
feedback (amber badges, toast notifications). The builder is already closer to the "child can use
it" standard than most diagram tools.

This feature closes the remaining gaps between the current implementation and a production-grade
first-use experience. It targets three areas:

1. **First-run clarity** — a blank canvas with no guidance, a broken example workflow (missing
   the mandatory Trigger node), and no inline workflow renaming leave first-time users confused
   before they have placed a single node.

2. **Interaction discoverability** — core interactions (double-click to configure, re-open chat
   after a canvas change, why Run is disabled) are invisible to the user until they already know
   where to look.

3. **Feedback completeness** — the unsaved-changes guard, live canvas-to-config-panel reflection,
   and post-run feedback chat integration are implemented as stubs or are missing entirely.

No fundamental redesign of the canvas model is required. All improvements are surface-level
additions to existing components.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — First-time user opens the builder and knows exactly what to do next (Priority: P1)

A business analyst opens `/workflow-builder` for the first time. They have no prior training.
Within 30 seconds, without reading documentation, they should understand what to do first, where
to start, and how the tool works at a high level.

**Why this priority**: The "child can use it" standard fails if a non-technical user reaches the
canvas and stares at it blankly. The current implementation auto-loads a 3-node example that
immediately shows an amber "Add a starting trigger" advisory — undermining confidence before the
user has done anything.

**Independent Test**: Present the builder to a first-time user with no instructions. Measure
time-to-first-node-placed and whether they reach a connected two-node workflow without asking
for help.

**Acceptance Scenarios**:

1. **Given** a user navigates to `/workflow-builder` with no workflow ID, **When** the page
   loads, **Then** they are presented with a choice between "Start from scratch" (empty canvas)
   and "Load the example" (the 3-node demo). The example variant includes a Trigger node so it
   is runnable without requiring additional steps.

2. **Given** a user selects "Start from scratch," **When** the empty canvas appears, **Then**
   a large, centred welcome state is visible on the canvas: an illustration, a one-sentence
   explanation ("Drag a step from the left panel onto the canvas to begin"), and a pulsing
   highlight on the Triggers category in the palette. The welcome state disappears as soon as
   the user places their first node.

3. **Given** a user selects "Load the example," **When** the canvas loads, **Then** the
   Trigger node is present (emerald colour, "Start here" label), the example workflow has no
   amber badges, and the Run button is enabled — proving the example is a complete, runnable
   demonstration.

4. **Given** an empty canvas with the welcome state visible, **When** a user drags or clicks
   any node from the palette, **Then** the welcome state is replaced immediately by the canvas
   node with no flash or layout shift.

---

### User Story 2 — User discovers how to configure a node without trial and error (Priority: P1)

A user has placed an "AI Step" node on the canvas. The amber "!" badge tells them something is
needed, but they do not know how to open the configuration. Currently the only affordance is a
browser `title` tooltip ("Double-click to configure") that appears only on hover — invisible on
first glance and non-existent on touch devices.

**Why this priority**: Configuration is the primary creative act in the builder. If users cannot
discover how to configure nodes, the workflow they build will be incomplete and the Run button
will remain disabled with no clear explanation for why.

**Independent Test**: Ask a first-time user to configure an "AI Step" node without giving them
any instructions. Measure whether they find the configuration panel without being told to
double-click.

**Acceptance Scenarios**:

1. **Given** a freshly placed node with the amber "!" badge, **When** the user views it,
   **Then** a small, visible "Tap to set up" or pencil-icon label is displayed on the node
   body — not just in the HTML title attribute — so the action is discoverable without hovering.

2. **Given** a user single-clicks a node (to select it), **When** the node is selected,
   **Then** a tooltip or inline callout appears for two seconds saying "Double-click to
   configure" — making the interaction explicit for users who stopped at a single click.

3. **Given** a user double-clicks a node, **When** the configuration panel opens, **Then**
   the panel opens with the first form field already focused so the user can begin typing
   immediately without an additional click.

4. **Given** a user types in the Goal field of an agentic node, **When** they type at least
   3 characters, **Then** the node's label on the canvas updates live to reflect the typed
   text — without waiting for a Save or Done button click. The panel button changes its label
   from "Save" to "Done" (indicating dismissal, not a separate persistence action).

---

### User Story 3 — User understands why the Run button is disabled (Priority: P1)

A user has placed two nodes and connected them. They click the Run button and nothing happens —
it is grey and disabled. The reason (a missing Trigger node, or an unconfigured node) is only
discoverable by hovering over the button on desktop, which reveals a tooltip.

**Why this priority**: A disabled action with no visible explanation is a dead end. Non-technical
users who hit an invisible wall are likely to abandon the tool rather than investigate.

**Independent Test**: Place two connected but unconfigured nodes with no Trigger. Without
hovering over anything, the user should be able to read on-screen why Run is not yet available.

**Acceptance Scenarios**:

1. **Given** the workflow is missing a Trigger node, **When** the toolbar is visible, **Then**
   the Run button shows an always-visible label beneath or beside it ("Needs a trigger to start")
   — not just in the hover title — so the reason is readable without interaction.

2. **Given** the workflow has a Trigger but one or more nodes have the amber "!" badge,
   **When** the toolbar is visible, **Then** the Run button label says "Configure all steps
   first" — distinguishable from the Trigger-missing case so the user knows which problem
   to solve.

3. **Given** all nodes are configured and a Trigger is present, **When** the Run button
   becomes enabled, **Then** it transitions from grey to green with a brief fade animation
   and any explanatory sub-label disappears — making the moment of "readiness" visible.

---

### User Story 4 — User renames their workflow without leaving the builder (Priority: P1)

A user has been building a workflow for 10 minutes. They realise it is called "Untitled Workflow"
and they want to give it a meaningful name. Currently, the toolbar shows the name as a static
`<span>` — there is no affordance to edit it in place. It is unclear where or whether renaming
is even possible.

**Why this priority**: Naming is part of the authoring experience. Forcing a user to go elsewhere
to rename a workflow they are actively editing breaks flow and is a common source of frustration
in creative tools.

**Independent Test**: Ask a first-time user to rename their workflow. Measure whether they find
the rename interaction without being told where to click.

**Acceptance Scenarios**:

1. **Given** the toolbar shows the workflow name, **When** the user clicks or taps the name,
   **Then** the name becomes an inline text input with the full name selected, ready to be
   overwritten.

2. **Given** the inline name input is focused, **When** the user presses Enter or clicks
   elsewhere, **Then** the new name is applied, the input reverts to a styled label, and the
   page title in the browser tab updates to reflect the new name.

3. **Given** the user clears the name entirely, **When** they press Enter or click elsewhere,
   **Then** the name reverts to its previous value and a brief tooltip says "A workflow must
   have a name" — the blank-name state is never persisted.

4. **Given** a new empty workflow is opened, **When** the page loads, **Then** the workflow
   name is shown as "Untitled Workflow" in an amber-highlighted editable state — making it
   immediately obvious that naming is expected and the field is interactive.

---

### User Story 5 — User navigates away from a workflow with unsaved changes and is warned (Priority: P1)

A user has made significant changes to a workflow. They click the browser's Back button or
navigate to the gallery. The current implementation lets them leave silently — all their work
is lost with no warning. The unsaved-changes guard is present in the code but is a stub that
approves navigation unconditionally.

**Why this priority**: Losing work with no warning is one of the most trust-destroying UX
failures in any authoring tool. This gap was explicitly acknowledged in the code but not yet
resolved.

**Independent Test**: Make a change to a workflow, then navigate to the gallery. Confirm the
warning appears and that cancelling navigation keeps the user on the builder with their changes
intact.

**Acceptance Scenarios**:

1. **Given** the user has made changes since the last save or auto-save, **When** they
   navigate away from the builder, **Then** a browser-native or in-app confirmation appears:
   "You have unsaved changes. Leave without saving?"

2. **Given** the confirmation appears, **When** the user clicks "Stay," **Then** navigation
   is cancelled and the user is returned to the exact canvas state they left.

3. **Given** the confirmation appears, **When** the user clicks "Leave," **Then** navigation
   proceeds and the workflow state is discarded for the session. Any auto-saved version
   remains recoverable from the gallery.

4. **Given** the user has made no changes since the last save, **When** they navigate away,
   **Then** no confirmation appears — the guard only fires when there is something to lose.

---

### User Story 6 — User reopens the chat panel and sees a prompt to update code after canvas changes (Priority: P2)

A user generated workflow code via the chat assistant. They then added a new node to the canvas
and want to regenerate. They click the Chat button, but the panel opens with the old conversation
— nothing in the UI indicates that the canvas has changed or that the code may need updating.
The `_chatPanel.NotifyWorkflowChanged()` call exists in code but produces no visible indicator.

**Why this priority**: The chat-to-code loop is the primary value proposition of the builder.
Silent divergence between the visual topology and the generated code is a quality problem that
erodes trust.

**Independent Test**: Generate code via chat, then add a new node to the canvas. Without opening
chat, verify the Chat button shows a visual change indicator. Open chat and verify the assistant
offers to regenerate.

**Acceptance Scenarios**:

1. **Given** workflow code has been generated in the chat panel, **When** the user modifies
   the canvas topology (adds a node, removes a node, or changes an edge), **Then** a small
   orange dot badge appears on the Chat toolbar button indicating "canvas has changed."

2. **Given** the Chat panel is opened when the canvas has changed since last code generation,
   **When** the panel opens, **Then** the assistant's first message in the updated session is:
   "Your workflow has changed since I last generated code — want me to update it?" with a
   single-click "Update code" button.

3. **Given** the user clicks "Update code," **When** the response arrives, **Then** the chat
   panel shows a diff-style view (lines added in green, removed in red) so the user sees only
   what changed rather than re-reading the entire output.

---

### User Story 7 — User completes a post-run feedback loop through the chat panel (Priority: P2)

After a workflow run, the "Did this do what you expected?" button on each node's output badge
opens the chat panel — but the panel opens empty. The current code acknowledges this with
a comment (T063). The user is left to type their own question about what went wrong.

**Why this priority**: The feedback loop between run output and chat refinement is what transforms
the builder from a code-generation tool into a learning system. Without pre-population, the
feedback button is a broken affordance that teaches users to ignore it.

**Independent Test**: Run a workflow, then click "Did this do what you expected?" on any node
output badge. The chat panel must open with a specific, relevant pre-populated message requiring
no additional typing from the user.

**Acceptance Scenarios**:

1. **Given** a node has completed execution (success or failure), **When** the user clicks
   "Did this do what you expected?", **Then** the chat panel opens and the message input is
   pre-populated with: "The '[Node Name]' step [succeeded/failed]. Its goal was: [GoalPrompt].
   It produced: [OutputSummary]. [Did it do what you expected? If not, describe what you
   expected.]" — ready to submit or edit.

2. **Given** the pre-populated message is visible, **When** the user submits it unchanged,
   **Then** the assistant acknowledges the output and offers a concrete suggestion for
   improving the node's configuration.

3. **Given** the pre-populated message is visible, **When** the user edits it before
   submitting, **Then** their edited version is sent, not the pre-populated template.

---

### User Story 8 — User finds and opens a saved workflow efficiently from the gallery (Priority: P2)

A user returns to the gallery after two weeks and has 12 saved workflows. Currently, they are
sorted by last-modified date with no search or filter capability. The thumbnail shows "No preview"
for all workflows because thumbnail generation is not yet implemented. Finding the right workflow
requires reading every name.

**Why this priority**: Gallery usability degrades with scale. Without thumbnails and search,
the gallery becomes a list of names — not meaningfully different from a plain file picker.

**Independent Test**: Load 8 workflows into the gallery and ask a first-time user to find
"the one that handles billing escalations." Measure time to open the correct workflow.

**Acceptance Scenarios**:

1. **Given** the gallery is open with workflows listed, **When** the page renders, **Then**
   each gallery card shows a generated visual thumbnail representing the workflow's node layout
   (e.g. a simplified SVG of coloured node rectangles connected by arrows, sized to fit the
   card). The "No preview" fallback is shown only for workflows saved before thumbnail
   generation was introduced.

2. **Given** the gallery has more than 5 workflows, **When** the page renders, **Then** a
   search input appears at the top of the gallery that filters cards in real time by workflow
   name as the user types. Search must update within 150ms of the last keystroke.

3. **Given** a search that matches zero workflows, **When** the search result is displayed,
   **Then** the empty state says "No workflows match '[search term]'" with a clear button
   to clear the search.

---

### User Story 9 — User learns the keyboard shortcuts without reading documentation (Priority: P3)

Power users and returning users want to be more efficient. Ctrl+Z, Ctrl+Y, Delete, and
Ctrl+S are implemented but invisible. A first-time user who discovers them by accident gets
a positive surprise; one who never discovers them loses efficiency they would have valued.

**Why this priority**: Discoverability of shortcuts is a quality-of-life improvement that
scales with usage — it becomes more valuable the more often the builder is used.

**Independent Test**: Ask a user who has used the builder 3+ times to name any keyboard
shortcut for the tool. If they cannot name one, the shortcuts are undiscoverable.

**Acceptance Scenarios**:

1. **Given** the toolbar is visible, **When** the user clicks or presses a "?" help icon
   in the toolbar's far-right corner, **Then** a compact floating panel lists all available
   keyboard shortcuts in plain language (e.g. "Undo last action: Ctrl+Z").

2. **Given** the shortcuts panel is open, **When** the user presses Escape or clicks outside
   the panel, **Then** the panel closes and focus returns to the canvas.

---

## Functional Requirements

### FR-01 New Workflow Entry Choice

- **FR-01.1** When the user navigates to `/workflow-builder` without a workflow ID **and has
  zero saved workflows**, the builder must present a two-option entry screen before showing the
  canvas: **"Start from scratch"** (empty canvas, no nodes) and **"Try the example"**
  (pre-loaded 3-node demo that includes a Trigger node). Once the user has at least one saved
  workflow, navigating to `/workflow-builder` without an ID opens an empty canvas directly —
  the entry choice is never shown again.
- **FR-01.2** The "Try the example" workflow pre-loaded by FR-01.1 must include a configured
  Trigger node, two downstream nodes (one agentic, one function), complete edge connections,
  no amber badges, and an enabled Run button. It must represent a complete, runnable workflow.
- **FR-01.3** The entry choice screen must not appear when the user navigates to
  `/workflow-builder/{id}` — a specific workflow ID always opens that workflow directly.

### FR-02 Empty Canvas Welcome State

- **FR-02.1** When the canvas has zero nodes, a welcome overlay is displayed centred within
  the canvas area. It must contain: a single-sentence instruction ("Drag a step from the left
  panel onto the canvas to begin"), and a visual indicator (pulsing glow or arrow) pointing
  toward the Triggers category in the node palette.
- **FR-02.2** The welcome overlay must disappear the moment the user places the first node — no
  animation delay, no click required to dismiss it.
- **FR-02.3** The welcome overlay must not reappear if all nodes are later deleted. It is a
  first-placement guide, not a persistent empty state. After all nodes are deleted from a
  previously populated canvas, a minimal "canvas is empty — drag a step to continue" text
  label (no illustration) replaces the full welcome overlay.

### FR-03 Node Configuration Discoverability

- **FR-03.1** Every unconfigured node (amber "!" badge) must display a small visible label or
  pencil icon on the node body itself reading "Set up" or equivalent. This label must be
  visible without hovering and without prior knowledge of the double-click interaction.
- **FR-03.2** When a user single-clicks a node to select it (without double-clicking), a
  callout or tooltip must appear for 2 seconds reading "Double-click to configure this step."
  The callout must not reappear for the same node within 60 seconds to avoid being annoying.
- **FR-03.3** When the configuration panel opens via double-click, the first form field must
  receive focus automatically (no additional click required to begin typing).
- **FR-03.4** For agentic nodes, the node's canvas label must update live as the user types
  in the Goal field — with a debounce of no more than 200 ms — so the canvas previews the
  typed text while the panel remains open.
- **FR-03.5** The configuration panel's action button must be labelled "Done" (not "Save"),
  communicating dismissal rather than a separate persistence action. The button closes the
  panel and applies the configuration; it does not trigger a separate network save.

### FR-04 Run Button Discoverability

- **FR-04.1** When the Run button is disabled, the reason must be displayed as always-visible
  text (not a hover tooltip) in the toolbar area immediately adjacent to the Run button. Two
  distinct messages must exist:
  - When a Trigger node is absent: "Needs a trigger to start"
  - When nodes are unconfigured: "Set up all steps first"
- **FR-04.2** When all pre-conditions are met and the Run button transitions from disabled to
  enabled, the button must visually animate from grey to green over 300 ms to draw the user's
  attention to the change.
- **FR-04.3** The explanatory text adjacent to the Run button must disappear when the button
  becomes enabled — it must not persist as visual noise once it is no longer actionable.

### FR-05 Inline Workflow Name Editing

- **FR-05.1** The workflow name displayed in the toolbar must be an interactable element. On
  click or tap, it transforms into an inline text input containing the current name (fully
  selected for easy overwrite).
- **FR-05.2** The inline input is committed by pressing Enter or by focus leaving the input.
  On commit, the browser tab title (`<title>`) updates to reflect the new name.
- **FR-05.3** If the committed name is empty or whitespace-only, the input reverts to the
  previous name and shows a brief one-second tooltip "A workflow name is required" adjacent
  to the field.
- **FR-05.4** For new, unsaved workflows, the toolbar must render the name field in an
  amber-highlighted editable state on first load — not as a passive label — to signal that
  naming is an expected action before the first save.

### FR-06 Unsaved Changes Navigation Guard

- **FR-06.1** The `OnLocationChanging` handler must track whether any canvas or configuration
  change has occurred since the last successful save (manual or auto-save). The "changed" state
  is set on any node add, node delete, edge add, edge remove, **committed node configuration
  update** (i.e. the user clicked Done in the config panel), or workflow settings update.
  Typing in the config panel without clicking Done does not set the flag. The state is cleared
  whenever the workflow is successfully saved.
- **FR-06.2** When the "changed" state is true and the user triggers navigation away from the
  builder, a confirmation modal must be presented before navigation proceeds. The modal must
  offer three options:
  - **"Save & Continue"** — saves the workflow immediately and then allows navigation to proceed.
  - **"Discard Changes"** — abandons uncommitted edits and allows navigation to proceed.
  - **"Cancel — keep editing"** — closes the modal and returns the user to the exact canvas
    state they left, with no navigation. No focus management is required since the user is
    returning to the builder.
- **FR-06.3** When the "changed" state is false (workflow is up to date), navigation must
  proceed without any confirmation — the guard must not fire unnecessarily.

### FR-07 Chat Panel Change Indicator

- **FR-07.1** After workflow code has been generated at least once in the current session, any
  subsequent change to the canvas topology (node or edge added or removed) must cause an orange
  notification dot to appear on the Chat button in the toolbar. The dot persists until the user
  opens the chat panel.
- **FR-07.2** When the chat panel is opened while the change dot is active, the assistant must
  automatically append a new message at the bottom of the conversation: "Your workflow has
  changed since I last generated code. Want me to update it?" with an inline "Update code"
  button.
- **FR-07.3** Clicking "Update code" triggers regeneration. The response must be rendered as
  a compact diff: only lines that changed are shown, each prefixed with `+` (green) for
  additions or `-` (red) for removals, with 3 lines of surrounding unchanged context visible
  above and below each changed block (grey, no prefix). A "Show full code" link beneath the
  diff expands the full regenerated file on demand.
- **FR-07.4** The orange dot is removed from the Chat button as soon as the panel is opened,
  regardless of whether the user acts on the regeneration prompt.

### FR-08 Post-Run Feedback Pre-Population

- **FR-08.1** When the user clicks "Did this do what you expected?" on any node's execution
  output badge, the chat panel must open and the message input must be pre-populated with a
  template message combining: the node's display label, its completion status (succeeded or
  failed), a short excerpt of its goal prompt (max 80 characters), and an excerpt of its
  output summary (max 80 characters). A prompt phrase follows: "Did this do what you
  expected? If not, describe what you wanted instead."
- **FR-08.2** The pre-populated message must be editable by the user before submission. If the
  user clears the pre-populated text and types their own message, their version is sent.
- **FR-08.3** After the user submits the feedback message (pre-populated or edited), the
  assistant must respond with a concrete suggestion for modifying the node's Goal field — not
  a generic acknowledgement.

### FR-09 Gallery Improvements

- **FR-09.1** At save time, the builder must attempt to generate an SVG thumbnail representing
  the workflow's node layout. The thumbnail must be a simplified schematic: each node is a
  colour-coded rounded rectangle (using the same colour palette as the canvas), connected
  by directional lines. Node labels are omitted in the thumbnail to keep it legible at
  card size. The SVG is stored with the workflow and displayed in the gallery card. If
  thumbnail generation fails for any reason, the save proceeds silently and the gallery card
  shows the existing "No preview" fallback — the failure is never surfaced to the user.
- **FR-09.2** When the gallery has at least one workflow, a search input is displayed at the
  top of the gallery page in a fixed position. It filters displayed cards by workflow name in
  real time, updating within 150 ms of each keystroke. The input is always present — it never
  appears or disappears based on workflow count.
- **FR-09.3** A zero-result search state shows: "No workflows match '[search term]'" and a
  "Clear search" button that empties the search field and restores the full gallery view.
- **FR-09.4** The gallery card footer must display a node type summary (e.g. "2 AI steps,
  1 approval, 1 notification") rather than a raw step count (e.g. "4 step(s)"), making the
  card more informative at a glance.

### FR-10 Keyboard Shortcut Panel

- **FR-10.1** A "?" icon button must be placed at the far-right end of the toolbar (after the
  Run/Stop button). Clicking it opens a floating panel listing all keyboard shortcuts available
  in the builder: Undo (Ctrl+Z), Redo (Ctrl+Y), Delete selected (Delete / Backspace), and Save
  (Ctrl+S — if wired). Each shortcut entry shows the key combination and a plain-language
  description of what it does.
- **FR-10.2** The shortcuts panel closes when the user presses Escape, clicks the "?" button
  again, or clicks anywhere outside the panel. Focus returns to the canvas on close.

---

## Success Criteria

1. **First-node placement time**: A first-time user (no training) places their first node on
   the canvas in under 60 seconds from the moment the page loads.
2. **Configuration discovery rate**: At least 90 % of first-time users open the node
   configuration panel without being given verbal instructions (measured via usability test
   with 5+ participants).
3. **Run-button readiness comprehension**: When the Run button is disabled, at least 90 % of
   users can state the reason without hovering — verified by reading the always-visible
   explanatory text.
4. **Unsaved changes protection**: Zero workflows are silently lost to accidental navigation
   — verified by automated test confirming the guard fires on every navigation attempt when
   the workflow has unsaved changes.
5. **Gallery thumbnail completeness**: 100 % of workflows saved after this feature ships
   show a generated SVG thumbnail in the gallery (no "No preview" cards for new saves).
6. **Chat change awareness**: After a canvas modification, the Chat button's orange dot
   appears within 500 ms on 100 % of tested changes.
7. **Post-run feedback pre-population**: The "Did this do what you expected?" button opens
   the chat panel with a pre-populated message within 300 ms on 100 % of clicks.
8. **Inline rename discoverability**: At least 80 % of users find the inline rename
   interaction without instructions.
9. **Workflow name reflection in tab**: The browser tab title matches the workflow name
   within 500 ms of committing a rename.

---

## Key Entities

| Entity | Description |
|--------|-------------|
| **Entry Choice Screen** | The two-option screen shown when navigating to the builder without a workflow ID. Presents "Start from scratch" and "Try the example." |
| **Welcome Overlay** | The first-time placement guide displayed on an empty canvas. Disappears on first node placement. |
| **Set-up Affordance** | The visible label or icon on an unconfigured node that signals the double-click interaction is available. |
| **Run Readiness Label** | The always-visible text adjacent to the Run button that explains why it is disabled. |
| **Inline Name Field** | The interactable workflow name in the toolbar that becomes an input on click. |
| **Change Tracker** | The internal state flag that tracks whether unsaved changes exist since the last save. |
| **Chat Change Dot** | The orange notification badge on the Chat button that signals a topology change since last code generation. |
| **Feedback Pre-Population Template** | The automatically assembled chat message combining node name, status, goal excerpt, and output excerpt. |
| **Workflow Thumbnail** | The SVG schematic generated at save time representing the node layout in miniature. |
| **Keyboard Shortcuts Panel** | The floating overlay listing all available key combinations. |

---

## Assumptions

1. The Trigger node's existing colour coding (emerald green), "Start here" sub-label, and
   placement-at-top-left behaviour are retained. No changes to the Trigger node's visual
   identity are required.
2. All other node type colours, icons, and palette categories from specs/003 and specs/004
   are retained — this spec does not redesign the node visual system, only adds discoverability
   signals.
3. Thumbnail generation is performed client-side by reading the current diagram's node models
   and generating a deterministic SVG — no server-side render is needed.
4. The "Did this do what you expected?" feedback mechanism accesses the `NodeExecutionState`
   that is already available via the run orchestrator — no new API contract is required.
5. The gallery search is a client-side filter over the already-loaded `_workflows` list — no
   additional API calls are introduced.
6. The inline workflow name field reuses the existing `_workflow.Name` binding and triggers
   the existing `OnSaveAsync` flow (or defers to auto-save); it does not introduce a separate
   "rename" API endpoint.
7. The unsaved-changes flag is client-side only — it does not survive a browser reload.
   Auto-save already runs every 60 seconds (FR-06.1 in spec/003), providing a recoverable
   state. The guard protects only intra-session navigation.

---

## Out of Scope

- Redesigning the node visual model, colour palette, or palette categories
- Adding new node types beyond those already defined in specs/003–004
- Real-time collaborative editing
- A node-type plugin or extension SDK
- Visual diff between saved workflow versions
- Touch-optimised gesture support (pinch-to-zoom, etc.)
- Accessibility improvements to the canvas itself (WCAG 2.1 AA for pointer-only interactions
  remains the accepted standard per spec/003 Success Criteria 9)
- Dark-mode toggle or theming options
