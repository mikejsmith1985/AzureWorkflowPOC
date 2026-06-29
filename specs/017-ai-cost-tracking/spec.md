# Feature Specification: Two-Dimensional AI Cost Tracking on the Work Hierarchy

**Feature short name**: ai-cost-tracking
**Feature directory**: `specs/017-ai-cost-tracking`
**Created**: 2026-06-29
**Status**: Draft (ready for clarify/planning)

## Why

Leadership needs to see **how much AI a unit of work costs** and balance that against the features,
initiatives, and projects it belongs to. Two distinct kinds of AI spend exist and must be tracked
**separately** yet roll up the **same** work hierarchy:

- **Runtime spend** — AI the *product pipeline* consumes when it runs (validation, workflow execution).
- **Development spend** — AI the *engineers* consume building the work (coding-agent sessions).

Today neither rolls up: runtime cost lands on a single work item as a snapshot, and development cost
isn't captured at all. The missing piece is a **durable binding** that ties every cost record to a
ticket, plus a **rollup** along the ticket → feature → initiative → project tree.

## Clarifications

### Session 2026-06-29

- Q: Binding-key form — reuse the ADO work-item id, or mint a source-neutral token? → A: Mint a
  **source-neutral binding token** at ticket creation and stamp it on every system's ticket record
  (ServiceNow + ADO), so either system's number resolves to the same key.
- Q: When one run produces multiple tickets, how is its cost attributed? → A: To a **single primary/
  anchor work item** (the Epic for a Plan run; the single item for Specify/Implement) — never split or
  duplicated across the children.
- Q: How is a coding session that spans multiple tickets handled? → A: **Session-level single binding** —
  a session is bound to one ticket; to work a different ticket the developer starts a new session (or
  re-declares, rebinding the whole session). Per-segment splitting is out of scope for v1.

## User Scenarios & Testing

### Primary scenario
As an engineering leader, I open a Feature (or Initiative, or Project) and see its **total AI cost**,
split into **runtime** and **development**, equal to the sum of all the tickets beneath it — so I can
weigh AI spend against the value of that initiative.

### Acceptance scenarios
1. **Given** a ticket that the pipeline processed and that engineers worked on, **when** I view it,
   **then** it shows a runtime AI cost and a development AI cost, each a cumulative total across all
   runs/sessions for that ticket.
2. **Given** a Feature with several child tickets, **when** I view the Feature, **then** its AI cost
   (each dimension) equals the sum of its descendants' costs — no double counting.
3. **Given** a ticket is re-processed or re-worked, **when** new cost is recorded, **then** the ticket's
   cumulative cost **increases** (it never resets to the latest run only).
4. **Given** a single pipeline run that produces several tickets, **when** its cost is recorded, **then**
   that cost is counted **once** across the affected tickets, not duplicated onto each.
5. **Given** a coding-agent session that supplies a valid binding key, **when** it ends, **then** its
   spend is attributed to the correct ticket; **given** a session with no resolvable key, **then** the
   spend lands in a quantifiable **"unattributed"** bucket rather than being silently dropped.

### Edge cases
- A ticket has not yet passed Definition of Ready → it has no binding key → it cannot receive
  development spend (and cannot be assigned).
- A session names a binding key that doesn't resolve (typo, not-yet-ready) → unattributed bucket.
- One session spans multiple tickets → the session stays bound to its single declared ticket; the
  developer starts a new session (or re-declares) to attribute work to a different ticket.
- The same ticket exists in more than one source system → one binding key spans all of them.

## Requirements (Functional)

- **FR-001**: The system MUST assign a **canonical binding key** to every ticket at creation and record
  it on the ticket in a durable, queryable location.
- **FR-002**: Passing **Definition of Ready MUST require** a valid binding key; a ticket without one
  cannot be marked ready or assigned.
- **FR-003**: A **supplied** binding key MUST resolve back to **exactly one** ticket, independent of
  source system. (Boundary: the **system** resolves a key it is given; the **coding agent/tooling**
  derives that key from its context — e.g. the branch name — and supplies it. The app does not parse
  branches; see [[FR-005]] and the org-rollout runbook.)
- **FR-004**: The system MUST capture **runtime AI spend** per pipeline run and associate it with the
  binding key of the ticket the run produced or acted on.
- **FR-005**: The system MUST capture **development AI spend** from coding-agent sessions and associate
  all of a session's spend with the **single binding key** that session declares (one binding per
  session; switching tickets requires a new session or a re-declare that rebinds the whole session).
- **FR-006**: Every captured cost record MUST be tagged with its **dimension** (runtime vs development)
  so the two are reportable independently and combined.
- **FR-007**: Per-ticket cost MUST be **cumulative** across all runs/sessions — recording new cost adds
  to the ticket's total; it never overwrites.
- **FR-008**: When one run produces multiple tickets, its cost MUST be attributed to a **single
  primary/anchor work item** (the Epic for a Plan run; the single item for Specify/Implement) — never
  split across or duplicated onto the sibling children.
- **FR-009**: The system MUST **roll up** both cost dimensions along the work hierarchy (ticket →
  feature → initiative → project), reportable at every level.
- **FR-010**: Development spend MUST bind only to a ticket whose binding key is valid and DoR-passed;
  spend that cannot be bound MUST be recorded as **unattributed**, never discarded silently.
- **FR-011**: Capturing cost MUST be **best-effort** — a telemetry failure MUST never block or disrupt a
  pipeline run, a validation, an approved work-item write, or a developer's coding session.

## Success Criteria

- **SC-001**: A leader can view total AI cost for any Feature / Initiative / Project, split by dimension,
  and the value equals the sum of that node's descendants' captured cost.
- **SC-002**: 100% of tickets that pass Definition of Ready carry a binding key that resolves to exactly
  one ticket.
- **SC-003**: Re-processing or re-working a ticket increases its cumulative cost; it never resets.
- **SC-004**: A single multi-ticket run contributes its cost exactly once across the affected tickets.
- **SC-005**: Coding-agent sessions that supply a valid key are attributed correctly; the share of spend
  that is unattributed is measurable (target: a small, visible number, not silent loss).
- **SC-006**: For any work item, a viewer can distinguish "cost to build it" (development) from "cost to
  run it" (runtime).

## Key Entities

- **Binding Key** — a canonical, source-neutral ticket identifier, minted at ticket creation and
  enforced at Definition of Ready; the universal join across systems, branches, runs, and sessions.
- **Cost Record** — one captured cost: binding key, dimension (runtime | development), model, tokens,
  cost, timestamp, originating source/session id. Immutable; totals are derived by summing records.
- **Work Hierarchy Node** — a ticket with parent links (ticket → feature → initiative → project); the
  axis along which cost rolls up.

## Assumptions

- **Multi-system intake** (tickets originate in ServiceNow and are realized as Azure DevOps work items),
  so the binding key is a **source-neutral minted token written to every system's ticket record**
  (decided in clarification) — not a reuse of any single system's native id.
- The pipeline is the **mandatory chokepoint**: every ticket is created and DoR-checked before
  assignment, so a binding key always exists before any developer or run touches the ticket.
- Coding agents can export usage/cost telemetry and can be configured to carry a binding key per
  session. **This is an organizational tooling rollout, not something the app can force.**
- No user authentication exists; the binding and identity are **self-asserted by trusted, secret-gated
  callers** and validated against the ticket store — good for attribution, not identity-proof.

## Out of Scope

- **ServiceNow write-back of the binding key (deferred — implementation finding T037).** The ServiceNow
  integration is intake-only (no set-field client), so the binding key is written to the **ADO work
  item** and the local resolution map only. Resolution works without the SNow write; stamping the key
  back onto the SNow ticket is a follow-up if cross-system human lookup from ServiceNow is needed.
- Authenticated per-person identity (a separate effort: wire an identity provider and capture the
  authenticated principal). Per-person attribution remains a secondary dimension, not this feature's
  axis.
- Enforcing that every developer's coding agent is configured to emit + bind (org rollout).
- Real-time budgets, alerts, or spend caps (future).

## Dependencies

- Builds on spec-016 (runtime AI usage capture) and the telemetry write-back (PR #42), the
  triggering-user attribution (PR #44), and the Definition-of-Ready phase pipeline.
