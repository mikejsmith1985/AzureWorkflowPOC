# Feature Specification: Node Realization — Convert Plain-Language Nodes into Production-Ready Agentic & Function Nodes

**Feature Branch**: `feature/node-realization`

**Created**: 2026-06-22

**Status**: Draft — clarified, ready for `/speckit-plan`

**Input**: User description: "Once a workflow is built with plain language there must be an
intuitive process to have the LLM convert the nodes into real agentic or function nodes that
can function in a real production scenario."

---

## Clarifications

### Session 2026-06-22

- Q: What does node realization produce, given the existing "generate Semantic Kernel code" flow?
  → A: **Executable per-node configuration**, consumed and run directly by the existing runtime —
  no source code shown to the business user. The code-generation chat remains a **separate,
  optional "export to code"** affordance and is not the production-readiness mechanism.
- Q: How much must the user approve before a node counts as realized? → A: **Review-required per
  node** — every proposed configuration must be explicitly accepted. A "bulk accept" convenience
  is allowed but still requires one explicit confirmation. No silent auto-accept.
- Q: What proof is required before a workflow is "production-ready"? → A: **Configuration
  completeness + connector health check.** A workflow is ready when every node is realized and
  valid, cross-node inputs/outputs are consistent, and all bound connectors pass a health check.
  A one-click test/dry-run is **offered but not mandatory**.

---

## Overview

Today a user composes a workflow on the canvas in **plain language**: each node carries a
human name (e.g. "Summarise the ticket") and, for AI and trigger steps, a free-text goal
description. That is enough to *describe intent* but not enough to *run for real* — the system
does not yet know which model an agent should use, what its operating instructions and output
shape are, which tools or connectors it may call, or — for function steps — which concrete
connector and operation a node maps to and how data flows in and out. The nodes are, in effect,
sticky notes: legible to a human, inert to the runtime.

This feature adds the missing bridge: an **intuitive, guided realization step** that turns each
plain-language node into a fully-specified, executable node the runtime can run in a real
production scenario. A business user clicks one obvious action ("Make this real" / "Set up for
production"), and the system uses the LLM to *propose* the concrete configuration for every node
— derived from the node's plain-language goal, its input/output labels, its neighbours in the
graph, and the connectors the workspace already has available. The user reviews each proposal in
plain language, adjusts or accepts it, and the workflow transitions from "draft" to
"production-ready." A node is only considered realized when every field the runtime needs is
present and validated.

The experience must feel like a knowledgeable assistant doing the heavy lifting — not like
filling in a technical form. The user should never have to hand-author a prompt, a JSON schema,
a connector binding, or a field map unless they choose to; the LLM proposes sensible defaults and
the user confirms in language they understand.

> **Decided (clarification 2026-06-22):** Node realization produces **executable per-node
> configuration** that the existing runtime/execution orchestrator runs directly — no source code
> is shown to the business user. The existing "generate Semantic Kernel code" flow
> (`IWorkflowCodeGenerator`) remains a **separate, optional "export to code"** affordance and is
> not the mechanism by which a workflow becomes production-ready.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Turn a finished plain-language workflow into a runnable one (Priority: P1)

A business analyst has laid out a support-triage workflow: a trigger, an "AI Agent" node labelled
"Summarise the customer's problem," a "Smart Branch" labelled "Is it urgent?", an "Ask a Person"
approval, and a "Notify" node labelled "Email the customer." Every node is plain language; none
can run yet. The analyst clicks **"Make it real."** The system works through the graph and, for
each node, proposes a concrete production configuration in plain language: the AI agent gets a
suggested instruction, the model it will use, and the shape of what it will produce; the Smart
Branch gets the conditions it will test; the Notify node is matched to the workspace's configured
email/Teams connector with the message fields filled from upstream output. The analyst reads each
proposal, tweaks two of them, accepts the rest, and the workflow flips to **"Ready to run."**

**Why this priority**: This is the core promise of the feature — without it, a plain-language
workflow can be drawn but never executed. It is the single most valuable outcome and is
independently demonstrable end-to-end.

**Independent Test**: Build a multi-node plain-language workflow, invoke realization, accept the
proposals, and confirm the workflow reaches a "ready to run" state and then actually executes
through the existing run flow without any further manual configuration.

**Acceptance Scenarios**:

1. **Given** a workflow whose nodes are all plain-language (no executable configuration), **When**
   the user invokes the realization action, **Then** the system produces a concrete, reviewable
   proposed configuration for every node that requires one, expressed in plain language.
2. **Given** the system has proposed configurations, **When** the user accepts them (with or
   without edits), **Then** each affected node is marked configured and the workflow's overall
   status advances to "ready to run" only once **all** nodes are realized and valid.
3. **Given** a fully realized workflow, **When** the user runs it, **Then** it executes through
   the normal run flow using the realized configuration, with no additional setup prompts.
4. **Given** a node the system cannot fully realize on its own (e.g. it needs a connector that is
   not set up, or the goal is too vague), **When** realization runs, **Then** that node is clearly
   flagged as "needs your input" with a plain-language explanation of what is missing — the
   workflow does **not** silently claim to be production-ready.

---

### User Story 2 — Review and adjust what the assistant proposed (Priority: P1)

The analyst does not blindly trust the machine. For the "Summarise the customer's problem" agent,
the assistant proposed an instruction and an output shape (a short summary plus a severity label).
The analyst wants the summary capped at three sentences and the severity limited to
Low/Medium/High. They open the node's realization proposal, see the suggestion in plain language,
edit the wording, and re-confirm. The change is reflected without having to re-realize the whole
workflow.

**Why this priority**: Trust and control are prerequisites for a business user to put an
LLM-authored configuration into production. A proposal the user cannot understand or steer is not
production-safe. Same realization flow as US1, viewed from the review/override angle.

**Independent Test**: Realize a workflow, open one node's proposal, change a value, accept, and
confirm only that node updated and the rest of the realized workflow is untouched.

**Acceptance Scenarios**:

1. **Given** a proposed node configuration, **When** the user opens it, **Then** every part the
   runtime will rely on is shown in plain language (what the step will do, what it will use, what
   it will produce) — not as raw code or unexplained technical fields.
2. **Given** the user edits a proposed value and re-confirms, **Then** the node stores the edited
   value and the rest of the workflow's realized nodes are unaffected.
3. **Given** the user rejects a proposal, **When** they ask for an alternative, **Then** the
   system offers a different proposal rather than forcing the original.
4. **Given** the user changes a node's plain-language goal after it was realized, **Then** the
   node is marked "out of date" and the user is offered a one-click re-realization of just that
   node.

---

### User Story 3 — Realize a single node without re-doing the whole workflow (Priority: P2)

The analyst added one new "Save to records system" node to an already-realized workflow. They
want to realize just that node, not re-run the entire workflow's realization (which would risk
disturbing the configurations they already reviewed).

**Why this priority**: Workflows evolve incrementally. Forcing a full re-realization on every
small change is disruptive and erodes trust in already-approved nodes. Important for real use but
secondary to the core whole-workflow flow.

**Independent Test**: On a realized workflow, add one node, realize only that node, and confirm
the previously realized nodes retain their exact configuration.

**Acceptance Scenarios**:

1. **Given** a workflow with a mix of realized and not-yet-realized nodes, **When** the user
   realizes a single node, **Then** only that node changes and all previously realized nodes keep
   their configuration unchanged.
2. **Given** a single-node realization, **When** it completes, **Then** the workflow's overall
   readiness status recalculates to reflect whether all nodes are now realized.

---

### User Story 4 — Be told honestly when something can't go to production yet (Priority: P2)

The "Email the customer" node needs an email/messaging connector, but none is configured in the
workspace. Rather than inventing fake credentials or silently marking the node done, the system
tells the analyst, in plain language, that this step needs a connector to be set up, and points
them to where to do it.

**Why this priority**: "Function in a real production scenario" demands honesty about gaps.
Quietly producing a configuration that cannot actually run is worse than refusing — it creates the
illusion of readiness. Required for trust; gated behind the core flow.

**Independent Test**: Remove/disable all messaging connectors, realize a workflow containing a
Notify node, and confirm the node is flagged "needs a connector," the workflow is not marked
production-ready, and the user is directed to connector setup.

**Acceptance Scenarios**:

1. **Given** a node whose realized form requires an external connector that is not configured,
   **When** realization runs, **Then** the node is marked "blocked — needs setup" with a
   plain-language reason and a path to resolve it.
2. **Given** any node is in a "blocked" or "needs your input" state, **Then** the workflow cannot
   be marked production-ready and the run action communicates clearly why.

---

### Edge Cases

- A node's plain-language goal is empty or contradictory → realization asks a clarifying question
  rather than guessing silently.
- The same workflow is realized twice → the second pass does not duplicate or corrupt the first;
  already-accepted nodes are preserved unless the user opts to re-realize.
- The LLM is unavailable mid-realization → partial progress is preserved, the user is told which
  nodes were and were not realized, and they can retry the rest.
- Two upstream nodes feed one downstream node with mismatched output shapes → realization surfaces
  the mismatch in plain language instead of producing an un-runnable mapping.
- A connector exists but is unhealthy (failing credentials) at realization time → treated as
  "blocked," consistent with US4.
- The user edits the graph (adds/removes an edge) after realization → affected nodes are marked
  out of date, per US2 acceptance #4.

---

## Functional Requirements

### FR-13 Realization Entry Point & Flow

- **FR-13.1** The builder must expose a single, obvious action to convert the current
  plain-language workflow into a production-ready one (a "make it real" / "set up for production"
  affordance), discoverable without documentation.
- **FR-13.2** Invoking realization must process every node that requires executable configuration
  and produce a **proposed** configuration for each, derived from that node's plain-language goal,
  its input/output labels, its position in the graph (upstream/downstream neighbours), and the
  connectors available in the workspace.
- **FR-13.3** Realization must show live progress as it works through the nodes, so the user
  understands the assistant is reasoning per node rather than hanging.
- **FR-13.4** The user must be able to realize the whole workflow at once (US1) and re-realize an
  individual node in isolation (US3) without disturbing other already-realized nodes.
- **FR-13.5** Changing a node's plain-language goal, type, or its connected edges after
  realization must mark that node "out of date" and offer one-click re-realization of just that
  node (US2 #4).

### FR-14 Agentic Node Realization (AgenticReason, Trigger context)

- **FR-14.1** For an AI/agentic node, realization must propose, in plain language: what the step
  will do (its operating instruction), which language model it will use, and the shape of the
  result it will produce for downstream steps.
- **FR-14.2** When a node's output drives downstream branching or mapping, the proposed result
  shape must be **structured** (a defined set of fields/values) rather than free text, so the
  runtime can act on it deterministically.
- **FR-14.3** If realizing the agent implies it should call a tool or connector (e.g. look
  something up), the proposal must name that capability in plain language and bind it to a
  configured connector, or flag it as "needs setup" if none exists (US4).
- **FR-14.4** The user must be able to edit any proposed agent instruction or output shape in
  plain language and re-confirm without writing code or schema by hand.

### FR-15 Function Node Realization (Route, Transform, Notify, Data, Human Approval)

- **FR-15.1** For a **Notify** node, realization must match the step to a configured messaging
  connector (email/Teams/etc.) and propose the message content and recipient mapped from upstream
  output — or flag "needs a connector" if none is configured.
- **FR-15.2** For a **Data** node, realization must propose the concrete read/write operation and
  the data source/binding, mapping inputs and outputs to the workflow's data — or flag missing
  setup.
- **FR-15.3** For a **Route/Branch** node, realization must propose the concrete conditions that
  select each outgoing path, expressed against the (structured) output of upstream nodes, with one
  condition per outgoing edge and a clear default/fallback path.
- **FR-15.4** For a **Transform** node, realization must propose how upstream data is reshaped
  into what the downstream node expects, surfaced as a plain-language input→output mapping.
- **FR-15.5** For a **Human Approval** node, realization must propose who is asked, what they are
  shown, and what their decision options are, mapped to the human-in-the-loop pause/resume the
  runtime already supports.
- **FR-15.6** For a **Trigger** node, realization must capture what real event or input starts the
  workflow and the shape of the initial data it provides to the first downstream node.

### FR-16 Review, Override & Safety

- **FR-16.1** Every proposed configuration must be presented for human review in plain language
  and **explicitly accepted** before that node counts as realized (review-required per node). A
  "bulk accept all proposals" convenience is permitted, but it must still require one explicit
  user confirmation — proposals must never be silently auto-accepted into production-ready state.
- **FR-16.2** The user must be able to accept, edit-then-accept, reject, or request an alternative
  proposal for any node.
- **FR-16.3** Realization must never fabricate credentials, endpoints, or connector bindings that
  do not exist; missing prerequisites must be surfaced as "blocked — needs setup" (US4).
- **FR-16.4** A workflow must reach "production-ready" status **only** when every node is realized,
  valid, and unblocked; any blocked or out-of-date node must prevent that status and be clearly
  indicated.
- **FR-16.5** Realization must be non-destructive to the plain-language layer: the original
  human-readable goal/label of each node is preserved alongside its realized configuration, so the
  user can always see and re-edit the intent.

### FR-17 Production-Readiness Validation

- **FR-17.1** Before a node is marked realized, the system must validate that every field the
  runtime requires for that node type is present and internally consistent (e.g. a branch has a
  condition per edge; a notify has a connector and recipient).
- **FR-17.2** The system must validate cross-node consistency: each node's expected input is
  satisfiable by the (structured) output of its upstream node(s), surfacing mismatches in plain
  language (Edge Cases).
- **FR-17.3** A workflow may be marked "production-ready" when, and only when: every node is
  realized and valid (FR-17.1), cross-node inputs/outputs are consistent (FR-17.2), and every
  bound connector passes a **health check**. A one-click test/dry-run of the realized workflow
  must be **offered** to the user but is **not** a mandatory gate for production-ready status.
- **FR-17.4** The readiness status and any blocking reasons must be visible at a glance on the
  canvas and reflected in whether the "run" action is enabled.

---

## Success Criteria

1. **Plain-language to runnable, hands-off**: A user who only ever typed plain-language node names
   and goals can produce a workflow that executes end-to-end **without manually authoring any
   prompt, schema, connector binding, or field map** — measured by completing US1 with zero
   hand-edits required (edits allowed but not necessary).
2. **Speed of realization**: For a typical 5–8 node workflow, the user gets reviewable proposals
   for all nodes within a single guided session (target: under ~2 minutes of assistant work),
   with visible per-node progress throughout.
3. **Reviewability**: 100% of proposed node configurations are presented in plain language a
   non-technical reviewer can understand and edit — verified by usability review with a
   non-developer who can correctly state what each node will do.
4. **Honest gating**: In 100% of cases where a required connector or input is missing, the node is
   flagged "blocked/needs setup" and the workflow is **not** marked production-ready — verified by
   test with connectors disabled (US4).
5. **Incremental safety**: Realizing or re-realizing a single node never alters the configuration
   of other already-accepted nodes — verified across at least 5 single-node realizations on a
   populated workflow.
6. **Executes for real**: A workflow marked "production-ready" by this feature runs through the
   existing execution flow and completes (or fails only for genuine runtime/data reasons, not
   missing configuration) — verified by running at least 3 distinct realized workflows.
7. **Edit-and-stick**: A user edit to any proposed configuration is preserved through acceptance,
   save, navigation, and re-open — verified end-to-end (and consistent with the persistence
   guarantees of specs 003 / node-realization follow-on).
8. **Out-of-date detection**: Changing a realized node's plain-language goal or its connections
   marks it out of date in 100% of tested cases, preventing stale config from silently shipping.

---

## Key Entities

| Entity | Description |
|--------|-------------|
| **Plain-Language Node** | The node as the user authored it: a human name/label plus, for AI/trigger nodes, a free-text goal. The expression of intent. Preserved after realization. |
| **Realized Node Configuration** | The concrete, executable specification the runtime needs to run a node: for agents — operating instruction, model, structured output shape, and any tool/connector bindings; for function nodes — the bound connector/operation and input→output mapping; for branches — per-edge conditions. Derived by the LLM, reviewed by the user. |
| **Realization Proposal** | A single LLM-generated, not-yet-accepted candidate configuration for one node, shown to the user in plain language for accept / edit / reject / regenerate. |
| **Node Realization Status** | Per-node state: *draft* (plain-language only) → *proposed* → *realized* (accepted & valid), plus *blocked* (needs setup), *needs-input* (too vague), and *out-of-date* (intent changed after realization). |
| **Workflow Readiness Status** | Aggregate state of the workflow: *draft* vs *production-ready*. Production-ready only when every node is realized, valid, and unblocked. |
| **Connector Binding** | The link between a realized function/agent node and a configured workspace connector (messaging, data, work-tracking, LLM). Never fabricated; absence is surfaced as blocked. |

---

## Assumptions

1. **Realization output is executable node configuration, not source code** (decided 2026-06-22).
   The existing "generate code" chat flow remains a separate export/inspection affordance and is
   not the delivery mechanism for production-readiness.
2. The runtime already supports executing the node types (trigger, agentic, route, transform,
   notify, data, human-approval) given complete configuration; this feature supplies that
   configuration, it does not introduce new runtime execution strategies.
3. Structured agent output, human-in-the-loop pause/resume, and connector configuration already
   exist in the platform and are the primitives realization binds to (per the project's framework
   constitution) — realization does not hand-roll parallel mechanisms for these.
4. Connectors are configured at the workspace level (existing connector configuration feature);
   realization consumes the set that already exists and never creates credentials.
5. Single-user authoring session (no concurrent multi-user realization of the same workflow in
   this release), consistent with the current builder.
6. The plain-language layer (node labels and goals) and its editing/persistence behaviour are
   provided by prior specs (003–006 and the node-text/persistence fixes) and are treated as a
   stable foundation here.
7. "Production scenario" means the workflow runs against the workspace's real configured
   connectors and chosen model(s); it does not imply multi-tenant deployment, scaling, or
   release-management concerns, which are out of scope.

---

## Dependencies

- Existing connector configuration (messaging, data, work-tracking, LLM) — realization binds to
  these and reports when they are missing.
- Existing workflow execution/run flow — consumes realized configuration to actually run.
- Existing structured-output and human-in-the-loop capabilities of the underlying framework.
- Plain-language node authoring + persistence (specs 003–006 and follow-on fixes).

---

## Out of Scope

- Authoring or managing connectors/credentials themselves (handled by the connector configuration
  feature; this feature only consumes and reports on them).
- Multi-tenant deployment, autoscaling, environment promotion, or release management of workflows.
- Concurrent multi-user / collaborative realization of the same workflow.
- Generating downstream **source code** as the means of production-readiness (decided out:
  today's code-gen flow is a separate, optional export and is unchanged by this spec).
- A marketplace/library of pre-built node templates (possible future work).
- Fine-grained model-parameter tuning UIs (temperature, token limits) beyond what a plain-language
  proposal needs; advanced users' raw overrides are a follow-on concern.
