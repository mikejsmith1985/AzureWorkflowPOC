# Feature Specification: Multi Work-Tracker Support via a Work-Tracker Adapter

**Feature Branch**: `018-work-tracker-adapter`

**Created**: 2026-06-29

**Status**: Draft

**Input**: User description: "define what it would take to implement IWorkTrackerAdapter as discussed above for multi work tracking setup from the application."

## Context

Today the pipeline is wired directly to **Azure DevOps**: work-item creation, the telemetry/cost field
write-back + projection, and field provisioning (process detection, work-item-type field attachment) all
assume ADO's data model. The two-dimensional AI-cost core (cost ledger, source-neutral binding key,
dev-usage ingest) was deliberately built tracker-neutral, but it currently has only one place to land
results. This feature introduces a **single work-tracker abstraction** so the application can target a
different tracker (e.g. Jira) — or more than one — without ADO-specific logic leaking into the pipeline,
cost, or binding layers.

## Clarifications

### Session 2026-06-29

- Q: Tracker selection granularity — single active tracker per instance, or concurrent multi-tracker
  routing per project/workflow? → A: Single active tracker per instance now, with tracker resolution
  behind a seam so per-project/per-workflow routing can be added later as an additive change (no core
  rewrite). (FR-005)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Point the application at a different work tracker (Priority: P1)

A platform operator configures the application to use Jira instead of Azure DevOps. The pipeline then
creates work items, stamps the cost binding key, and records AI cost against Jira issues — with **no
change to the pipeline, cost, or binding code**.

**Why this priority**: This is the entire point — portability of the work-tracking backend. Without it,
the cost/telemetry investment is locked to one vendor. It is also the MVP: the abstraction plus the
existing ADO behavior moved behind it plus one alternative tracker proves the design.

**Independent Test**: With ADO selected, the existing telemetry/cost behavior is unchanged (regression
suite green). With Jira selected, a pipeline run creates the issue, stamps the binding key, and sets the
cost fields — observed on the real Jira issue.

**Acceptance Scenarios**:

1. **Given** ADO is the configured tracker, **When** a phase run completes, **Then** behavior is
   identical to today (work item created, binding key + cost fields written, rollup via ADO Analytics).
2. **Given** Jira is the configured tracker, **When** the same phase run completes, **Then** a Jira issue
   is created with the binding key and cost fields set — using the same pipeline/cost code path.
3. **Given** a tracker is selected, **When** the core pipeline code is inspected, **Then** it contains no
   tracker-specific types or branching — only the abstraction.

---

### User Story 2 - Tracker-neutral field provisioning (Priority: P2)

An operator runs a one-step provisioning that ensures the telemetry + cost fields exist and are usable on
the relevant item types for whichever tracker is configured — generalizing today's ADO field preflight.

**Why this priority**: Fields that aren't usable on the tracker's item types make the whole cost feature
silently fail (exactly the class of bug found in the ADO preflight). Provisioning must work per tracker
on its own customization model.

**Independent Test**: On each tracker, run provisioning twice; the first run makes the fields usable on
the relevant item types, the second is a no-op. Verified by querying the tracker.

**Acceptance Scenarios**:

1. **Given** ADO, **When** provisioning runs, **Then** the fields are created and attached to the relevant
   work-item types (Epic/Story/Task/Bug), idempotently.
2. **Given** Jira, **When** provisioning runs, **Then** the fields are created and associated with the
   relevant issue types via the tracker's own customization model, idempotently.
3. **Given** the configured account lacks permission to customize, **When** provisioning runs, **Then** it
   reports an actionable error and the core run still proceeds (best-effort).

---

### User Story 3 - Cost binding & projection work identically across trackers (Priority: P3)

A binding key resolves to the correct ticket and cost projects onto the tracker's fields, the same way
regardless of tracker — including the secret-gated dev-usage ingest.

**Why this priority**: The binding key + ledger are already tracker-neutral; this story guarantees the
*resolution* and *projection* edges also are, so cost lands correctly on any tracker.

**Independent Test**: Post a dev-usage payload with a binding key on each tracker; the cumulative cost
fields on the resolved ticket reflect the ledger total; an unknown key is recorded unattributed.

**Acceptance Scenarios**:

1. **Given** a minted binding key on tracker X, **When** a run or dev-usage event occurs, **Then** the
   cumulative cost fields on the resolved ticket reflect the ledger — on every tracker.
2. **Given** an unresolvable key, **When** dev-usage arrives, **Then** it is recorded unattributed
   regardless of tracker.

---

### User Story 4 - Cost rollup per tracker, gaps surfaced not hidden (Priority: P4)

A leader sees AI cost rolled up the work hierarchy using each tracker's native rollup. Where a tracker
lacks native hierarchical aggregation, the limitation is surfaced to the operator rather than silently
producing incomplete numbers.

**Why this priority**: Rollup is where trackers differ most (ADO Analytics sums natively; some trackers
need add-ons). Honesty about the gap matters more than forcing a custom engine.

**Independent Test**: With per-item cost fields populated, the configured tracker's native rollup shows
hierarchy totals; for a tracker without native rollup, the operator sees a clear "rollup requires <native
tool>" notice.

**Acceptance Scenarios**:

1. **Given** ADO, **When** a leader opens the rollup view, **Then** runtime + dev cost sum up the tree via
   ADO Analytics (unchanged from today).
2. **Given** a tracker without native hierarchical aggregation, **When** rollup is requested, **Then** the
   per-item fields are still correct and the operator is told which native tool provides the rollup.

### Edge Cases

- What happens when the configured tracker is unsupported or its credentials are missing/invalid?
  → Provisioning and write-back fail with an actionable message; the core pipeline run is not blocked.
- How does the system handle a tracker whose identifiers are strings (e.g. `PROJ-123`) vs numeric?
  → Work items are referenced through an opaque identifier that accommodates both.
- What happens to existing cost-ledger and binding data if the operator switches trackers?
  → The ledger and binding keys are tracker-neutral and are preserved; only the projection target changes.
- How are logical fields handled when a tracker names fields differently (friendly reference vs numeric id)?
  → Each adapter resolves tracker-neutral logical field names to its native references.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST expose a single work-tracker abstraction that the pipeline, cost, and binding
  layers depend on, instead of any tracker-specific client.
- **FR-002**: The abstraction MUST support, at minimum: create a work item (with item type and parent
  link), set fields on a work item, resolve a binding key to a work item, write the binding key onto the
  ticket, and provision the required custom fields.
- **FR-003**: Work items MUST be referenced through an opaque identifier that accommodates both numeric
  (Azure DevOps) and string-key (Jira) identities without the core layers knowing which.
- **FR-004**: Telemetry and cost fields MUST be addressed by **tracker-neutral logical names**; each
  adapter resolves them to its native field references (e.g. `Custom.AIRuntimeCostUSD` vs `customfield_*`).
- **FR-005**: The active work tracker MUST be selectable through configuration without changing or
  redeploying the core application. A **single tracker is active per application instance**; tracker
  resolution MUST go through a seam that allows future per-project / per-workflow routing to be added
  later as an additive change, without modifying the core layers.
- **FR-006**: With Azure DevOps selected, all current telemetry/cost behavior (creation, write-back,
  projection, binding, provisioning, ADO Analytics rollup) MUST be preserved with no regression.
- **FR-007**: At least one additional tracker (Jira) MUST be implementable behind the abstraction covering
  create, set-fields, resolve-by-binding, write-binding, and provision.
- **FR-008**: Field provisioning MUST be idempotent and MUST adapt to each tracker's customization model
  (ADO process / work-item-type field attachment vs Jira field contexts / screens), reporting actionable
  errors on failure.
- **FR-009**: Cost/telemetry write-back and projection MUST operate entirely through the abstraction, with
  no tracker-specific branching in the pipeline, cost, or binding layers.
- **FR-010**: Cost rollup MUST be delivered via each tracker's native mechanism; where a tracker lacks
  native hierarchical aggregation, the system MUST surface that limitation to the operator rather than
  silently omitting data.
- **FR-011**: Selecting or switching the configured tracker MUST NOT corrupt or lose existing cost-ledger
  or binding data (those remain tracker-neutral; only the projection/creation target changes).
- **FR-012**: Misconfiguration (unsupported tracker, missing credentials, insufficient permission) MUST
  fail with an actionable message and MUST NOT block the core pipeline run (best-effort, consistent with
  the existing telemetry behavior).

### Key Entities *(include if feature involves data)*

- **Work-Tracker Adapter**: The abstraction the core depends on — create / set-fields / resolve-by-binding
  / write-binding / provision. One implementation per supported tracker.
- **Work Item Reference**: An opaque identifier for a tracker item, accommodating numeric and string keys.
- **Logical Field Map**: A tracker-neutral logical field name (e.g. `AIRuntimeCostUSD`) resolved by each
  adapter to its native field reference.
- **Tracker Configuration**: Which tracker is active (and its connection settings/credentials), sourced
  from the existing connector-config + secret model.
- **Cost Ledger / Binding Key** *(existing, unchanged)*: Tracker-neutral; reused as-is — this feature does
  not modify them.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With Azure DevOps selected, the existing telemetry + cost behavior is unchanged — the full
  regression suite passes with no modification to test expectations beyond the abstraction seam.
- **SC-002**: Switching the configured tracker requires **zero** changes to pipeline, cost, or binding
  code (only configuration + the selected adapter differ).
- **SC-003**: On the second tracker, a pipeline run creates the work item, stamps the binding key, and
  sets the cost fields — observed end-to-end on the real tracker.
- **SC-004**: Field provisioning is idempotent on both trackers — a second run makes no changes.
- **SC-005**: Per-item cost fields are correct on both trackers; any tracker lacking native rollup shows
  the operator a clear notice naming the native tool required.
- **SC-006**: Adding a hypothetical third tracker requires implementing only the adapter contract — the
  change surface is confined to a new adapter plus its configuration, with no edits to the core layers.

## Assumptions

- The cost ledger, binding-key minting, DoR enforcement, and dev-usage ingest are already tracker-neutral
  and are reused unchanged; this feature only abstracts the *creation*, *field-set/projection*,
  *binding-resolution*, and *provisioning* edges.
- Azure DevOps is refactored behind the abstraction as the reference implementation; Jira is the second
  implementation that proves portability.
- Rollup uses each tracker's native tooling (ADO Analytics; for Jira, Advanced Roadmaps or a marketplace
  aggregation) — no custom cross-tracker rollup engine is built (Framework-First).
- Tracker credentials and selection use the existing connector-configuration and secret-injection model.
- ServiceNow remains an *intake* source, not a work tracker, unless explicitly added later.
- "Multi work tracking" refers to the application supporting different trackers across deployments/projects
  via one abstraction — not real-time bidirectional sync between two trackers.

## Out of Scope

- A custom cross-tracker rollup/aggregation engine (rollup stays native per tracker).
- Migrating or copying existing work items between trackers.
- Real-time bidirectional synchronization of the same work across two trackers simultaneously.
- A graphical field-mapping UI (configuration-driven logical→native field mapping is sufficient for v1).
- Adding trackers beyond Azure DevOps and Jira in this feature (the third+ is proven by the contract, not built).
