# Feature Specification: Workflow Trigger Node, Directional Links & Node Deletion

**Feature Branch**: `feature/visual-workflow-builder`

**Created**: 2026-06-19

**Status**: Draft

**Input**: User description: "In the workflow builder we need a 'trigger' mechanism type to start
the workflow — is this what 'Smart Branch' is supposed to be? Also we need the ability to link
directionally and finally delete nodes when needed."

---

## Clarifications

### Session 2026-06-19

- Q: Is "Smart Branch" the same thing as a Trigger? → A: **No.** A **Trigger** is a dedicated
  start node with no input ports — it marks the single entry point of a workflow and defines
  *when* and *with what initial data* the workflow begins. A **Smart Branch** is a "Decisions &
  Routing" node (spec 003 FR-02.1) that uses AI reasoning to evaluate in-flight data and route
  execution down one of several labelled output paths. They solve fundamentally different
  problems: one starts the workflow; the other routes it mid-execution.

---

## Overview

Three targeted additions close gaps identified during the first Visual Workflow Builder
implementation sprint (specs/003):

1. **Trigger Node** — A first-class "Start" node type that every workflow must have exactly one
   of. It has no input ports, one or more labelled output ports, and is the only node that can
   occupy the entry position of the execution graph. Without it, the builder and the execution
   engine have no unambiguous starting point.

2. **Directional Connections** — Visual arrows between nodes already exist (spec 003 FR-03), but
   users must be able to read direction at a glance without hovering. This enhancement makes
   direction unambiguous through larger arrowheads, animated flow indicators during execution,
   and direction-aware port labelling so first-time users never connect nodes backwards by
   accident.

3. **Node Deletion** — Individual nodes (and their attached connections) must be removable from
   the canvas through two standard interactions: a keyboard shortcut (Delete/Backspace) and a
   right-click context menu. Multi-node deletion already exists in spec 003 FR-01.3; this spec
   fills the gap for single-node deletion with clear, undo-able behaviour.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — User places a Trigger node to define where a workflow starts (Priority: P1)

A business analyst opens the workflow builder to build a new automation. They want to define the
starting condition — "when a new support ticket arrives" — and connect that trigger to the first
processing step. They should immediately understand that the Trigger node is different from other
nodes (it has no inputs, it starts the flow) without reading any documentation.

**Why this priority**: Without a Trigger node, every workflow is topologically ambiguous — neither
the builder nor a downstream user can tell which node is the beginning. This is a correctness
blocker for code generation and execution.

**Independent Test**: Ask a first-time user to identify, place, and configure a Trigger node and
confirm they can connect it to a downstream node without hesitation and without reading the help
docs.

**Acceptance Scenarios**:

1. **Given** the node palette is open, **When** the user browses the palette, **Then** a
   "Triggers" category appears at the top of the palette list, containing at least one
   entry — a "Start / Trigger" node — with a plain-language description: "Marks where your
   workflow begins and what kicks it off."
2. **Given** a user drags a Trigger node onto the canvas, **When** it lands, **Then** the
   node renders with a visually distinct appearance (e.g. a play icon or a flag motif, green
   accent colour) and shows only output ports — no input ports — making it immediately clear
   that this is the entry point, not a middle step.
3. **Given** a canvas that already has one Trigger node, **When** the user tries to place a
   second Trigger node, **Then** the builder prevents the placement, shows a plain-language
   message ("Every workflow has exactly one starting trigger — this workflow already has one"),
   and leaves the existing Trigger node untouched.
4. **Given** a Trigger node on the canvas, **When** the user double-clicks to configure it,
   **Then** the configuration panel shows a plain-language "What starts this workflow?"
   text area and an "Initial data description" field explaining what information is available
   to the first downstream node — no technical schema editor.
5. **Given** a workflow that has no Trigger node, **When** the user attempts code generation
   or execution, **Then** the builder highlights the absence with an amber badge in the
   toolbar reading "Add a starting trigger to run this workflow" and does not proceed — this
   is the one hard block permitted in addition to the island-node rule from spec 003 FR-08.3.

---

### User Story 2 — User connects nodes and can tell at a glance which direction data flows
(Priority: P1)

A non-technical user has placed three nodes and drawn connections between them. They step back
to review the diagram and want to confirm they connected them in the right order without having
to hover over each arrow to read a tooltip.

**Why this priority**: Ambiguous direction is the primary source of user error when building
node graphs. If users can build backwards flows by accident, the generated code and execution
order will be wrong — destroying confidence in the tool.

**Independent Test**: Show a first-time user a five-node workflow diagram (printed screenshot,
no tooltips). Ask them to trace the path from start to finish. At least 90 % of participants
must trace the correct path on the first attempt.

**Acceptance Scenarios**:

1. **Given** a connection between two nodes, **When** the user views the canvas at any zoom
   level from 25 % to 200 %, **Then** the arrowhead at the target end of the connection is
   clearly visible without zooming in — at least 12 px wide at 100 % zoom — and the line
   includes a subtle mid-line directional accent (e.g. a chevron or animated dash) that
   reinforces direction without requiring the user to find the arrowhead.
2. **Given** a user begins dragging a connection from a node's port, **When** they are
   mid-drag, **Then** the line preview shows its arrowhead pointing toward the current mouse
   position so the user always knows which end is the "receiving" end before releasing.
3. **Given** a user attempts to drag a connection from an input port (the receiving side),
   **When** they begin the drag, **Then** the builder gently corrects the direction — either
   by auto-swapping to the nearest valid output port on the same node, or by showing an
   inline tip ("Connections start from an output port — the right side of a node") — so an
   accidental backwards connection attempt never succeeds silently.
4. **Given** a running workflow, **When** a connection is actively passing data between two
   nodes, **Then** the connection line animates in the direction of data flow (e.g. a
   travelling dot or pulse moving from source to target) for the duration of that transfer,
   making the execution path visually obvious without any user interaction.

---

### User Story 3 — User removes a node they placed by mistake (Priority: P1)

A user drags three nodes onto the canvas but realises the middle one is wrong. They want to
remove it cleanly — with its connections — in one interaction, and be able to undo if they
change their mind.

**Why this priority**: Without node deletion, users must clear the canvas and start over when
they make a mistake. This is a basic authoring capability that must exist from the first release.

**Independent Test**: Place a node connected to two others, then delete the middle node.
Confirm (a) both connections are removed automatically, (b) the two remaining nodes are
unchanged, and (c) pressing Undo restores the deleted node and both connections.

**Acceptance Scenarios**:

1. **Given** a single node is selected on the canvas, **When** the user presses the Delete or
   Backspace key, **Then** the node and all connections to or from it are removed in one action,
   with no confirmation dialog required (undo is available immediately).
2. **Given** a single node is selected, **When** the user right-clicks the node, **Then** a
   context menu appears with a clearly labelled "Delete node" option as the last item, with
   a destructive-action visual style (e.g. red text or icon).
3. **Given** a connected node is deleted by any method, **When** the deletion completes,
   **Then** any node that previously received a connection from the deleted node is unaffected
   in position and configuration — only the connection edges are removed, not the adjacent
   nodes.
4. **Given** a node has just been deleted, **When** the user presses the standard undo
   shortcut (Ctrl+Z / ⌘Z), **Then** the deleted node and all its former connections are
   restored to their exact previous positions and configuration state.
5. **Given** the user selects a Trigger node and attempts deletion, **When** they press Delete
   or choose "Delete node" from the context menu, **Then** the builder allows the deletion —
   the single-trigger constraint (User Story 1, scenario 3) only prevents placing a *second*
   Trigger; removing the only Trigger is permitted so the user can replace it with a different
   trigger type.

---

### User Story 4 — User understands the difference between Trigger and Smart Branch
(Priority: P2)

A user is building a workflow that must route differently based on whether an input is urgent or
routine. They open the palette looking for a "trigger" to start the urgency-check logic and are
unsure whether they want a Trigger node or a Smart Branch node.

**Why this priority**: Confusion between these two concepts is the specific question that
prompted this spec. The builder must make the distinction unmissable.

**Independent Test**: Show a first-time user the palette and ask them to point to "the node that
starts the workflow" and "the node that makes a decision based on what's in the data." Both
should be identified correctly on the first attempt by at least 85 % of participants.

**Acceptance Scenarios**:

1. **Given** a user hovers over the "Start / Trigger" node in the palette, **When** the
   tooltip appears, **Then** it reads: "Marks where your workflow begins. Every workflow has
   exactly one. Connect it to your first step." No mention of decision-making or routing.
2. **Given** a user hovers over a "Smart Branch" node in the palette, **When** the tooltip
   appears, **Then** it reads: "Asks the AI to read the current data and choose which path
   to take next. Use this in the middle of a workflow to split paths based on content." No
   mention of starting or triggering.
3. **Given** a user types "start" or "trigger" into the palette search, **When** results
   appear, **Then** the "Start / Trigger" node is the top result and "Smart Branch" does not
   appear in the results — the two concepts are disentangled at the search layer.
4. **Given** a user types "branch" or "decide" into the palette search, **When** results
   appear, **Then** Smart Branch appears in results and "Start / Trigger" does not.

---

## Functional Requirements

### FR-09 Trigger Node

- **FR-09.1** A new node category — **Triggers** — must appear at the top of the node palette,
  above all other categories. Its colour scheme must be visually distinct from both Agentic
  (warm) and Function (cool) nodes; use a strong accent colour (e.g. green or amber) so it
  reads as "the starting point" at a glance.
- **FR-09.2** The initial release must ship at least one Trigger node type: **"Start / Trigger."**
  This node has zero input ports and at least one output port labelled "Begin." Its
  configuration panel exposes two plain-language fields: "What starts this workflow?" (a text
  area describing the trigger condition in everyday language) and "What information is available
  at the start?" (a text area describing the initial data that downstream nodes can use).
- **FR-09.3** A workflow canvas may contain at most one Trigger node at any time. If a user
  attempts to place a second Trigger node, the attempt is blocked silently (the drag is
  cancelled, the palette node returns to its resting state) and an inline banner message
  explains the constraint in plain language. The block applies to all node types in the
  Triggers category, not just the specific "Start / Trigger" type.
- **FR-09.4** When code generation or execution is initiated and no Trigger node is present,
  the builder must block those actions with an amber indicator in the toolbar reading "Add a
  starting trigger to run this workflow" and a one-sentence instruction in the Run Output panel.
  This is the only new hard block permitted by this spec beyond the island-node rule
  (spec 003 FR-08.3).
- **FR-09.5** The canvas must render the Trigger node at a fixed "home" position (e.g. upper-
  left quadrant) when first placed, with a subtle "Start here" label below it, so new users
  understand the intended left-to-right reading direction of the graph without explicit
  instruction.
- **FR-09.6** The Trigger node's "What starts this workflow?" configuration text must be
  forwarded to the Chat Assistant as context whenever a new conversation is opened, so the
  assistant immediately knows what event drives the workflow and can frame its questions
  accordingly.

### FR-10 Directional Connection Display

- **FR-10.1** Every connection arrow must display a filled arrowhead at the target (receiving)
  end. At 100 % canvas zoom the arrowhead must be at minimum 12 px wide and 8 px tall — large
  enough to read without hovering. Arrowhead size must scale proportionally with zoom level
  so it remains legible from 25 % to 200 %.
- **FR-10.2** Every connection arrow must include a directional accent at its midpoint — either
  a static chevron glyph or a slow-moving animated dash pattern — oriented to reinforce the
  source-to-target direction. This accent must be visible at all zoom levels and must not
  obscure the connection's plain-language label.
- **FR-10.3** When a user begins dragging a new connection from a node, the live preview line
  must show its arrowhead pointing toward the current cursor position at all times during the
  drag, so the user always knows which end is the target before releasing.
- **FR-10.4** Dragging from an input port (the left side of a node) must not create a
  backwards connection silently. The builder must either (a) automatically re-interpret the
  drag as originating from the nearest output port on the same node, showing an inline tip
  to explain the correction, or (b) reject the drag start with an inline tip reading
  "Connections flow from the right side (output) to the left side (input) of a node." Either
  approach is acceptable; silent creation of a backwards edge is not.
- **FR-10.5** During workflow execution, any connection that is actively transmitting data must
  animate in the direction of flow — for example, a small travelling dot or a pulsing glow
  that moves from the source node to the target node. The animation must complete its travel
  in under one second per connection so the user perceives near-real-time flow. Animations
  must stop when execution completes or is cancelled.

### FR-11 Node Deletion

- **FR-11.1** A selected node must be deletable by pressing the **Delete** key or the
  **Backspace** key while the node (and not a text input field) has focus. The deletion must
  be instant — no confirmation dialog — and must be immediately undoable via the existing
  undo stack (spec 003 FR-01.4).
- **FR-11.2** Right-clicking a node must present a context menu. The context menu must include
  a **"Delete node"** option, visually styled as a destructive action (e.g. red label or trash
  icon). Choosing it must behave identically to the keyboard shortcut: instant deletion, no
  dialog, immediately undoable.
- **FR-11.3** When a node is deleted by any method, every connection edge attached to that node
  (both incoming and outgoing) must be removed at the same time as the node. Adjacent nodes
  must retain their positions and configurations; only their connection edges to the deleted
  node disappear.
- **FR-11.4** Undo of a node deletion must restore: the node itself at its exact previous
  canvas position, all configuration state that the node held at the time of deletion, and
  all connection edges that the node had at the time of deletion. The restoration must be
  indistinguishable from a state in which the node was never deleted.
- **FR-11.5** Multi-node deletion (selecting multiple nodes and pressing Delete) is already
  covered by spec 003 FR-01.3. This spec requires that the same undo guarantee (FR-11.4)
  applies to multi-node deletion: all deleted nodes and their edges are restored together by
  a single undo action.
- **FR-11.6** When the user selects a node and initiates a deletion, if the deleted node
  would leave one or more previously connected nodes as islands (no remaining connections),
  the builder must show an amber validation badge on those now-isolated nodes immediately
  after deletion — consistent with spec 003 FR-08.1 — so the user is alerted without any
  blocking modal.

---

## Success Criteria

1. **Trigger placement time**: A first-time user locates, places, and configures a Trigger
   node in under 60 seconds without consulting documentation.
2. **Single-trigger enforcement**: Attempting to place a second Trigger node is blocked
   100 % of the time, with the plain-language explanation displayed within 500 ms.
3. **Direction legibility**: At least 90 % of first-time participants correctly trace
   the execution path through a five-node diagram (static screenshot, no tooltips) on the
   first attempt.
4. **Node deletion**: Selecting a node and pressing Delete removes the node and all its
   connections within 200 ms, with no confirmation dialog.
5. **Deletion undo fidelity**: A deleted node and all its former connections are fully
   restored by a single undo action — verified for 100 % of tested deletion scenarios
   including multi-node deletion.
6. **Trigger–Smart Branch disambiguation**: At least 85 % of first-time users correctly
   identify which palette node starts a workflow and which makes a routing decision,
   using only the palette labels and tooltips (no external help).
7. **Backwards-connection prevention**: Dragging from an input port never silently creates
   a backwards edge — the builder either corrects or rejects the drag 100 % of the time.
8. **Execution flow animation**: A connection animating during execution completes its
   travel from source to target in under one second, verified by automated UI timing tests.

---

## Key Entities

| Entity | Description |
|--------|-------------|
| **Trigger Node** | A node with zero input ports and one or more output ports that marks the single entry point of a workflow. Every workflow must contain exactly one. |
| **Start / Trigger** | The default, general-purpose Trigger node type shipped in the initial release. Configured with a plain-language description of what initiates the workflow and what initial data is available. |
| **Smart Branch** | A "Decisions & Routing" agentic node (defined in spec 003 FR-02.1) that uses AI reasoning to evaluate in-flight data and route execution to one of several labelled output paths. It is **not** a trigger — it appears in the middle of a workflow, not at the start. |
| **Connection Arrow** | A directed edge linking a source output port to a target input port. Direction is expressed through an arrowhead at the target end, a midpoint directional accent, and animation during execution. |
| **Arrowhead** | The filled triangle at the target end of a connection arrow that communicates the receiving node — the direction-of-flow indicator visible without interaction. |
| **Context Menu** | A right-click menu on a node that exposes node-level actions including "Delete node." |

---

## Assumptions

1. The three additions in this spec are enhancements layered on top of the Visual Workflow
   Builder already specified in specs/003. All behaviours in spec 003 remain in effect; this
   spec adds to or refines them, never contradicts them.
2. The Trigger node category is extensible in intent — future sprints may add additional
   trigger types (e.g. "Schedule Trigger," "Webhook Trigger") — but only one trigger type
   ships in this release. The one-trigger-per-canvas constraint applies to the entire
   Triggers category, not just the default type.
3. The "Smart Branch" node referenced in the user's question is already captured under the
   "Decisions & Routing" category in spec 003 FR-02.1 as a branching function node. No new
   entity needs to be created for it; this spec only clarifies its distinction from the
   Trigger node.
4. Arrowhead sizing and animation values specified as numbers (12 px, 8 px, 1 second) are
   baseline targets that the implementation team may refine during visual QA — the intent
   (clearly visible without hovering; perceptibly fast animation) governs over the exact
   pixel values.
5. The deletion undo guarantee leverages the existing 50-step undo stack from spec 003
   FR-01.4. No new undo infrastructure is required.

---

## Out of Scope

- Additional trigger types beyond "Start / Trigger" (e.g. Schedule, Webhook, Event-based
  triggers) — deferred to a future release
- Animated tutorials or on-boarding overlays explaining the Trigger vs Smart Branch distinction
- Connection labelling enhancements (label styling, re-routing handles) beyond what is already
  specified in spec 003 FR-03.5
- Custom arrowhead styles or per-connection colour coding
- Batch/bulk deletion via a "Clear all" command (only per-node and multi-select deletion
  as defined in FR-11.1–FR-11.6)
