# Feature Specification: Visual Workflow Builder

**Feature Branch**: `feature/visual-workflow-builder`

**Created**: 2026-06-18

**Status**: Draft

**Input**: User description: "I want to include a UI for building new workflows with both agentic and function-based nodes. It should be incredibly easy — so simple a child can use it. It should include a chat function so the LLM can write the code for the agents after the user drag-and-drops all the nodes into the workflow pattern they want to build. Approach this from a master Architect with over 40 years of experience and a specialization in drag-and-drop UX experience."

---

## Clarifications

### Session 2026-06-18

- Q: Where does the initial input data come from when the user clicks "Run"? → A: A plain-language input form appears before execution starts. The user fills it out in natural language ("test with a support request about a billing error"). The LLM translates that description into the structured input the workflow requires — the user never sees or types raw data formats.
- Q: When a user opens the Workflow Gallery, which saved workflows do they see? → A: Personal only — each user sees only workflows they created. Shared/team visibility is out of scope for this release.
- Q: What is the maximum execution time before the builder auto-stops a running workflow? → A: Configurable per workflow, set in the workflow's own settings. A default timeout applies when the user has not changed it, ensuring no workflow can hang indefinitely without an explicit choice to allow a longer run.
- Q: Should the builder prevent cycles/loops in a workflow topology, or allow them? → A: Cycles are fully permitted — no hardcoded structural guardrails in the UI. Safety and correctness are enforced by the LLM through a built-in Workflow Design Skill that asks the user the right questions at design time and before execution. Per-workflow configuration (e.g. timeout, iteration limits) is set via the workflow settings form and submitted to the LLM as context. This keeps the builder maximally flexible and avoids baking brittle rules into the UI.
- Q: When the LLM is unreachable, which builder capabilities should remain available? → A: Canvas editing, saving, loading, and node configuration remain fully available. Chat, code generation, the Workflow Design Skill, and execution input translation each display a plain-language "assistant unavailable" message and disable their submit controls until connectivity is restored. The canvas is never held hostage by LLM availability.

---

## Overview

The Visual Workflow Builder gives any user — regardless of technical background — the ability to
design an automated workflow by dragging pre-built nodes onto a canvas and connecting them in
the order they should execute. Once the topology is arranged, a built-in chat assistant reads
the layout and generates complete, ready-to-use workflow code on demand. No syntax knowledge is
required to design; the designer is simply describing *what should happen and in what order*.

Two families of nodes are available:

- **Agentic Nodes** — nodes powered by an AI model that can reason, decide, generate text,
  summarize information, classify data, or converse with a user. The logic inside is
  AI-determined at run-time.
- **Function Nodes** — nodes that perform a predictable, rule-based operation: transform data,
  branch on a condition, call an external service, send a notification, or wait for a human
  decision. The logic inside is deterministic.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — First-time user builds a workflow without any training (Priority: P1)

A business analyst with no programming experience opens the workflow builder for the first time.
They want to create an automated process that reviews incoming requests, has an AI summarize
them, waits for a human to approve, and then sends a notification. They accomplish this entirely
through drag and drop — no configuration screen should require them to type code or understand
technical terminology.

**Why this priority**: This is the core value promise: "a child can use it." If a non-technical
user cannot complete a basic workflow without assistance, the feature has failed its prime
directive.

**Independent Test**: Have a first-time user with no training complete a three-node workflow
(one agentic node, one human-approval node, one notification node) and connect them in under
five minutes without consulting any documentation.

**Acceptance Scenarios**:

1. **Given** a user opens the builder for the first time, **When** they view the canvas,
   **Then** the node palette is visible, all node categories are labelled in plain language
   (no acronyms, no jargon), and at least one example workflow is pre-loaded on the canvas
   as a starting point they can modify or clear.
2. **Given** a user drags a node from the palette to the canvas, **When** the node lands,
   **Then** it snaps into a grid position, its name and a one-sentence plain-language
   description are visible without clicking, and a pulsing indicator shows which ports can
   accept a connection.
3. **Given** two nodes on the canvas, **When** the user draws a line from one node's output
   port to another node's input port, **Then** a labelled arrow appears showing the direction
   of flow, and the builder highlights any incompatible connection attempt *before* the user
   releases the mouse, preventing it rather than showing an error after the fact.
4. **Given** an incomplete workflow (e.g. a node with no outgoing connection), **When** the
   user attempts to proceed to the code-generation step, **Then** the builder highlights the
   unconnected nodes in amber and explains in one plain sentence what is missing — it does
   not block progress with a technical error code.

---

### User Story 2 — User generates working workflow code via the chat assistant (Priority: P1)

After arranging their nodes, a user opens the chat panel and asks the assistant to generate
the code that implements their workflow. They want to be able to describe in plain language any
behaviour that cannot be captured by the visual topology alone (e.g. "the AI node should
summarize in three bullet points" or "send the notification only on weekdays"), and receive
complete, runnable code in return.

**Why this priority**: The chat-to-code bridge is the feature's unique differentiator. Without
it, the builder is only a diagram tool. With it, it becomes a full workflow authoring system.

**Independent Test**: Arrange a four-node workflow on the canvas, open the chat, describe one
natural-language constraint not expressible as a visual connection, and confirm the generated
output is complete, syntactically correct, and reflects both the visual topology and the
chat-stated constraint.

**Acceptance Scenarios**:

1. **Given** at least two connected nodes on the canvas, **When** the user opens the chat
   panel, **Then** the assistant's opening message names each node in the user's workflow by
   its plain-language label (not an internal identifier) and asks what the workflow should
   accomplish — proving it has read the topology.
2. **Given** a user submits a plain-language description in the chat, **When** the assistant
   responds, **Then** the response contains a complete code block and a short plain-English
   summary of what was generated and why each node maps to the code it produced.
3. **Given** generated code is displayed, **When** the user clicks "Copy" or "Save to
   project," **Then** the action completes in under two seconds and the user receives a clear
   confirmation with the name of the saved file or the confirmation that the clipboard was
   updated.
4. **Given** a user asks the assistant a follow-up question (e.g. "can you add retry logic to
   the third node?"), **When** the assistant responds, **Then** it updates only the relevant
   portion of the previously generated code, preserves unchanged sections, and highlights the
   changed lines so the user can identify what changed without reading the entire output.

---

### User Story 3 — User configures a node's behaviour through a plain-language form (Priority: P2)

A user double-clicks an agentic node and wants to set its goal in their own words: "Summarize
the input in three bullet points and flag anything that mentions a deadline." They expect a
simple text field — not a prompt-engineering template or a JSON schema editor.

**Why this priority**: Configuring individual node behaviour without writing code is what
separates this builder from a code editor with a diagram attached. It must be accessible to
a non-developer.

**Independent Test**: Double-click each node type in the palette and confirm that every
configuration screen uses plain-language labels and contains no references to API parameters,
model names, or configuration keys.

**Acceptance Scenarios**:

1. **Given** a user double-clicks an agentic node, **When** the configuration panel opens,
   **Then** it presents exactly three fields visible without scrolling: a "Goal" text area
   (what should the AI accomplish?), an "Input label" field (what should this step be called
   in plain language?), and an "Output label" field (what does this step produce?).
2. **Given** a user types a goal in plain language, **When** they close the panel, **Then**
   the node's label on the canvas updates to a shortened version of the goal so the canvas
   stays readable.
3. **Given** a user double-clicks a function node, **When** the configuration panel opens,
   **Then** every configurable option is presented as a labelled control (dropdown, toggle,
   or text field) — never as a raw key-value pair or JSON editor.
4. **Given** a required configuration field is left empty, **When** the user closes the
   panel, **Then** the node on the canvas shows a small amber badge and a tooltip explaining
   in one sentence what is still needed, without preventing the user from continuing to build.

---

### User Story 4 — User saves, names, and reloads a workflow (Priority: P2)

A user has invested time building a workflow and wants to save it, give it a meaningful name,
and return to it in a later session to continue editing or generate updated code.

**Why this priority**: Without persistence, every session starts from scratch. This is table
stakes for a authoring tool.

**Independent Test**: Save a five-node workflow under a custom name, close the builder,
re-open it, and confirm the workflow appears in the saved-workflows list with its name, last-
modified date, and a visual thumbnail.

**Acceptance Scenarios**:

1. **Given** a workflow exists on the canvas, **When** the user clicks "Save," **Then** they
   are prompted for a name if the workflow has not been named, the save completes in under
   three seconds, and a visible "Saved" confirmation appears.
2. **Given** a saved workflow, **When** the user opens the builder in a new session, **Then**
   the workflow gallery shows the workflow with its name, a thumbnail preview of the canvas
   layout, and the date it was last modified.
3. **Given** a user opens a saved workflow, **When** the canvas loads, **Then** every node
   is in the exact position and configuration state it was in when saved, including any
   unsaved chat messages from the last session.
4. **Given** a user makes changes to a saved workflow, **When** they navigate away without
   saving, **Then** the builder presents a one-sentence prompt ("You have unsaved changes —
   save before leaving?") before allowing navigation.

---

### User Story 5 — User runs a workflow directly from the builder and observes results (Priority: P2)

After generating code for a workflow, a user wants to see it actually execute — watching each
step process in sequence — without leaving the builder, asking a developer to deploy anything,
or opening a separate terminal window. When a step fails, they want to understand exactly why
in plain language so they can fix the node configuration and re-run without starting over.

**Why this priority**: Closing the design-to-execution loop inside a single tool is the
capability that transforms the builder from a code-generation aid into a complete authoring
environment. It allows non-technical users to validate their workflows without developer
mediation.

**Independent Test**: Build a three-node workflow, click "Run," and confirm that: (a) each
node visually animates as it executes, (b) output appears beneath each node as it completes,
(c) introducing a deliberate misconfiguration in one node causes only that node and its
downstream nodes to show failure state, and (d) the plain-language failure reason appears in
the Run Output panel without any technical stack trace visible to the user.

**Acceptance Scenarios**:

1. **Given** a valid workflow with no blocking badges, **When** the user clicks "Run," **Then**
   a plain-language input form appears asking "What scenario should I test?" — the user types
   a natural-language description, the assistant displays a one-sentence confirmation of how
   it interpreted the input, and execution begins only after the user confirms. The "Run"
   button changes to "Stop" and no page navigation or reload occurs.
2. **Given** a workflow that is running, **When** a node completes successfully, **Then** its
   output appears as a collapsible badge beneath the node within one second of completion, and
   the animation moves to the next node in the flow.
3. **Given** a running workflow, **When** the user clicks "Stop," **Then** execution halts
   after the current in-progress node finishes its unit of work, all pending nodes are marked
   as "Skipped," and the "Run" button is restored — no data is lost from completed nodes.
4. **Given** a node that fails during execution, **When** the failure occurs, **Then** the
   node's badge turns red, the Run Output panel shows a plain-language sentence explaining
   what went wrong (e.g. "The 'Summarise Request' step could not process its input because
   no input arrived from the previous step"), and all downstream nodes show as skipped.
5. **Given** a completed run (success or failure), **When** the user clicks "Did this do what
   you expected?" on any node's output badge, **Then** the chat assistant opens and
   pre-populates a message describing that node's goal and actual output, ready for the user
   to add a correction in plain language.

---

### User Story 6 — User discovers available node types through guided exploration (Priority: P3)

A user opening the palette for the first time wants to understand what each node does before
committing to using it, without reading a separate help document.

**Why this priority**: Discoverability is a UX multiplier. A user who understands the full
palette designs richer workflows without needing support.

**Independent Test**: Ask a first-time user to identify the purpose of every node in the
palette in under three minutes using only what is visible in the builder itself.

**Acceptance Scenarios**:

1. **Given** a user hovers over a node in the palette, **When** a tooltip appears, **Then**
   it contains: the node's plain-language name, a one-sentence description of what it does,
   and a miniature example showing one input and one output — no technical terms.
2. **Given** a user clicks a node in the palette without dragging it, **When** the detail
   panel opens, **Then** it shows a short animated preview of the node processing a sample
   input and producing an output, taking no longer than five seconds to play.
3. **Given** nodes grouped in categories in the palette (e.g. "AI Steps," "Decisions,"
   "Notifications"), **When** a user collapses a category, **Then** the canvas does not
   change and no nodes already placed are affected.

---

## Functional Requirements

### FR-01 Canvas & Layout

- **FR-01.1** The canvas must support an unlimited number of nodes placed within an
  infinite-scroll workspace that the user can zoom (minimum 25 %, maximum 200 %) and pan
  with a mouse or touch gesture.
- **FR-01.2** Nodes must snap to an invisible grid with spacing no coarser than 16 px, with
  optional free-placement mode toggled by a clearly labelled button.
- **FR-01.3** Multi-select must allow the user to drag a rectangular selection area to choose
  multiple nodes at once, then move, copy, or delete the group in a single action.
- **FR-01.4** Undo and redo must support at least 50 steps and must be accessible via standard
  keyboard shortcuts and a visible toolbar button.
- **FR-01.5** The canvas must be responsive: the full editing experience must be usable on a
  1280 × 800 viewport without horizontal scrolling of the chrome (node palette, toolbar,
  chat panel).

### FR-02 Node Palette

- **FR-02.1** The palette must group nodes into named categories. The minimum shipped
  categories are: **AI Steps** (agentic nodes), **Decisions & Routing** (branching function
  nodes), **Human Steps** (approval/input gates), **Notifications** (output function nodes),
  and **Data** (transform/read/write function nodes).
- **FR-02.2** Each node entry in the palette must display: a colour-coded icon (warm tones for
  agentic; cool tones for function), a plain-language name (max 4 words), and a one-sentence
  description (max 15 words).
- **FR-02.3** The palette must include a search field that filters nodes in real time as the
  user types; results must update within 100 ms of the last keystroke.
- **FR-02.4** Any node in the palette must be placeable on the canvas by either drag-and-drop
  or a single click (click places the node at a smart default position on the visible canvas).

### FR-03 Node Connections

- **FR-03.1** Each node must expose named, labelled input ports (left side) and output ports
  (right side). Port labels must be plain language (e.g. "Approved," "Rejected," "Result").
- **FR-03.2** Connecting two ports must require only: hover the source port (pointer becomes
  a crosshair), drag to the target port, and release. No modal, no menu, no typing.
- **FR-03.3** The only connections blocked at the canvas level are physically impossible ones
  (e.g. output-to-output, input-to-input). Cycles, loops, and back-edges are fully permitted
  topologies — correctness of looping logic is handled by the Workflow Design Skill, not by
  the canvas. During a blocked-connection drag the target port is greyed out and the
  connection line turns red before the user releases.
- **FR-03.4** Removing a connection must be possible by clicking the arrow mid-line and
  pressing Delete, or by right-clicking the arrow and choosing "Remove connection."
- **FR-03.5** Connection arrows must auto-route to avoid overlapping nodes when the canvas
  layout changes; manual routing handles (bezier control points) must be accessible via a
  single click on the arrow.

### FR-04 Node Configuration

- **FR-04.1** Every node must have a configuration panel accessible via double-click. The
  panel must open as an inline sidebar, never a blocking modal that hides the canvas.
- **FR-04.2** Agentic node configuration exposes exactly three plain-language fields visible
  without scrolling: a required **Goal** text area ("What should the AI accomplish?"), an
  **Input label** field ("What should this step be called?"), and an **Output label** field
  ("What does this step produce?"). No prompt-template syntax, no model selection, no
  temperature slider — those are defaults the system manages.
- **FR-04.3** Function node configuration must expose only the options that are meaningful
  in plain language for that node type. A "Send notification" node exposes "Who to notify"
  and "Message template," not a webhook payload schema.
- **FR-04.4** All configuration changes must be applied immediately to the canvas preview
  (e.g. the node label updates as the user types the Goal) — no "Apply" button is required.
- **FR-04.5** Required configuration fields must be visually distinguishable from optional
  fields (e.g. with a subtle asterisk and a plain label "Required"). Leaving a required field
  empty must not block the user from designing the rest of the workflow.

### FR-05 Chat Assistant

- **FR-05.1** The chat panel must open as a resizable, dismissible sidebar that does not
  overlap the canvas unless the user explicitly maximizes it.
- **FR-05.2** On opening, the assistant must summarize the current workflow topology in plain
  language — naming every node and describing the flow — before asking the user for input.
- **FR-05.3** The assistant must accept natural-language instructions and generate complete
  workflow code from the combination of the canvas topology and the conversation history.
- **FR-05.4** Generated code must appear in a syntax-highlighted, read-only code block within
  the chat panel, accompanied by a plain-language explanation of what was generated and why.
- **FR-05.5** A "Copy to clipboard" button and a "Save to project" button must appear
  alongside every generated code block. "Save to project" must prompt the user for a file
  name if one has not yet been established.
- **FR-05.6** The chat panel must retain full conversation history for the current session.
  If a user modifies the canvas after code has been generated, the assistant must proactively
  note the change and offer to regenerate ("Your workflow has changed — want me to update
  the code?").
- **FR-05.7** The user must be able to ask follow-up questions in plain language to refine the
  generated code without starting a new conversation. The assistant must emit a diff-style
  view showing only what changed between the previous and updated code.
- **FR-05.8** The assistant must include a built-in **Workflow Design Skill** — an active
  mode that engages automatically before code generation and before execution. In this mode
  the assistant reviews the current topology and the workflow's configured settings, then asks
  the user a targeted sequence of plain-language questions to surface any logical gaps
  (e.g. "This step can loop back — how should it decide when to stop?"). Questions are asked
  one at a time; answers are incorporated into the generated code and persisted in the
  workflow's settings form. The skill must never block progress — if the user dismisses a
  question, the assistant records it as "user-deferred" and proceeds. All correctness
  enforcement is conversational, never a hard UI gate.
- **FR-05.9** When the LLM is unreachable, the chat panel, code generation, Workflow Design
  Skill, and execution input translation must each display a plain-language status message
  ("The assistant is currently unavailable — your canvas work is saved and you can continue
  designing") and disable their submit controls. All canvas, save, load, and node
  configuration features must remain fully operational. The builder must automatically restore
  LLM features without a page reload once connectivity is re-established, with no loss of
  canvas state or conversation history.

### FR-06 Workflow Persistence

- **FR-06.1** The builder must save the full workflow state (canvas layout, node positions,
  node configurations, chat history) automatically at regular intervals (at minimum every
  60 seconds) with a visible "Auto-saved" timestamp indicator.
- **FR-06.2** The user must be able to manually save at any time via a toolbar button and via
  a standard keyboard shortcut.
- **FR-06.3** Saved workflows must be listed in a gallery view accessible from the builder's
  home screen, showing: workflow name, thumbnail, last-modified date, node count. The gallery
  is personal — each user sees only workflows they created. No workflow created by another
  user is visible in this release.
- **FR-06.4** Workflows must be duplicatable from the gallery in one click, producing a copy
  with the suffix " (copy)" appended to the name.
- **FR-06.5** Deleting a workflow from the gallery must require a single explicit confirmation
  ("Delete [Workflow Name]? This cannot be undone.") — not multiple steps, not a trash/
  recycle-bin intermediate state.

### FR-06B Workflow Settings

- **FR-06B.1** Each workflow must have a settings panel (accessible from the toolbar) where
  the user can configure workflow-level properties without touching individual nodes.
- **FR-06B.2** The settings panel must include an execution timeout field, expressed in plain
  language (e.g. "Stop automatically after: 5 minutes"). The field must accept values in
  whole minutes from 1 to 60. A default of 5 minutes applies to all newly created workflows.
- **FR-06B.3** The currently configured timeout must be visible in the toolbar whenever the
  workflow is open, so the user is never surprised by an unexpected stop.
- **FR-06B.4** Workflow settings must be saved as part of the workflow's persisted state
  (FR-06.1) so they are restored on reload.
- **FR-06B.5** Answers provided by the user in response to Workflow Design Skill questions
  must be stored in the workflow's settings and pre-populated the next time the skill runs
  for that workflow, so the user is never asked the same question twice unless they change
  the topology in a way that makes a prior answer invalid.

### FR-07 In-Builder Workflow Execution

- **FR-07.1** A "Run" button must be accessible from the toolbar whenever the active workflow
  has no blocking (red) validation badges. Clicking it opens a plain-language input form
  before execution starts. The user describes the test scenario in natural language (e.g.
  "test with a support request about a billing error"); the assistant translates this
  description into the structured input the workflow requires, confirms the translation in
  one sentence, and then begins execution. The user never sees or types raw data formats.
- **FR-07.2** While a workflow is running, each active node must animate visually (e.g. a
  pulsing border) to show the user which step is currently executing. The user must be able to
  observe progress in real time without polling or refreshing.
- **FR-07.3** Execution output for each node must be displayed inline on the canvas (as a
  collapsible output badge beneath the node) and in full detail in a dedicated "Run Output"
  panel that opens alongside the chat panel.
- **FR-07.4** A running workflow must be cancellable at any time via a clearly labelled "Stop"
  button. Cancellation must halt execution cleanly — in-flight agentic steps are allowed to
  complete their current unit of work before stopping; they must not be abandoned mid-sentence.
- **FR-07.5** If a node fails during execution, its badge must turn red, the "Run Output" panel
  must display the failure reason in plain language, and all downstream nodes must be shown as
  skipped. The failure must not crash or reload the builder.
- **FR-07.6** After a run completes (success or failure), the user must be able to compare the
  actual per-node outputs against the node's configured goal — a one-click "Did this do what
  you expected?" prompt that feeds back into the chat assistant to suggest goal refinements.
- **FR-07.7** If execution reaches the workflow's configured timeout without completing, the
  builder must stop all remaining nodes, mark them as "Timed out," display a plain-language
  message ("This workflow took longer than [N] minutes — you can increase the timeout in
  Workflow Settings"), and preserve the output of any nodes that completed before the timeout.

### FR-08 Validation & Guidance

- **FR-08.1** Canvas-level validation is limited to objectively incomplete states: a node with
  no connections at all, a required configuration field left empty, or a physically impossible
  connection. These are surfaced as amber (warning) badges — never blocking modals. All
  structural and logical correctness concerns (loop termination, cycle safety, missing stop
  conditions) are the responsibility of the Workflow Design Skill (FR-05.8), not the canvas.
- **FR-08.2** Every canvas validation message must be written in plain language and end with a
  suggested corrective action in one sentence.
- **FR-08.3** The only hard block on code generation or execution is a node with no
  connections whatsoever (an island node). All other warnings are advisory — the user may
  proceed, with the Workflow Design Skill asking clarifying questions before the action
  completes.

---

## Success Criteria

1. **First-workflow time**: A first-time user with no training builds, connects, and submits
   a three-node workflow for code generation in under 5 minutes.
2. **Error-free first attempt**: At least 90 % of first-time users successfully connect two
   nodes on their first attempt without encountering a blocking error message.
3. **Code generation accuracy**: The generated code correctly implements the visual topology
   (all nodes present, correct execution order, chat constraints applied) in at least 95 % of
   generated outputs as measured by automated topology-to-code consistency checks.
4. **Round-trip fidelity**: A workflow saved and reopened in a new session is visually and
   behaviourally identical to the state at the time it was saved — verified for 100 % of
   tested workflows.
5. **Chat responsiveness**: The chat assistant produces a complete code response in under
   15 seconds for workflows containing up to 10 nodes, as measured on a standard development
   workstation.
6. **Node discoverability**: First-time users correctly identify the purpose of a randomly
   selected node from its palette entry alone (no tooltip) at a rate of at least 80 %.
7. **Execution visibility**: A running workflow's active node is visually distinguishable from
   idle nodes within 500 ms of execution reaching that node, as verified by automated UI tests.
8. **Execution failure transparency**: When a node fails during a run, the Run Output panel
   displays a plain-language failure reason within two seconds, with no technical stack trace
   visible to the user.
9. **Accessibility**: The canvas is pointer-only interaction; this is the accepted pattern for
   complex visual editors per WCAG 2.1 Advisory Techniques. All non-canvas builder surfaces —
   palette, chat panel, configuration panels, and toolbar — must meet WCAG 2.1 Level AA.

---

## Key Entities

| Entity | Description |
|--------|-------------|
| **Workflow** | A named, directed graph of nodes and connections representing a complete automated process, along with its associated chat conversation and generated code artifacts. |
| **Agentic Node** | A node whose internal behaviour is driven by an AI model at run-time. Its configuration is a plain-language goal, not code. |
| **Function Node** | A node that performs a deterministic, rule-based operation. Its behaviour is fully determined by its configuration at design time. |
| **Port** | A named connection point on a node. Input ports receive data/control from an upstream node; output ports emit data/control to a downstream node. |
| **Edge (Connection)** | A directed link from a source port to a target port, representing the flow of data or control between two nodes. |
| **Node Palette** | The categorised catalogue of all available node types, displayed as a sidebar. |
| **Canvas** | The infinite, zoomable, pannable workspace on which the user places and connects nodes. |
| **Chat Assistant** | The conversational interface embedded in the builder that reads the topology, accepts natural-language instructions, and emits workflow code. |
| **Generated Code** | A complete, executable workflow implementation produced by the Chat Assistant from the canvas topology and conversation history. |
| **Workflow Gallery** | The home-screen listing of all saved workflows, with name, thumbnail, and metadata. |
| **Workflow Settings** | Per-workflow configuration properties (e.g. execution timeout, iteration limits) stored alongside the workflow and editable without touching individual nodes. Also the target for answers captured by the Workflow Design Skill. |
| **Workflow Design Skill** | A built-in LLM skill embedded in the Chat Assistant that activates before code generation and execution. It reviews the topology and asks targeted plain-language questions to surface logical gaps; answers are woven into the generated code and stored in Workflow Settings. All enforcement is conversational — never a hard UI block. |

---

## Assumptions

1. The workflow builder is embedded within the existing web application rather than being a
   standalone tool, allowing it to share authentication and project context without requiring
   users to log in again.
2. Code generation targets the project's established workflow framework and conventions — the
   chat assistant produces code that is immediately compilable within the existing codebase
   without requiring additional scaffolding.
3. Node types in the initial release map directly to the step types already in use in this
   project's workflow framework; no new runtime primitives need to be built before the builder
   can generate valid code.
4. Workflow storage uses the same storage infrastructure the project already employs for other
   persistence (files or cloud blob storage) — no new storage backend is introduced.
5. The builder supports a single active user per session; real-time collaborative editing
   (multiple users editing the same canvas simultaneously) is out of scope for this release.
   Saved workflows are owned by the creating user and are not visible to other users.
6. The initial node palette is curated and fixed; a node-type plugin/extension mechanism is
   desirable but deferred to a future release.

---

## Out of Scope

- Real-time multi-user collaborative editing of the same canvas
- A node-type plugin or extension SDK (building custom node types)
- Visual version history / diff between workflow versions (save history is retained but
  not visually browseable in this release)
- Exporting a workflow as an image or PDF
- Theming or white-labelling of the builder UI
