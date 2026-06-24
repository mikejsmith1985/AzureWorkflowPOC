# Feature Specification: ADO Telemetry Field Bootstrap

**Feature Branch**: `feature/sk-process-intake-pipeline`

**Created**: 2026-06-23

**Status**: Draft

**Input**: User description: "Implement the ADO Telemetry Field Bootstrap preflight step. Before any ADO work items are created the pipeline must detect whether the target Azure DevOps organization supports custom field creation, create the required telemetry fields if it can (Bootstrap Mode), or fall back to mapping available native fields if it cannot (Adaptive Mode). The ADO organization, project, and credentials must all be configurable from the console UI."

---

## Clarifications

### Session 2026-06-23

- Q: Where should the bootstrap/adaptive manifest be persisted? → A: File on disk in the feature's spec directory (e.g. `specs/NNN-feature/.ado-bootstrap-manifest.json`).
- Q: When should the preflight step run? → A: Automatically on pipeline/application startup AND via a manual "Test Connection" button in the global Settings panel.
- Q: Where in the console UI should the ADO connection settings live? → A: A global "Settings" panel shared across all workflows, with ADO connection as one named section within it.
- Q: Should the preflight support non-Agile inherited processes? → A: Agile and Scrum inherited processes both supported; any other process type fails preflight with a clear error.
- Q: What retry policy applies when ADO rate-limits or transiently fails a field creation call? → A: Retry each failed field up to 3 times with exponential backoff before recording it as permanently failed.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — First-time field bootstrap with admin rights (Priority: P1)

A developer sets up the pipeline against a new Azure DevOps project for the first time. They
supply the organization URL and project name through the global Settings panel and trigger
the preflight. They want to know that all the custom telemetry fields (AI Session ID, AI Model
Used, AI Input Tokens, etc.) have been created in ADO and are ready to receive data — without
having to create the fields by hand through the ADO admin portal.

**Why this priority**: This is the enabling step for all telemetry tracking. Without it no custom
fields exist and the rest of the pipeline has nowhere to write AI cost and activity data.

**Independent Test**: With a freshly provisioned ADO project (no custom fields), configure the
org URL and project in the console, trigger the preflight, then inspect the ADO project — all
12 User Story fields and 2 Task fields must be present and attached to their respective work
item types.

**Acceptance Scenarios**:

1. **Given** valid ADO configuration and a project with no existing telemetry fields, **When**
   the preflight step runs, **Then** all required custom fields are created at the organization
   level and attached to the correct work item types (User Story and Task).
2. **Given** valid ADO configuration and a project where some telemetry fields already exist,
   **When** the preflight step runs again (re-run / pipeline restart), **Then** existing fields
   are left untouched, only missing fields are created, and no error is raised.
3. **Given** a completed bootstrap, **When** the preflight finishes, **Then** a manifest is
   written to the pipeline state recording which fields were created, which already existed, and
   which (if any) failed — and the telemetry mapping is set to "preferred" (custom fields).

---

### User Story 2 — Graceful fallback without admin rights (Priority: P2)

The developer is working against a client-managed ADO organization where they do not have
permission to create custom fields. When the pipeline starts they still want telemetry to be
captured — even if it means using native ADO fields and tags rather than dedicated custom fields.

**Why this priority**: The pipeline must work in locked-down environments without requiring
elevated ADO permissions. Telemetry capture should degrade gracefully, not silently fail.

**Independent Test**: Point the pipeline at an ADO organization where the configured credentials
have no process-level write permission. Trigger the preflight. Confirm that no fields are
created, a runtime mapping document is produced that maps each telemetry value to the closest
available native field (or marks it as log-only), and the pipeline continues without error.

**Acceptance Scenarios**:

1. **Given** ADO credentials without field-creation rights, **When** the preflight step runs,
   **Then** no custom fields are created and no permissions error halts the pipeline.
2. **Given** the Adaptive Mode mapping is produced, **When** a telemetry value has a known
   native fallback field, **Then** it is mapped to that field (e.g. string values map to Tags,
   input token count maps to Story Points).
3. **Given** the Adaptive Mode mapping is produced, **When** a telemetry value has no suitable
   native fallback, **Then** it is recorded as "log only" — captured in the pipeline run log but
   not written to ADO — and the pipeline continues without failing.
4. **Given** a completed Adaptive Mode run, **When** the preflight finishes, **Then** a manifest
   is written to the pipeline state recording the resolved mapping, any unmatched fields, and
   all log-only fields.

---

### User Story 3 — Configure ADO connection from the console UI (Priority: P1)

The developer does not want to edit configuration files by hand. They want to open the
application's console settings, type in the ADO organization URL and project name, and be
done. The preflight should use whatever is configured there, with no hard-coded values
anywhere.

**Why this priority**: The user explicitly requires all ADO target settings to be changeable
through the console UI without touching files or redeploying the app.

**Independent Test**: Change the ADO organization URL in the console settings to point at a
different organization, restart the pipeline, and confirm the preflight targets the newly
configured organization — not the previous one.

**Acceptance Scenarios**:

1. **Given** an ADO organization URL and project name entered through the console settings
   panel, **When** the preflight runs, **Then** it targets exactly those settings — no other
   source (hard-coded value, environment variable leak) overrides them.
2. **Given** incomplete or missing ADO configuration (org URL not set), **When** the preflight
   is triggered, **Then** the pipeline halts with a clear error message identifying the missing
   setting before attempting any ADO calls.
3. **Given** an ADO organization that is not reachable (network error / invalid URL), **When**
   the preflight runs, **Then** it fails with a clear diagnostic message and halts the pipeline
   before any work item creation begins.
4. **Given** the developer clicks "Test Connection" in the settings panel, **When** the on-demand
   preflight completes, **Then** the settings panel displays the mode selected (Bootstrap or
   Adaptive), a summary of the manifest outcome, and any errors — without starting a pipeline run.

---

### User Story 4 — Telemetry field config is overridable per workflow (Priority: P3)

A power user wants to customize which telemetry fields are active for a given workflow or
client environment — for example, disabling cost fields for a client that has billing
sensitivities, or adding extra fields for a specialized engagement.

**Why this priority**: Configurability prevents the need for code changes when the default
field set does not match a specific deployment's needs.

**Independent Test**: Supply a modified field configuration that omits the cost fields. Run the
preflight. Confirm that only the configured fields are created/mapped and the cost fields are
absent from both ADO and the manifest.

**Acceptance Scenarios**:

1. **Given** a custom field configuration supplied at the workflow level, **When** the preflight
   runs, **Then** it creates and maps only the fields listed in that configuration, not the
   default set.
2. **Given** no custom configuration is supplied, **When** the preflight runs, **Then** it uses
   the built-in default field set (12 User Story fields + 2 Task fields) as defined in the
   canonical configuration.

---

### Edge Cases

- **Unsupported ADO process type**: The target project uses CMMI, hosted XML, or any process
  other than Agile or Scrum → halt preflight with a clear error naming the detected process
  type; do not attempt field creation or mapping.
- **ADO org unreachable**: Network failure or invalid URL during preflight → halt with a clear
  error, do not attempt any work item creation downstream.
- **Partial bootstrap failure**: Some fields are created but others fail after 3 retry attempts
  (rate limit, transient error) → log failed fields, continue with the fields that succeeded,
  write a partial manifest, do not halt the pipeline.
- **Picklist creation fails**: The Speckit Phase picklist field cannot be created as a picklist
  type → fall back to a plain string field, log the downgrade, continue.
- **No fallback field available in Adaptive Mode**: A telemetry value cannot be mapped to any
  native field → mark as log-only, continue, include in manifest.
- **Re-run on already-bootstrapped environment**: All fields already exist → skip all creation
  calls, write a manifest confirming all fields are present, no error.
- **Config change between runs**: Org URL or project name changed in console settings → the
  next preflight run targets the new settings; a mismatch with a prior manifest is logged but
  does not block the run.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The pipeline MUST execute the preflight step automatically on startup, before any
  work item is created, to determine the correct field-mapping mode for the run.
- **FR-001b**: The global Settings panel MUST expose a "Test Connection" button that triggers
  the preflight on demand, allowing the developer to validate ADO configuration without
  starting a full pipeline run. The result (mode selected, manifest summary) must be displayed
  in the settings panel after the on-demand run completes.
- **FR-002**: The preflight step MUST be idempotent — running it any number of times against
  the same ADO environment produces the same outcome with no duplicate fields, no errors on
  fields that already exist, and the same manifest structure each time.
- **FR-003**: The system MUST read the ADO organization URL and project name exclusively from
  the ADO connection section of the global Settings panel; no hard-coded or default-override
  values are permitted.
- **FR-004**: If the ADO organization URL or project name is absent from the configuration,
  the system MUST halt the preflight with a clear error message before making any ADO API call.
- **FR-005**: The preflight step MUST detect the inherited process type of the target ADO project
  (Agile or Scrum). If the process is neither Agile nor Scrum, the preflight MUST fail with a
  clear error message naming the detected process type and halt before any field operations.
- **FR-005b**: The preflight step MUST detect whether the configured credentials have permission
  to create custom fields in the target ADO organization (Bootstrap Mode) or not (Adaptive Mode).
- **FR-006**: In Bootstrap Mode, the system MUST create each required telemetry field at the
  organization level if it does not already exist, then attach it to the correct work item type
  within the inherited process.
- **FR-007**: Before creating any field, the system MUST check whether it already exists; if it
  does, the system MUST skip creation and proceed to the attachment step without error.
- **FR-008**: The Speckit Phase field MUST be created as a picklist (string) type with the
  values `Spec`, `Plan`, `Tasks`, `Analyze`, and `Implement`. If picklist creation fails, the
  system MUST fall back to a plain string field and log the downgrade.
- **FR-009**: On completion of Bootstrap Mode, the system MUST write a manifest file to the
  feature's spec directory on disk (e.g. `.ado-bootstrap-manifest.json` within
  `specs/NNN-feature/`) recording: the mode (`bootstrap`), which fields were created, which
  already existed, which failed, and that the mapping strategy is `preferred` (custom fields).
- **FR-010**: In Adaptive Mode, the system MUST pull the available fields for each target work
  item type and build a runtime mapping for every desired telemetry field using this priority
  order: (1) exact custom reference name match, (2) known native field match, (3) Tags
  key-value fallback, (4) log-only.
- **FR-011**: When multiple telemetry values are written to the Tags field in Adaptive Mode,
  the system MUST encode them as pipe-separated key-value pairs appended to any existing tags
  (e.g. `ai-session:abc123 | ai-model:claude-sonnet-4-6 | ai-phase:Spec`).
- **FR-012**: On completion of Adaptive Mode, the system MUST write a manifest file to the
  feature's spec directory on disk (same location as FR-009) recording: the mode (`adaptive`),
  the full field mapping, unmatched fields, and log-only fields.
- **FR-013**: If the ADO organization is not reachable during preflight, the system MUST halt
  with a diagnostic error and prevent any downstream work item creation for that session.
- **FR-014**: Before recording a field creation as failed, the system MUST retry that field up
  to 3 times using exponential backoff. Only after all 3 retries are exhausted may the field
  be written to the manifest as permanently failed.
- **FR-014b**: If a partial bootstrap failure occurs after retries (some fields created, others
  permanently failed), the system MUST log the failed fields, write a partial manifest, and
  continue the pipeline using the fields that did succeed — without halting the entire run.
- **FR-015**: The telemetry field configuration (which fields to create and their fallback
  mappings) MUST be externalized so that a different field set can be supplied at the workflow
  or deployment level without modifying the application code.
- **FR-016**: The system MUST apply the following default field set when no override is
  provided: 12 telemetry fields on the story-level work item type (User Story for Agile,
  Product Backlog Item for Scrum) — AI Session ID, AI Model Used, AI Input Tokens,
  AI Output Tokens, AI Cache Tokens, AI Estimated Cost USD, AI Session Duration Sec, AI Tool
  Calls, AI Tool Accept Rate Pct, AI API Errors, AI Cache Hit Rate Pct, Speckit Phase — and
  2 fields on Task (AI Session ID, AI Model Used) for both process types.
- **FR-017**: All credentials used to authenticate with ADO MUST be resolved at runtime from
  the application's secure configuration; no credential value may be hard-coded or appear in
  any log output.

---

### Key Entities *(include if feature involves data)*

- **ADO Connection Settings**: The org URL and project name configured by the developer through
  the ADO connection section of the global Settings panel. These are the sole source of truth
  for which ADO environment the pipeline targets.
- **Telemetry Field Definition**: A single field the pipeline wishes to write — has a canonical
  name, a custom ADO reference name, a data type, optional picklist values, and a declared
  fallback field.
- **Field Configuration**: The full set of Telemetry Field Definitions for each work item type,
  along with fallback strategy rules. May be the built-in default or a workflow-level override.
- **Bootstrap Manifest**: A JSON file written to the feature's spec directory on disk after a
  Bootstrap Mode run, listing which fields were created, which existed, and which failed.
- **Adaptive Manifest**: A JSON file written to the feature's spec directory on disk after an
  Adaptive Mode run, listing the resolved mapping from each desired field to its actual ADO
  field (or log-only).
- **Runtime Field Mapping**: The live mapping object (loaded from the on-disk manifest) that
  the rest of the pipeline consults when writing telemetry values to ADO work items.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The preflight step completes in under 30 seconds under normal network conditions,
  whether it runs in Bootstrap or Adaptive Mode.
- **SC-002**: A bootstrapped environment is reachable for field writes 100% of the time when
  the preflight completes without reported failures — zero "field not found" errors occur
  during subsequent work item creation.
- **SC-003**: The preflight step is idempotent: running it 10 times consecutively against the
  same environment produces zero field duplication and zero errors in at least 10 out of 10
  runs.
- **SC-004**: When the configured credentials lack field-creation permission, 100% of telemetry
  values are either mapped to a native ADO field or explicitly recorded as log-only — none are
  silently dropped.
- **SC-005**: A developer with no ADO admin knowledge can configure the ADO connection and
  trigger a successful preflight in under 5 minutes using only the global Settings panel.
- **SC-006**: The pipeline never writes to ADO using fields that were not confirmed by the
  preflight manifest — the manifest is the contract that the rest of the pipeline honors.
- **SC-007**: Changing the ADO org URL in the console settings and restarting the pipeline
  results in the preflight targeting the new organization in 100% of cases with no residual
  references to the old target.

---

## Assumptions

- **One target per session**: Each pipeline session targets exactly one ADO organization and
  one project, configured through the console UI. Multi-org support is out of scope.
- **Supported process types**: The target ADO project uses an inherited Agile or Scrum process.
  In Agile the telemetry fields are attached to the "User Story" work item type; in Scrum they
  are attached to "Product Backlog Item" instead. The preflight detects which process is active
  and attaches fields to the correct type automatically. Any other process type (CMMI, hosted
  XML, custom) causes the preflight to fail with a clear error.
- **PAT authentication**: The pipeline authenticates to ADO using a Personal Access Token
  (PAT) supplied via the Forge Vault. The console UI exposes the org URL and project name;
  it does not expose the PAT value directly.
- **Process list as permission probe**: A 200 response from the ADO Processes API is treated as
  evidence of sufficient admin access for Bootstrap Mode. A 403 causes an immediate switch to
  Adaptive Mode without further access checks.
- **Attachment idempotency**: If a field has already been attached to a work item type,
  re-attaching it either succeeds silently or returns a harmless duplicate error that the system
  treats as a no-op.
- **Tags as accumulative**: The Tags field in ADO is treated as a set of tokens; the pipeline
  appends its key-value pairs without replacing existing tags. Duplicate AI-telemetry tag keys
  from a re-run overwrite the prior AI value only — other tags are preserved.
- **No Power BI scope**: Setting up or configuring Power BI reporting on top of the custom
  fields is out of scope for this feature.
- **No OTel capture scope**: The mechanism by which the pipeline captures OpenTelemetry data
  from the Claude Code CLI session (token counts, cost, duration, etc.) is handled separately
  and is not in scope here. This feature only concerns the ADO field setup and mapping.

---

## Dependencies

- **Global Settings panel — ADO connection section**: The section within the application's
  global Settings panel where the developer enters the ADO org URL and project name and
  triggers an on-demand "Test Connection" run. This panel must exist (or be delivered as part
  of this feature) before the preflight can be configured.
- **Forge Vault**: The ADO PAT must be stored in and injected from the Forge Vault per
  Article IX of the project constitution.
- **Azure DevOps REST API v7.1**: The preflight uses the ADO Processes, Work Item Fields, and
  Work Item Type Fields endpoints. A reachable ADO organization at the configured URL is
  required.
- **Feature spec directory write access**: The manifest files are written as JSON files inside
  the feature's `specs/NNN-feature/` directory. The running pipeline must have write access to
  that directory path.
- **Downstream work item creation step** (out of scope here): The step that creates work items
  reads from the manifest produced by this feature; it is a consumer but is not implemented
  here.
