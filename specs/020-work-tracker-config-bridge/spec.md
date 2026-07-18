# Feature Specification: Work-Tracker Config Bridge — Select & Configure Any Tracker (incl. Jira) from the UI

**Feature Branch**: `feature/generic-work-tracker-config-labels`

**Created**: 2026-07-18

**Status**: Draft

**Input**: User description: "make the full bridge work so i can use this with Jira" — the connector-settings
screen is hardcoded to Azure DevOps, while the tracker the pipeline actually calls (the spec-018 adapter
layer) is configured only by environment variables. An operator has no in-app way to choose Jira or enter
Jira credentials, so Jira cannot be used in practice.

## Context

Spec-018 introduced a tracker-neutral **work-tracker adapter** abstraction and shipped two implementations
(Azure DevOps and Jira), but it deliberately stopped short of wiring the pipeline and the operator-facing
configuration onto it. The result is **two disconnected systems**:

1. **The connector-settings UI** — what an operator actually edits. It is hardcoded to a single Azure DevOps
   connector (organization URL, project, personal access token), stored in the connector-config database and
   encrypted-secret store. There is no provider choice and no Jira form.
2. **The adapter layer** — what the running pipeline actually calls. It selects the active tracker and reads
   that tracker's credentials **only from static application configuration / environment variables**, which
   are never surfaced in the UI and, for Jira, are not present in any shipped settings file.

Because nothing an operator edits in the UI feeds the adapter layer, selecting Jira is impossible through the
product. This feature is the **bridge**: it makes the connector-settings UI the single, generic place to
choose the active work tracker and enter its credentials, and makes that selection the source of truth the
running pipeline uses — proving it end-to-end against a real Jira instance.

This is a UI-and-wiring feature. The tracker operations themselves (create/upsert/set-fields/comment/provision)
already exist behind the spec-018 adapters and are reused unchanged (Framework-First).

## Clarifications

### Session 2026-07-18

- Q: How is the generic work tracker represented in the connector model? → A: A single generic
  `WorkTracker` connector type carrying a `provider` discriminator (Azure DevOps / Jira); the
  vendor-specific `AzureDevOps` connector type is retired and its stored rows migrated. (FR-014)
- Q: When does a changed tracker selection or credential take effect? → A: Live — no restart. The active
  adapter and its credentials resolve per run from the stored configuration, matching the existing LLM
  connector hot-reload. (FR-005)
- Q: What happens to an existing Azure DevOps connector on upgrade? → A: Auto-migrate in place on startup —
  the existing ADO row becomes the generic `WorkTracker` connector (provider = Azure DevOps) with its
  encrypted secret preserved and set as the active tracker; zero operator action. (FR-015)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure Jira from the UI and run a ticket end-to-end (Priority: P1)

A platform operator opens the connector settings, chooses **Jira** as the work-tracking system, enters the
Jira site URL, account email, API token, and project key, saves, and runs a ticket. The pipeline creates the
issue in Jira, stamps the cost binding key, sets the cost fields, and appends its comments — all driven by
what the operator entered in the UI, with **no environment-variable or file edits**.

**Why this priority**: This is the whole request. Without it, Jira is unusable through the product regardless
of the adapter being present. It is also the MVP that proves the bridge works.

**Independent Test**: With Jira configured entirely through the UI, run a demo ticket; observe the created
issue, the binding key, and the cost fields on the real Jira instance — with no static Jira configuration set.

**Acceptance Scenarios**:

1. **Given** no Jira configuration exists in any settings file, **When** an operator enters Jira credentials
   in the connector-settings UI and saves, **Then** the running pipeline uses those credentials on the next run.
2. **Given** Jira is the selected work tracker, **When** a phase run completes, **Then** a Jira issue is
   created with the binding key and cost fields set, and the run's comments appended — via the existing adapter.
3. **Given** an operator revisits the settings, **When** the Jira connector is shown, **Then** the non-secret
   fields are displayed and the API token is never shown back in plaintext.

---

### User Story 2 - Choose and switch the active tracker generically (Priority: P2)

An operator sees the work tracker presented as a generic **"Work Tracking System"** with a provider selector
(Azure DevOps / Jira), not as a hardcoded single vendor. They can switch the active provider, and the change
takes effect for subsequent runs without restarting the application.

**Why this priority**: Generic, selectable presentation is what makes the product multi-tracker rather than
ADO-with-extra-code. It is also what the label change the operator originally asked for actually requires — a
provider selector, not just renamed text.

**Independent Test**: With ADO active, switch the selector to Jira and save; the next run targets Jira. Switch
back; the next run targets ADO. No restart, no file edit.

**Acceptance Scenarios**:

1. **Given** the connector-settings screen, **When** it renders, **Then** the tracker is labelled generically
   and the currently active provider is clearly indicated.
2. **Given** a provider is selected, **When** the form renders, **Then** it shows exactly that provider's
   connection fields with provider-appropriate help text (ADO: organization URL / project / PAT; Jira: site
   URL / email / API token / project key).
3. **Given** the operator changes the active provider and saves, **When** the next pipeline run starts, **Then**
   it uses the newly selected provider without an application restart.

---

### User Story 3 - Verify a connection before relying on it (Priority: P3)

Before running real work, an operator clicks **Test Connection** for the selected provider and sees an accurate
pass/fail with an actionable message — for Jira as well as Azure DevOps.

**Why this priority**: "So I can use this with Jira" means the operator needs confidence the credentials are
right before a run depends on them. Today only Azure DevOps has a preflight; Jira has none.

**Independent Test**: Enter valid Jira credentials → Test Connection passes; enter an invalid token or an
unreachable site → it fails with a message that names the likely cause.

**Acceptance Scenarios**:

1. **Given** valid credentials for the selected provider, **When** Test Connection runs, **Then** it reports
   success within a few seconds.
2. **Given** invalid or missing credentials, **When** Test Connection runs, **Then** it reports failure with an
   actionable message and does not create any work item.

---

### User Story 4 - Existing Azure DevOps deployments are unaffected (Priority: P4)

An operator already running Azure DevOps upgrades to this feature and sees no behavior change: ADO remains the
default active tracker, the existing connector configuration keeps working, and no reconfiguration is required.

**Why this priority**: The bridge must not regress the tracker that already works in production.

**Independent Test**: With an existing ADO connector configured, upgrade and run the regression suite; behavior
is identical and no manual reconfiguration is needed.

**Acceptance Scenarios**:

1. **Given** an existing ADO connector, **When** the feature is deployed, **Then** ADO remains the active
   tracker by default and the existing configuration continues to work unchanged.
2. **Given** ADO is active, **When** the regression suite runs, **Then** all current telemetry/cost behavior
   passes with no change to expectations beyond the generic presentation.

### Edge Cases

- **No tracker configured at all** → the application still runs; tracker writes are skipped best-effort and the
  UI shows a clear "no work tracking system configured" state rather than failing a run.
- **Switching provider while runs are in flight** → the switch applies to subsequent runs; in-flight runs
  complete against the tracker they started with.
- **Invalid project key (Jira) or project (ADO)** → surfaced by Test Connection and, if a run proceeds anyway,
  handled best-effort with an actionable log, never a hard crash.
- **Secret rotation** → entering a new token in the UI is used on the next run without a restart; the old value
  is never displayed.
- **Static config and UI config both present** → the UI/stored configuration is authoritative; static settings
  act only as a first-run seed.
- **Upgrade with an existing ADO connector** → it is auto-migrated in place to the generic connector
  (provider = Azure DevOps), secret preserved and set active; re-running migration is a no-op (FR-015).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An operator MUST be able to select which work tracker is active entirely through the
  connector-settings UI, without editing environment variables, settings files, or redeploying.
- **FR-002**: The connector-settings UI MUST present the work tracker **generically** ("Work Tracking System")
  with a provider selector, rather than as a single hardcoded vendor.
- **FR-003**: For each supported provider (Azure DevOps, Jira), the UI MUST show that provider's own connection
  fields and help text — Azure DevOps: organization URL, project, personal access token; Jira: site URL,
  account email, API token, project key.
- **FR-004**: The active-tracker selection and its connection settings entered through the UI MUST be the
  **source of truth** the running pipeline uses; the active adapter and its credentials MUST resolve from the
  stored connector configuration, not only from static application configuration.
- **FR-005**: Changing the active tracker or its credentials through the UI MUST take effect for subsequent
  pipeline runs **without an application restart**. The active adapter and its credentials MUST be resolved
  **per run** from the stored configuration (not baked once at startup), consistent with the existing LLM
  connector hot-reload behavior.
- **FR-006**: Secrets (personal access token, API token) MUST continue to be stored via the existing
  encrypted-secret model, never written in plaintext to configuration, logs, or the database, and never
  displayed back after entry.
- **FR-007**: The UI MUST provide a **Test Connection** action for the selected provider — including Jira — that
  verifies credentials and reachability and reports an actionable pass/fail result before a run depends on it.
- **FR-008**: Exactly **one** tracker is active per application instance (unchanged from spec-018 FR-005); the
  currently active provider MUST be clearly indicated in the UI.
- **FR-009**: With Jira selected and configured through the UI, a pipeline run MUST create the issue, stamp the
  binding key, set the cost/telemetry fields, and append comments on the real Jira instance — via the existing
  adapter, with no change to the pipeline, cost, or binding code.
- **FR-010**: With Azure DevOps selected, all existing telemetry/cost behavior MUST be preserved with no
  regression; existing ADO connector configuration MUST continue to work, and ADO MUST remain the default
  active tracker for current deployments.
- **FR-011**: Field provisioning (telemetry/cost fields) MUST run against whichever tracker the operator has
  selected in the UI, using that tracker's customization model.
- **FR-012**: Misconfiguration (missing/invalid credentials, unreachable tracker, insufficient permission) MUST
  surface an actionable message in the UI and MUST NOT hard-block the application; pipeline tracker writes
  remain best-effort, consistent with existing behavior.
- **FR-013**: First-run guidance and help copy MUST refer to the work tracker generically and reflect the
  selected provider, rather than hardcoding "Azure DevOps".
- **FR-014**: The work tracker MUST be modelled as a **single generic connector type** carrying a `provider`
  discriminator (Azure DevOps / Jira), rather than a separate connector type per vendor; the retired
  vendor-specific `AzureDevOps` connector type MUST NOT remain a distinct operator-facing connector.
- **FR-015**: On upgrade, an existing Azure DevOps connector MUST be **auto-migrated in place** to the generic
  connector (provider = Azure DevOps) with its encrypted secret preserved and set as the active tracker,
  requiring no operator action; migration MUST be idempotent and MUST NOT lose the existing configuration.

### Key Entities *(include if feature involves data)*

- **Work Tracking System Connector**: A **single generic** operator-facing connector type representing the
  active work tracker — carries the chosen provider (a discriminator) plus that provider's connection
  settings and secret. Replaces the retired vendor-specific `AzureDevOps` connector type (FR-014).
- **Provider Selection**: Which tracker implementation is active (Azure DevOps or Jira), stored on the generic
  connector as the `provider` discriminator and resolved by the pipeline **per run** (FR-005).
- **Provider Credential Set**: The provider-specific connection fields (ADO: org URL / project / PAT; Jira:
  site URL / email / API token / project key), non-secret parts stored in the connector config, secrets in the
  encrypted-secret store.
- **Connection Test Result**: The outcome of a Test Connection attempt for the selected provider — pass/fail
  plus an actionable message — surfaced in the UI.
- **Work-Tracker Adapter / Active-Tracker Resolution** *(existing, spec-018)*: Reused unchanged as the
  execution layer; this feature feeds it from the stored connector configuration instead of static config.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can configure Jira and run a ticket end-to-end onto a **real Jira issue using only the
  UI** — no environment-variable, settings-file, or redeploy steps.
- **SC-002**: Switching the active tracker between Azure DevOps and Jira is done entirely in the UI and takes
  effect on the next run **without an application restart**.
- **SC-003**: The Azure DevOps regression suite passes unchanged and existing ADO deployments require **zero**
  manual reconfiguration after upgrade.
- **SC-004**: Test Connection for both providers returns an accurate pass/fail with an actionable message within
  a few seconds.
- **SC-005**: Secrets never appear in the UI after entry, in logs, or in the database in plaintext.
- **SC-006**: No user-facing string presents the work tracker as Azure-DevOps-only; the provider name shown to
  the operator always matches the active selection.

## Assumptions

- The connector-config database and encrypted-secret store become the **runtime source of truth** for tracker
  selection and credentials; the static `WorkTracker:*` / `AzureDevOps` application-config keys become an
  optional first-run **seed/fallback**, not the primary path.
- **Live-apply (no restart)** is the expected behavior, consistent with the existing LLM connector hot-reload
  in the codebase.
- Supported providers in this feature are **Azure DevOps and Jira only**. Monday and any other tracker are
  proven by the spec-018 contract but not built here.
- **Single active tracker per instance** (spec-018 FR-005 unchanged); per-project / per-workflow tracker routing
  remains a future additive change behind the existing resolution seam.
- The spec-018 adapters (Azure DevOps and Jira) are functionally complete for create / upsert / set-fields /
  comment / provision; this feature **wires them to the UI**, it does not re-implement tracker operations.
- Existing Azure DevOps users keep ADO as the active tracker after upgrade via an automatic in-place
  migration to the generic connector (FR-015) — no reconfiguration and no data loss, not a from-scratch re-setup.

## Out of Scope

- Implementing Monday or any third tracker beyond the existing spec-018 contract.
- Concurrent multi-tracker routing or per-project tracker selection (single active tracker per instance stands).
- Migrating or copying existing work items between trackers, or bidirectional sync.
- A graphical logical→native field-mapping UI (field mapping stays configuration-driven, per spec-018).
- Changing the intake sources (e.g. ServiceNow remains an intake source, not a work tracker).
