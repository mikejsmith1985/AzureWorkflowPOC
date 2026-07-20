# Feature Specification: Intelligent DoR Validation Workflow — Config-Driven, AI-Reviewed, HITL Resolution

**Feature Branch**: `feature/dor-validation-workflow`

**Created**: 2026-07-19

**Status**: Draft

**Input**: User description: replace the default/starter workflow in the Automation tab's Workflow Builder with
an Intelligent Definition-of-Ready (DoR) Validation Workflow, and enhance the builder so it can express and run
this workflow end-to-end. When a Jira ticket is created, the system reviews it against a Definition of Ready
using AI, auto-advances ready tickets, and — when a ticket falls short — opens a human conversation in a chat
channel to close the gaps, enforcing configurable SLAs with escalation before handing off to a human. Human-in-
the-loop (HITL) handling is critical and must be robust across service restarts.

## Context

The Automation tab (Workflow Builder + Gallery) lets an operator design node-graph workflows and "Make it real"
to run them on the Microsoft Agent Framework (MAF) engine. Today the only starter content is an in-memory
**"Example: Support Request Flow"** that loads for new workflows; there is no shipped, runnable, business-grade
default. Separately, the codebase already contains most of the executable building blocks — MAF workflow
execution with native HITL pause/resume (RequestPort + durable checkpointing + restart rehydration), structured
AI review, a Jira work-tracker adapter, outbound chat messaging, an encrypted connector-configuration store, and
append-only audit logs.

This feature composes those blocks into a single, opinionated, **config-driven DoR Validation Workflow** and
makes it the default the operator starts from — replacing the placeholder example. It also **enhances the
builder** so the workflow's node kinds (AI DoR review, human conversation, work-item update/transition, SLA-aware
notification) are first-class and easy to configure. The heart of the feature is **graceful, auditable human-in-
the-loop resolution**: when a ticket is not ready, the system does not simply fail — it converses with a human to
fix the gaps, escalates on SLA breach, and only then hands off, never losing the audit trail.

The guiding principles from the source specification are: **configuration over code** (every behaviorally
significant value is runtime configuration, changeable without redeployment), **AI-first review** (tickets are
judged by a language model against a live DoR document, not a hardcoded rule set), and **graceful degradation**
(clean escalation and human handoff when automation cannot resolve).

## Clarifications

### Session 2026-07-19

- Q: How are human replies captured for the conversation loop? → A: Via the **existing Slack MCP** connection
  (poll the thread over the current outbound gateway), extending the MCP server/gateway with a thread-read /
  history capability where needed — **no** separate Slack Events API app or new public inbound endpoint. This
  keeps a single Slack integration and token, and piggybacks on the wake cadence the SLA engine already requires.
- Q: What DoR document source(s) does this feature support? → A: Build a **source-type seam** with **`inline`
  (markdown in config) and `url` (authless fetch)** backends in this feature; **Confluence** and **SharePoint**
  are deferred behind the seam (additive later, no rework).
- Q: Is a dry-run / read-only mode in scope for this feature? → A: Yes — a **global dry-run flag** built in from
  the start. When enabled, the workflow logs the transitions, field updates, and messages it *would* perform
  without executing any write or sending any message, so AI accuracy can be validated before going live.
- Q: Metrics — dashboard in scope, or emit audit data and defer reporting? → A: **Emit the structured
  audit/metric data now; defer the reporting UI.** All eight operational metrics MUST be derivable from the
  recorded append-only audit + execution-event data; a dedicated analytics dashboard is a fast-follow.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ready ticket auto-advances (Priority: P1)

A newly created Jira ticket already meets the Definition of Ready. The workflow triggers on creation, reads the
ticket, evaluates it against the current DoR document with AI, finds every criterion satisfied, transitions the
ticket to the configured "ready" status, optionally posts a success notice, and records the outcome — with no
human involvement.

**Why this priority**: This is the smallest end-to-end slice that proves the trigger → hydrate → AI review →
Jira transition → audit path works against a real Jira instance. Everything else builds on it.

**Independent Test**: Create a well-formed ticket in the configured project; observe it move to the ready status
and an audit record tagged PASSED, with no message posted to the resolution channel.

**Acceptance Scenarios**:

1. **Given** a ticket that satisfies every DoR criterion, **When** it is created in a monitored project/issue
   type, **Then** the workflow transitions it to the configured ready status and writes a PASSED audit record.
2. **Given** success notifications are enabled, **When** a ticket auto-passes, **Then** a success message is
   posted to the configured success channel; **When** they are disabled, **Then** no message is posted.
3. **Given** a ticket outside the configured project or issue types, **When** it is created, **Then** the
   workflow does not act on it.

---

### User Story 2 - Not-ready ticket is resolved by conversation (Priority: P1)

A newly created ticket is missing information the DoR requires (e.g. no acceptance criteria). The workflow posts
a clear, specific message to the configured primary chat channel listing the gaps and linking the ticket, then
waits for a human reply. When a human answers, the AI re-evaluates the reply against the outstanding gaps; once
the gaps are closed, it updates the permitted ticket fields, transitions the ticket to ready, and records the
resolution.

**Why this priority**: This conversational HITL resolution is the core value of the feature — it turns a failed
check into a fixed ticket without a human leaving chat. It must survive service restarts during the (possibly
hours-long) wait.

**Independent Test**: Create a ticket missing acceptance criteria; confirm a gap message is posted to the primary
channel; reply in-thread with acceptance criteria; confirm the AI updates the ticket, transitions it to ready,
and records RESOLVED_AUTO — including if the service is restarted between the message and the reply.

**Acceptance Scenarios**:

1. **Given** a ticket that fails one or more DoR criteria, **When** the review completes, **Then** a message
   naming the specific unmet criteria and the ticket link is posted to the primary channel, and the workflow
   enters a waiting state.
2. **Given** the workflow is waiting for a reply, **When** a human replies in the thread and the reply resolves
   all outstanding gaps, **Then** the AI updates the permitted fields via the ticket system and transitions the
   ticket to ready, tagging the result RESOLVED_AUTO.
3. **Given** a reply resolves only some gaps, **When** it is evaluated, **Then** the workflow posts a focused
   follow-up asking only about the remaining gap(s) and increments the exchange count.
4. **Given** the service restarts while the workflow is waiting for a human reply, **When** it comes back up,
   **Then** the workflow resumes in the same waiting state and still processes the eventual reply.
5. **Given** the exchange count reaches the configured maximum without resolution, **When** the next timeout or
   reply is evaluated, **Then** the workflow exits to manual mode (see User Story 4) without transitioning the
   ticket.

---

### User Story 3 - SLA breach escalates, then hands off (Priority: P2)

A not-ready ticket's conversation is not resolved within the configured primary SLA. The workflow escalates:
it posts a context summary to the configured escalation channel (typically leads/management), starts a fresh SLA
clock and exchange budget, and continues the conversation there. If the escalation SLA or its exchange budget is
also exhausted, the ticket is tagged for manual intervention, a summary comment is added, and the workflow exits
without changing the ticket's status.

**Why this priority**: SLA-bounded escalation is what makes the automation safe to run unattended — it guarantees
a ticket cannot sit forever and that a human is pulled in on a predictable schedule.

**Independent Test**: Configure a short primary SLA; create a not-ready ticket and do not reply; confirm that at
SLA expiry an escalation message is posted to the escalation channel with a summary and a new clock starts;
exhaust the escalation limits and confirm the ticket is tagged manual-required with a summary comment and its
status is unchanged.

**Acceptance Scenarios**:

1. **Given** a waiting conversation, **When** the primary SLA elapses without resolution, **Then** the workflow
   posts an escalation message with a context summary to the escalation channel and begins the escalation loop
   with its own SLA and exchange limits.
2. **Given** the SLA clock, **When** it is measured, **Then** it starts at first outreach (not ticket creation)
   and — when configured for business hours — counts only configured working hours/days in the configured
   timezone.
3. **Given** the escalation SLA or exchange budget is exhausted, **When** the limit is reached, **Then** the
   ticket is tagged with the configured manual label, a summary comment is added, the status is left unchanged,
   and the result is recorded as MANUAL_REQUIRED.
4. **Given** a reply arrives after the reply-timeout but before the SLA elapses, **When** it is received, **Then**
   it is still processed as a valid response.

---

### User Story 4 - Clean manual handoff preserves the audit trail (Priority: P2)

When automation gives up (exchange or SLA limits reached at any level), the workflow closes the conversation
cleanly: it posts a final message explaining that manual action is required, tags the ticket, adds an internal
comment summarizing what was attempted and what remains, deliberately leaves the ticket status untouched for a
human to action, and writes a manual-exit audit record.

**Why this priority**: Graceful degradation is a stated principle — failure must never mean a silently dropped
ticket or a lost trail. This story makes every non-happy path end in a clear, documented human handoff.

**Independent Test**: Force a manual exit (exhaust limits); confirm the final channel message, the ticket label,
the internal summary comment, the unchanged status, and a MANUAL_EXIT audit entry describing attempts and
outstanding gaps.

**Acceptance Scenarios**:

1. **Given** any manual-exit condition, **When** it triggers, **Then** a final explanatory message is posted to
   the active channel and the ticket receives the manual label plus an internal summary comment.
2. **Given** a manual exit, **When** it completes, **Then** the ticket status is NOT transitioned and the audit
   log contains a record of the attempts, iterations, elapsed time, and outstanding gaps.

---

### User Story 5 - The DoR workflow is the default an operator starts from (Priority: P2)

Opening the Workflow Builder for a new workflow presents the **Intelligent DoR Validation Workflow** as the
starting content instead of the old "Example: Support Request Flow". The operator sees the full node graph —
trigger, AI review, decision branch, human conversation, escalation, ticket update, audit — laid out and ready
to configure and run.

**Why this priority**: The user explicitly asked for the placeholder example to be gone and this to replace it;
it is the on-ramp that makes the whole feature discoverable.

**Independent Test**: Open the builder for a new workflow; confirm the DoR workflow graph loads (not the support
example), with each node present and labeled, and that it can be saved and "Made real".

**Acceptance Scenarios**:

1. **Given** the builder is opened for a new/unknown workflow, **When** the canvas loads, **Then** it shows the
   DoR Validation Workflow graph and the "Support Request Flow" example is no longer offered anywhere.
2. **Given** the loaded DoR workflow, **When** the operator runs "Make it real", **Then** every node maps to a
   runnable step (including the human nodes) with no unrealized/placeholder nodes remaining.

---

### User Story 6 - Everything significant is configurable without redeploy (Priority: P2)

An operator changes DoR behavior — the ready status, monitored projects, watched and AI-editable fields, SLA
durations, iteration limits, chat channels, the DoR document source, and the AI prompts — through configuration,
and the change takes effect on the next ticket without restarting or redeploying. Secrets are referenced by name
and resolved from the vault, never entered into configuration in plaintext.

**Why this priority**: Configuration-over-code is a core principle and the difference between a demo and a tool a
delivery team can actually own and tune.

**Independent Test**: Change the ready status (or an SLA, or a channel, or the DoR document) in configuration;
create a new ticket; confirm the new value is used on that run without any restart.

**Acceptance Scenarios**:

1. **Given** a configuration change to any behaviorally significant value, **When** the next ticket is processed,
   **Then** the new value is in effect without an application restart or redeploy.
2. **Given** the DoR document is updated at its source, **When** the next ticket is reviewed (allowing for the
   configured cache window), **Then** the review uses the updated DoR without any code change.
3. **Given** any secret (API token, webhook secret, AI key), **When** configuration is inspected, **Then** the
   secret value never appears in plaintext and is resolved from the vault by reference at runtime.

---

### User Story 7 - Enhanced, easier builder for these node kinds (Priority: P3)

The Automation tab is easier to use and more feature-rich for the DoR workflow's node kinds: an operator can
configure the AI-review node (DoR source + prompt), the human-conversation node (channel, timeout, iterations),
the ticket-update node (field whitelist + transition), and the SLA/escalation behavior directly in the builder,
with clear guidance and validation before running.

**Why this priority**: The user asked to enhance the Automation tab, not just seed a graph. It raises the ceiling
from "view a canned graph" to "confidently tune the workflow", but the workflow runs correctly with defaults
before any of this polish.

**Independent Test**: In the builder, open each DoR node's configuration panel; confirm the relevant, validated
settings are editable with guidance, and that misconfiguration is flagged before "Make it real" completes.

**Acceptance Scenarios**:

1. **Given** a DoR workflow node, **When** the operator opens its configuration, **Then** the settings relevant to
   that node kind are presented with guidance and validated on save.
2. **Given** an incompletely configured DoR workflow, **When** the operator attempts to run it, **Then** the
   builder identifies exactly which nodes/settings are incomplete and blocks the run until resolved.

### Edge Cases

- **Duplicate ticket-created event** → the same ticket must not start two concurrent workflow instances; a
  duplicate is discarded idempotently.
- **Reply after reply-timeout but before SLA** → the reply is still processed (reply-timeout and SLA are
  independent; only SLA expiry forces escalation).
- **Partial resolution** → a reply that closes some gaps advances the conversation to the remaining gap(s) only,
  not a restatement of all gaps.
- **AI returns an unparseable/malformed result** → one bounded corrective retry; if still unusable, exit to
  manual mode with an explanation rather than acting on garbage.
- **Ticket system unavailable** on read/update/transition → bounded retries; on exhaustion, alert and exit to
  manual mode without a partial write.
- **AI provider unavailable** → bounded retries; on exhaustion, comment on the ticket, alert, and exit to manual
  mode.
- **DoR document unreachable** → use the cached copy with a logged warning; if no cache exists, exit to manual
  mode with a clear reason (never review against an empty DoR).
- **Chat platform unavailable** on send → bounded retries with optional fallback; otherwise log and exit to
  manual mode.
- **Service restart mid-wait** → the workflow resumes from its last persisted state and continues the
  conversation; no human input is lost.
- **AI proposes a field outside the permitted whitelist** → the update is rejected/stripped by the system; only
  whitelisted fields are ever written, regardless of what the model suggests.
- **Unsigned/invalid inbound webhook** → rejected before any processing.
- **No DoR criteria fail but required fields are empty** → treated as not-ready (empty/insufficient counts as a
  gap).

## Requirements *(mandatory)*

### Functional Requirements

**Trigger & hydration**

- **FR-001**: The workflow MUST start when a ticket is created in a configured project and (optionally) matching
  configured issue types; tickets outside that scope MUST be ignored.
- **FR-002**: Inbound trigger events MUST be authenticated (HMAC signature validated) before any processing, and
  invalid or unsigned events MUST be rejected.
- **FR-003**: On trigger, the system MUST fetch the full ticket and normalize the configured watched fields into
  a review payload, and MUST load the current DoR document from its configured source (honoring a configured
  cache window).
- **FR-004**: Duplicate trigger deliveries for a ticket that already has an active workflow instance MUST be
  discarded idempotently (no second concurrent instance).

**AI DoR review**

- **FR-005**: The system MUST evaluate the ticket against the DoR document using the AI review engine and MUST
  obtain a structured result: an overall PASS/FAIL, a per-criterion PASS/FAIL with reasoning, the list of
  missing/insufficient fields, and any AI-suggested field values.
- **FR-006**: The DoR criteria MUST come from the configured DoR document (not hardcoded in the application), so
  that changing the DoR document is the only change required to change what is evaluated.
- **FR-007**: Every AI review result MUST be recorded to the audit trail with the ticket identifier, timestamp,
  and (when enabled) the full model response.

**Pass path**

- **FR-008**: When all DoR criteria pass, the system MUST transition the ticket to the configured ready status
  via the ticket system's transition mechanism, MUST optionally post a success notification when enabled, MUST
  write a PASSED audit record, and MUST exit.

**Fail path — conversational HITL resolution**

- **FR-009**: When one or more criteria fail, the system MUST post a message to the configured primary chat
  channel that names the specific unmet criteria and links the ticket, and MUST enter a durable waiting state for
  a human reply.
- **FR-010**: Human-in-the-loop waiting/resume MUST be durable — the workflow MUST persist its state so that a
  service restart during the wait resumes the same waiting workflow and still processes the eventual reply. (The
  existing MAF human-in-the-loop pause/resume and checkpointing MUST be reused; a bespoke pause/resume MUST NOT
  be hand-rolled.)
- **FR-011**: The system MUST capture human replies from the conversation thread it started, treating any
  non-bot reply in that thread as human input except authors on a configured ignore list. Reply capture MUST
  reuse the existing Slack MCP integration (polling the thread over the current gateway, extended with a
  thread-read capability), not a separate Slack Events API app or a new public inbound endpoint.
- **FR-012**: On each human reply, the system MUST re-evaluate the reply against the outstanding gaps using AI and
  MUST determine whether all gaps are now resolved, which remain, and what field updates the resolution implies.
- **FR-013**: When a reply resolves all gaps, the system MUST update the ticket's permitted fields and transition
  it to the ready status; when a reply resolves only some gaps, the system MUST post a focused follow-up about
  the remaining gap(s) only.
- **FR-014**: The system MUST count exchanges (a sent message plus its evaluated reply, including timed-out
  unanswered messages) and MUST stop the loop when the configured maximum-iterations limit is reached, exiting to
  manual mode.
- **FR-015**: Reply-timeout and SLA MUST be independent timers: a reply-timeout expiry counts the iteration as
  expired but does not itself fail the workflow; only SLA expiry drives escalation.

**SLA & escalation**

- **FR-016**: An SLA clock MUST start at the first outreach message (not at ticket creation) and MUST be
  persisted durably so it is correct across restarts.
- **FR-017**: When the primary SLA elapses without resolution, the system MUST escalate: post a context summary to
  the configured escalation channel, reset the exchange counter, and begin an escalation conversation loop with
  its own SLA and maximum-iterations limits.
- **FR-018**: SLA time MUST be measurable as either wall-clock or business-hours; when business-hours is
  selected, only the configured working days/hours in the configured timezone MUST count toward the SLA.
- **FR-019**: When the escalation SLA or its iteration limit is exhausted, the system MUST perform a manual exit
  (FR-020) without transitioning the ticket status.

**Manual exit**

- **FR-020**: On manual exit (at any level), the system MUST post a final explanatory message to the active
  channel, apply the configured manual label to the ticket, add an internal comment summarizing what was
  attempted and what remains outstanding, MUST NOT transition the ticket status, and MUST write a MANUAL_EXIT
  audit record.

**Ticket update via agent**

- **FR-021**: All ticket writes MUST go through the ticket system's API (no direct datastore writes) and MUST be
  restricted to a strict, configured whitelist of AI-editable fields; the whitelist MUST be enforced by the
  system programmatically, never left to the model to self-limit.
- **FR-022**: When a resolution is applied, the system MUST add an internal comment summarizing the changes made
  and which DoR criteria were resolved, and MUST tag the ticket with the outcome (PASSED, RESOLVED_AUTO, or
  MANUAL_REQUIRED).

**Audit & observability**

- **FR-023**: The system MUST write an append-only resolution record for each workflow instance capturing the
  outcome source (auto / escalation / manual), elapsed duration, iteration count, and the fields changed; audit
  records MUST NOT be updatable or deletable.
- **FR-024**: The system MUST record enough structured audit/metric data that review volume and results,
  auto-resolution rate, escalation rate, manual-exit rate, resolution time, iteration-count distribution, and
  which DoR criteria fail most often are all **derivable** from it. A dedicated reporting/analytics dashboard is
  out of scope for this feature (fast-follow); emitting the underlying queryable data is the requirement here.

**Configuration**

- **FR-025**: Every behaviorally significant value — monitored projects/issue types, watched fields, AI-editable
  fields, ready status/transition, manual label, DoR document source and cache window, AI model/prompts,
  primary/escalation/success channels and their timeouts and iteration limits, SLA durations and business-hours
  settings, the dry-run flag, and audit options — MUST be runtime configuration that takes effect on the next
  ticket without an application restart or redeploy.
- **FR-026**: Secrets (ticket API token, chat tokens, webhook secret, AI key) MUST be referenced by name and
  resolved from the vault at runtime, and MUST NEVER appear in configuration, logs, or the datastore in
  plaintext.

**Builder integration**

- **FR-027**: The Workflow Builder MUST present the Intelligent DoR Validation Workflow as the default starter
  content for a new workflow, and the previous "Support Request Flow" example MUST be removed as a starter/default
  everywhere it appears.
- **FR-028**: The default DoR workflow MUST be fully realizable ("Make it real") with every node mapping to a
  runnable step — including the human-conversation node(s) mapping to the human-in-the-loop pause/resume
  mechanism — with no unrealized placeholder nodes remaining.
- **FR-029**: The builder MUST let an operator configure each DoR node kind (AI review, human conversation,
  ticket update/transition, SLA-aware notification) with node-appropriate settings, guidance, and validation, and
  MUST block a run while any required node configuration is incomplete.

**Resilience**

- **FR-030**: External dependency failures (ticket system, AI provider, DoR document source, chat platform) MUST
  be retried within bounded limits and, on exhaustion, MUST degrade gracefully to a manual exit with an
  explanatory audit entry and (where applicable) an operator alert — never a partial/corrupt ticket write and
  never a silent drop.
- **FR-031**: The persisted workflow state machine MUST advance through explicit states
  (created → reviewing → passed/failed → awaiting-response → sla-breach → escalated → updating → manual-exit →
  done) and MUST persist after every transition so any instance can resume after interruption.

**Operational modes**

- **FR-032**: The workflow MUST support a **global dry-run (read-only) mode**, controlled by configuration.
  When enabled, the workflow MUST perform reviews and evaluations normally but MUST NOT execute any external
  write — no ticket transition, no field update, no chat message — instead recording each intended action to the
  audit trail as a "would-do" entry. The dry-run gate MUST apply to every side-effecting step so no write can
  bypass it, enabling validation of AI accuracy before live writes are enabled.

### Key Entities *(include if feature involves data)*

- **DoR Workflow Instance**: One run of the workflow for one ticket. Carries the current state (per the state
  machine), the outstanding gaps, exchange counts (primary and escalation), the SLA clock start, the active
  channel/thread, and the outcome. Persisted after every transition so it survives restarts.
- **DoR Document**: The externally-owned source of truth for what "ready" means. Loaded through a **source-type
  seam** — `inline` (markdown held in configuration) or `url` (authless fetch of a published doc) in this
  feature, with `confluence`/`sharepoint` deferred behind the same seam — cached for a configured window, and
  injected into the AI review. A `url` source lets non-technical stakeholders own the document externally.
- **DoR Review Result**: The structured AI verdict for a ticket at a point in time — overall pass/fail, per-
  criterion pass/fail with reasoning, missing/insufficient fields, and suggested values. Recorded to audit.
- **Conversation Thread**: The chat thread the workflow owns for a ticket, in either the primary or escalation
  channel; the boundary within which human replies are captured.
- **DoR Configuration**: The six configuration namespaces (ticket integration, DoR document source, AI review
  engine, communication channels, SLA, audit/observability) plus the three AI prompt templates — the runtime
  source of truth for all behaviorally significant values; secrets referenced by name.
- **Resolution / Audit Record**: The append-only record of a workflow instance's outcome (source, duration,
  iterations, fields changed, result tag), plus per-step audit entries.
- **Ticket (Work Item)**: The Jira issue under review — read for the review payload, written only through the
  permitted-field whitelist, transitioned via the configured transition, tagged with the outcome.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A well-formed ticket created in a monitored project is automatically advanced to the ready status
  with no human involvement, and the outcome is recorded — demonstrated end-to-end against a real Jira instance.
- **SC-002**: A not-ready ticket is resolved entirely through a chat conversation — the human never leaves chat —
  and the ticket is updated (permitted fields only) and advanced to ready, with the resolution recorded.
- **SC-003**: A conversation in progress survives a service restart: after restart the workflow is still waiting
  and still processes the human's eventual reply, losing no input.
- **SC-004**: An unresolved ticket escalates to the escalation channel exactly when its SLA elapses (measured from
  first outreach; business-hours honored when configured), and a fully-exhausted case ends as a clean manual
  handoff with the ticket tagged and its status unchanged.
- **SC-005**: Changing any behaviorally significant value (status, SLA, channel, iteration limit, or the DoR
  document itself) takes effect on the next ticket with no restart or redeploy.
- **SC-006**: The AI never writes a ticket field outside the configured whitelist, regardless of what it suggests.
- **SC-007**: No secret value ever appears in configuration, logs, or the datastore in plaintext.
- **SC-008**: Opening the builder for a new workflow presents the DoR Validation Workflow (not the old support
  example), and it can be saved and fully "Made real" with no unrealized nodes.
- **SC-009**: Every workflow instance ends with an append-only audit record classifying the outcome (PASSED,
  RESOLVED_AUTO, or MANUAL_REQUIRED) with duration and iteration counts.

## Assumptions

- **Ticket system is Jira** for this feature (the source specification is Jira-centric and the existing adapter
  targets Jira Cloud). The design stays tracker-neutral where the existing adapter seam allows, but end-to-end
  proof is against Jira. ServiceNow and other trackers are out of scope here.
- **The primary chat platform is the one already configured** in the messaging connector (Slack via the existing
  MCP/webhook path, with Teams supported for approvals); the conversation loop is built on that platform's
  threading model.
- **Existing infrastructure is reused, not re-implemented** (Framework-First / constitution Article VII): MAF
  workflow execution and its native HITL pause/resume + durable checkpointing + restart rehydration; structured
  AI review; the Jira work-tracker adapter; outbound messaging; the encrypted connector-config + secret store;
  and the append-only audit/cost logs. New code fills only the documented gaps.
- **Documented gaps to be built**: reading a Jira issue into the review payload; a Jira status-transition
  capability; inbound chat thread-reply capture **over the existing Slack MCP** (extend the gateway with a
  thread-read capability — not a separate Events API app); a loadable DoR-document source; and durable SLA clocks
  with multi-tier escalation. (These do not exist today.)
- **Runtime configuration lives in the existing connector-configuration store** (non-secret JSON + encrypted
  secrets), extended with the DoR namespaces; static application settings act only as an optional first-run seed.
- **Dry-run / read-only mode is in scope** (FR-032): a global, config-controlled mode that logs intended
  transitions, updates, and messages without executing them, so AI accuracy can be validated before live writes
  are enabled.
- **The DoR workflow is delivered as a single opinionated default graph** in the builder; deep per-node builder
  UX polish (User Story 7) is additive and the workflow runs correctly with sensible defaults before that polish.
- **Business-hours SLA** defaults to a single configured timezone and working-days/hours window; holidays and
  multiple regional calendars are not modeled here.
- **One workflow instance per ticket**; concurrency across different tickets is expected and isolated.

## Out of Scope

- Trackers other than Jira (ServiceNow remains an intake source, not the DoR target here); Monday and others.
- Bidirectional sync or migration of existing tickets between systems.
- A full analytics/reporting dashboard for the DoR metrics (the underlying audit data is produced; rich reporting
  surfaces are a fast-follow — see FR-024).
- Redaction/PHI-handling pipelines for regulated content beyond excluding fields from the watched set.
- Confluence and SharePoint DoR-document backends (the source-type seam is built now with `inline` + `url`;
  these two are additive behind it later).
- Multi-region / holiday-aware SLA calendars.
- Replacing or removing the other existing executable pipelines (intake, phase-handler); this feature adds the
  DoR workflow as the builder's default and does not delete unrelated engine code.
- Per-project or per-workflow multiple concurrent DoR configurations (a single active DoR configuration per
  instance), consistent with the current single-active-tracker model.
