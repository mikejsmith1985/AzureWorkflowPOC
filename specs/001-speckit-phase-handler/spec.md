# Feature Specification: Spec Kit Phase Handler

**Feature Branch**: `feature/speckit-phase-handler`

**Created**: 2026-06-14

**Status**: Draft

**Input**: User description: "Extend the existing Semantic Kernel Process Framework pipeline to act as a speckit phase handler. The pipeline currently processes support tickets through DoR validation with HITL. Add a new process that listens for HTTP POST signals from Forge Terminal indicating a speckit phase has completed. On receiving the signal, the pipeline reads the artifact files from the specs/NNN-feature-name/ directory, runs a validation step using Claude to summarize and flag any gaps, and writes a work item to Azure DevOps Boards. The work item type and content vary by phase — Specify creates an Epic, Plan creates Tasks, Implement creates a Bug/completion record. The pipeline only writes to Azure DevOps after receiving an approval signal from the Forge Terminal HITL decision card. It does not act autonomously. Maintain the existing SK Process Framework patterns already in the codebase — typed events, HITL via IExternalKernelProcessMessageChannel, structured LLM output via JSON schema."

## Clarifications

### Session 2026-06-15

- Q: When a Plan phase is approved, how are Task work items created from the plan artifacts? → A: One Task work item per planned unit of work found in the artifacts, each linked under the feature's Epic.
- Q: Should Plan and Implement work items link under the feature's Specify Epic, and what if no Epic exists yet? → A: Always link under the Epic; if the Epic does not yet exist, create it first so there are never orphaned work items.
- Q: Which Azure DevOps process does the target project use (this determines valid work item types)? → A: Agile — Epic, Task, and Bug are all valid types and map directly to the phase mapping.
- Q: If a phase-complete signal repeats for an already-handled feature+phase, what should happen? → A: Upsert — never create a duplicate, and never destroy prior content. The work item's structured fields (title, state, parent link) are set to current values (Azure DevOps retains every prior value as an immutable revision, so field changes stay auditable and recoverable), and the latest summary + flagged gaps are appended as a new timestamped comment in the work item's Discussion (append-only — prior comments are never modified).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Review a completed phase and approve creation of its work item (Priority: P1)

A development team uses a Spec-Driven Development tool that progresses a feature through
named phases (Specify, Plan, Implement). When a phase finishes, the team wants the phase's
output reviewed and, on their explicit approval, recorded as a tracking work item on their
project board — without anyone re-reading the raw artifact files by hand and without anything
being created automatically behind their back.

**Why this priority**: This is the core value loop and the minimum viable product. It proves
the end-to-end path — signal in, artifacts read, automated summary produced, human decision
captured, work item created only on approval. Every other story builds on it.

**Independent Test**: Send a "Specify phase complete" signal for a feature that has artifact
files on disk. Confirm that (a) a plain-language summary and a list of flagged gaps are
presented for review, (b) nothing is written to the board before a decision, (c) on approval an
Epic appears on the board with content derived from the artifacts, and (d) on rejection no work
item is created.

**Acceptance Scenarios**:

1. **Given** a feature directory containing the Specify-phase artifacts, **When** a
   "phase complete" signal for that feature and phase is received, **Then** the system reads the
   artifacts and produces a summary plus a list of flagged gaps for human review, and creates
   nothing on the board yet.
2. **Given** a phase awaiting review, **When** the reviewer approves it, **Then** the system
   creates exactly one work item of the type mapped to that phase and records the created work
   item's identifier and link.
3. **Given** a phase awaiting review, **When** the reviewer rejects it (or the review is never
   approved), **Then** no work item is created on the board and the outcome is recorded as
   rejected/abandoned.
4. **Given** a phase whose review is in progress, **When** no approval has yet been given,
   **Then** the board contains no work item for that phase.

---

### User Story 2 - Correct work item type and content per phase (Priority: P2)

The team wants each phase to map to the right kind of tracking artifact so the board reflects
the real shape of the work: a high-level container for the Specify phase, the breakdown of work
for the Plan phase, and a completion/defect record for the Implement phase.

**Why this priority**: Without per-phase mapping the integration only handles one slice of the
lifecycle. This makes the handler useful across the whole Spec-Driven Development flow.

**Independent Test**: Send a completed-phase signal for each of Specify, Plan, and Implement
(approving each) and confirm the board receives an Epic, a set of Task items, and a
Bug/completion record respectively, each populated from the matching artifacts.

**Acceptance Scenarios**:

1. **Given** an approved Specify phase, **When** the work item is created, **Then** it is an
   **Epic** summarizing the feature.
2. **Given** an approved Plan phase, **When** the work item(s) are created, **Then** one
   **Task** is created per planned unit of work described in the phase's artifacts.
3. **Given** an approved Implement phase, **When** the work item is created, **Then** it is a
   **Bug / completion record** capturing the outcome of the implementation.
4. **Given** a signal naming a phase the handler does not support, **When** it is received,
   **Then** the system records the signal as unsupported and creates no work item.

---

### User Story 3 - Traceability and safe re-signaling (Priority: P3)

The team wants the board to tell a coherent story for each feature: later phases should connect
back to the feature's top-level item, and an accidental or repeated signal for a phase that was
already handled must not litter the board with duplicates.

**Why this priority**: Improves the quality and trustworthiness of the board over time, but the
integration delivers value without it.

**Independent Test**: Create the Epic for a feature via the Specify phase, then handle that
feature's Plan and Implement phases and confirm their work items reference the Epic. Re-send an
already-approved phase signal and confirm no duplicate is created, the latest summary is appended
as a new comment, and prior content remains intact.

**Acceptance Scenarios**:

1. **Given** a feature whose Specify phase produced an Epic, **When** that feature's Plan or
   Implement phase work items are created, **Then** they are associated with the feature's Epic.
2. **Given** a phase that was already handled and approved for a feature, **When** an identical
   "phase complete" signal arrives again, **Then** no duplicate is created, the work item's
   current fields are refreshed, the latest summary and gaps are appended as a new comment, and
   prior comments and field revisions remain intact.

---

### Edge Cases

- **Missing or empty artifacts**: The named feature directory does not exist, is empty, or is
  missing the file(s) expected for the phase → the system reports the problem for review rather
  than fabricating content, and creates no work item.
- **Unsupported phase value**: The signal names a phase outside the supported set → recorded as
  unsupported, no work item created.
- **Board write fails**: The project board is unreachable or rejects the write after approval →
  the failure is recorded and surfaced; the approval is not silently lost.
- **Approval never arrives**: A review sits unanswered → the phase remains in an awaiting-review
  state and no work item is created; the run can be observed as pending.
- **Repeated / duplicate signal**: Handled per User Story 3 — the existing work item is upserted
  (current fields refreshed, latest summary appended as a new comment), never duplicated and
  never destroying prior content.
- **Malformed signal**: The signal is missing required fields (feature identifier or phase) →
  rejected with a clear reason, no run started.
- **Unauthorized signal**: A signal arrives without the expected shared authorization → rejected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST accept an inbound "phase complete" signal that identifies the
  feature (its artifact directory) and the completed phase.
- **FR-002**: The system MUST reject a signal that is missing required identifying information
  (feature reference or phase) or that fails the expected authorization check, without starting
  a run.
- **FR-003**: On a valid signal, the system MUST locate and read the artifact files for the
  named feature directory.
- **FR-004**: The system MUST produce, for human review, a plain-language summary of the phase's
  artifacts and an explicit list of flagged gaps, risks, or omissions.
- **FR-005**: The system MUST present the summary and flagged gaps to a human reviewer and pause
  for an explicit approve/reject decision before any work item is created.
- **FR-006**: The system MUST NOT create, modify, or write any work item to the project board
  before a human approval is recorded (no autonomous action).
- **FR-007**: On approval, the system MUST create a work item on the project board whose **type**
  is determined by the completed phase: Specify → Epic, Plan → Task(s), Implement →
  Bug/completion record.
- **FR-008**: On approval of a Plan phase, the system MUST create one Task work item per planned
  unit of work. Planned units are read from the feature's `tasks.md` when present; if `tasks.md` is
  absent (it is produced by a later Spec Kit phase), they are derived from the structural sections
  of `plan.md`.
- **FR-009**: The content of each created work item MUST be derived from the phase's artifacts
  (e.g., title and description), not from free-form invention.
- **FR-010**: On rejection, or while approval is outstanding, the system MUST NOT create any
  work item and MUST record the outcome (rejected or pending).
- **FR-011**: The system MUST record, for each handled phase, the outcome and any created work
  item identifier(s) and link(s) so the run is auditable after the fact.
- **FR-012**: The system MUST associate Plan and Implement work items with the feature's Epic so
  the board reflects the feature hierarchy. If the feature has no Epic yet (its Specify phase was
  never handled), the system MUST create the Epic first so that no work item is ever orphaned.
- **FR-013**: When a phase-complete signal repeats for a feature/phase that was already handled
  and approved, the system MUST upsert the existing work item(s) rather than create duplicates.
  Upsert means: set the structured fields (title, state, parent link) to the current values, and
  append the latest summary and flagged gaps as a new timestamped comment in the work item's
  Discussion — never overwriting prior narrative content.
- **FR-018**: Repeat-signal updates MUST be non-destructive: prior field values MUST remain
  recoverable through the work item's revision history, and prior summaries MUST remain visible
  as retained comments, so each work item carries an auditable version trail of every phase
  re-validation.
- **FR-014**: The system MUST handle a signal that names an unsupported phase by recording it as
  unsupported and creating no work item.
- **FR-015**: The system MUST record and surface a board-write failure after approval rather than
  discarding the approval silently.
- **FR-016**: The system MUST resolve all credentials and destination configuration for the
  project board from runtime configuration; no secret value is hard-coded or logged.
- **FR-017**: The existing support-ticket intake pipeline MUST continue to function unchanged
  alongside the new phase-handling capability.

### Key Entities *(include if feature involves data)*

- **Phase Completion Signal**: The inbound notification that a phase finished. Identifies the
  feature (artifact directory) and the completed phase; carries the authorization needed to be
  accepted.
- **Feature Artifact Set**: The files on disk for a feature (under its `specs/NNN-feature-name/`
  directory) that describe the output of each phase.
- **Phase Validation Report**: The human-readable summary plus the list of flagged gaps/risks
  produced from the artifacts, shown to the reviewer.
- **Approval Decision**: The recorded human choice (approve or reject) that gates any board
  write, originating from the reviewer's decision card.
- **Work Item**: The artifact created on the project board — an Epic, a Task, or a
  Bug/completion record — populated from the phase's artifacts and linked into the feature
  hierarchy.
- **Phase-to-Work-Item Mapping**: The rule set that determines which work item type and content
  shape a given phase produces.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of work items created on the board are preceded by a recorded human approval;
  zero work items are ever created without one.
- **SC-002**: For every supported phase signal, a reviewer can read the summary and the flagged
  gaps and reach an approve/reject decision without opening the underlying artifact files.
- **SC-003**: Each supported phase produces the correct work item type 100% of the time
  (Specify → Epic, Plan → Task(s), Implement → Bug/completion record).
- **SC-004**: Re-sending an already-handled phase signal results in zero duplicate work items;
  the existing work item is refreshed to match the latest artifacts while prior summaries and
  field values remain recoverable (100% non-destructive).
- **SC-005**: When artifacts are missing or the board write fails, the outcome is recorded and
  surfaced in 100% of cases — no silent loss of an approval or a failure.
- **SC-006**: After approval, the corresponding work item appears on the board within 30 seconds
  under normal conditions.
- **SC-007**: The pre-existing support-ticket pipeline continues to pass all of its existing
  behavioral checks after this feature is added.

## Assumptions

- **Reuses the existing pipeline foundation**: This capability is added as a new process
  alongside the current support-ticket intake pipeline, reusing the established run/observe,
  human-in-the-loop pause/resume, and structured-analysis patterns already in the codebase.
- **Approval channel**: The approve/reject decision originates from the Spec-Driven Development
  tool's human decision card and is delivered back to the system as an inbound signal that
  resumes the paused run (mirroring the existing human-in-the-loop resume mechanism).
- **Supported phases**: The handled set is Specify, Plan, and Implement. Other phase names are
  accepted but recorded as unsupported and produce no work item.
- **Plan granularity**: "Plan creates Tasks" means one Task work item per planned unit of work,
  read from the feature's `tasks.md` when present and otherwise derived from `plan.md` structural
  sections, each linked under the feature's Epic. (Confirmed 2026-06-15.)
- **Hierarchy linking**: Plan and Implement work items are always linked under the feature's
  Epic; if the Epic does not yet exist it is created first, so there are never orphaned work
  items. (Confirmed 2026-06-15.)
- **Idempotency key & behavior**: A handled phase is identified by feature reference + phase
  name; a repeat signal upserts the existing work item(s) — refresh the current fields and append
  the latest summary as a new Discussion comment — rather than creating duplicates. Prior values
  stay recoverable through Azure DevOps revision history and retained comments, so updates are
  non-destructive. (Confirmed 2026-06-15.)
- **Azure DevOps process**: The target project uses the **Agile** process, so Epic, Task, and Bug
  are all valid work item types and map directly to the phase mapping. (Confirmed 2026-06-15.)
- **Single destination**: There is one configured project-board destination (one organization /
  project / area) resolved from configuration for this POC.
- **Artifact location**: Artifacts live under the repository's `specs/NNN-feature-name/`
  directory tree, and the signal carries enough information to resolve which directory to read.
- **Authorization**: Inbound signals carry a shared secret/header consistent with the existing
  inbound-webhook authorization approach.

## Dependencies

- An external Spec-Driven Development tool that emits phase-complete signals and hosts the human
  decision card that returns the approve/reject decision.
- A reachable project-board service (Azure DevOps Boards) and valid credentials supplied via
  configuration.
- Read access to the repository's `specs/` artifact tree from the running system.
