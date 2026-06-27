# Feature Specification: Pipeline Connector Configuration Modal

**Feature Branch**: `feature/pipeline-connector-config`

**Created**: 2026-06-18

**Status**: Draft

**Input**: User description: "I want a config modal added to the Pipeline dashboard. I want to be able to configure All the connectors SNow, ADO, LLM, and Comms (Microsoft Teams). I would like the settings to be persisted to a database so I never have to reset them, but everything should be editable in the event I want to make a change. The configuration modal should include a test to validate that the connection is not just saved but functional as it needs to be used in the pipeline. IE a simple 200 ping returned doesn't mean that the LLM model has been properly configured and is reachable."

## Clarifications

### Session 2026-06-18

- Q: When a pipeline run is triggered and a required connector is not configured or hasn't passed its functional test, what should happen? → A: Block the run and report which connectors are not configured or untested — the operator must address them before starting.
- Q: Should the system maintain a history of connector configuration changes? → A: Record the timestamp of the last settings update per connector — no identity tracking required for this POC.
- Q: When the pre-flight check runs before a pipeline run, should it re-test connectors live or use the stored most-recent test result? → A: Always run a fresh live test at run time, all four connectors in parallel, so the pre-flight reflects current credential validity rather than a potentially stale cached result.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Configure a connector for the first time and verify it works (Priority: P1)

A pipeline operator has just deployed the system and needs to wire up all four external
connectors before the pipeline can run end-to-end. They open the configuration modal on the
Pipeline dashboard, fill in the required fields for each connector, click "Test Connection" on
each one, and confirm a meaningful success result — not just that the network route exists, but
that the credentials are valid and the target resource actually responds as expected. Only then
do they save.

**Why this priority**: No connector settings means no pipeline runs. This is the mandatory
first step for any new deployment and the scenario most operators will encounter first.

**Independent Test**: With no settings stored, open the modal for each connector in turn.
Enter valid credentials, click Test, and confirm the test result describes a genuine round-trip
to the external service (not just network reachability). Save. Confirm the saved settings
appear on next open and the pipeline can execute a run that uses all four connectors.

**Acceptance Scenarios**:

1. **Given** no connector settings are stored, **When** the operator opens the configuration
   modal, **Then** all four connectors are shown as "not configured" and their fields are empty.
2. **Given** the operator fills in valid credentials for a connector and clicks "Test
   Connection," **When** the test completes, **Then** the result describes a successful
   functional interaction with the external service (not a network ping), including enough
   detail for the operator to confirm the correct endpoint and account were reached.
3. **Given** the operator clicks "Test Connection" with incorrect credentials, **When** the test
   completes, **Then** the result clearly identifies the cause of the failure (wrong credentials,
   wrong endpoint, model not found, insufficient permissions, etc.) so the operator can
   self-diagnose without consulting logs.
4. **Given** the operator has tested and saved all connectors, **When** the application is
   restarted, **Then** all connector settings are present and the connectors remain operational
   without any re-entry.

---

### User Story 2 — Edit an existing connector setting (Priority: P2)

A pipeline operator needs to rotate an API key that has expired. They open the modal, update
the single credential field for the affected connector, run the test to confirm the new key
works, and save. No other connector settings are disturbed.

**Why this priority**: Credentials rotate regularly. Without editable settings the operator
must redeploy or edit config files — this story makes that a self-service operation.

**Independent Test**: Save a full set of valid connector settings. Then open the modal, update
one credential field on one connector, save, and confirm: (a) only that connector's field
changed, (b) all other connector fields are unchanged, (c) the pipeline uses the updated
credential on the next run.

**Acceptance Scenarios**:

1. **Given** all four connectors are already configured, **When** the operator opens the modal,
   **Then** each connector's non-secret fields are pre-populated with the stored values and
   secret fields show a masked placeholder indicating a value is stored.
2. **Given** the operator updates one field and saves, **When** the modal is closed and reopened,
   **Then** the updated value is reflected and all other fields are unchanged.
3. **Given** an operator enters a new value for a secret field and saves, **When** the modal is
   reopened, **Then** the secret field shows the masked placeholder (not the raw value) and the
   pipeline uses the new credential.

---

### User Story 3 — View configuration status at a glance (Priority: P3)

A pipeline operator wants to quickly see whether all connectors are set up and were last
verified successfully, without needing to open each connector's detail section.

**Why this priority**: Operations and support staff need a fast health check. A red/amber/green
status row is faster than opening and inspecting each connector individually.

**Independent Test**: Configure two connectors fully (with passing tests) and leave two
unconfigured. Open the modal. Confirm the overview distinguishes not-configured, configured-
but-not-yet-tested, and configured-and-last-tested connectors, showing the last test timestamp
and pass/fail result for tested connectors.

**Acceptance Scenarios**:

1. **Given** a mix of configured and unconfigured connectors, **When** the modal opens,
   **Then** each connector displays a status indicator: not configured, configured (untested),
   or tested (with the last test result and timestamp).
2. **Given** a connector's test previously passed, **When** the operator updates any of its
   settings, **Then** the status reverts to "configured (untested)" until the test is run again
   with the new settings.

---

### Edge Cases

- **Connector service temporarily unreachable**: The external service is down during a test →
  the test fails with a clear "service unreachable" message; previously saved settings are
  unchanged and the pipeline can still be started (operator is informed connectivity may fail).
- **Partial save during initial setup**: Operator saves settings before running the test →
  settings are persisted as entered; the connector shows "configured (untested)"; the modal
  surfaces a visual warning that this connector has not been verified.
- **Empty or invalid field values**: Operator clicks Test or Save with a required field empty
  → inline validation identifies the missing field before any network call is made.
- **Secret field unchanged on update**: Operator opens the modal, changes a non-secret field,
  and saves without touching the secret field → the stored secret is preserved unchanged; it is
  not cleared or overwritten with the placeholder text.
- **Concurrent edits**: Two browser sessions update the same connector simultaneously → the
  last save wins; no data corruption occurs.
- **LLM model name mismatch**: The API key is valid but the configured model name does not
  exist → the LLM test fails and reports "model not found" (not a generic credential error).
- **Pre-flight blocks a run**: A pipeline run is triggered while one or more connectors are not
  configured or are in "untested" status → the run is blocked before any pipeline step executes;
  the operator is presented with a diagnostic list identifying each blocking connector and its
  current status (not configured / untested / last test failed), so they know exactly what to
  fix before retrying.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Pipeline dashboard MUST include a persistent, discoverable entry point (e.g.,
  a settings button) that opens the connector configuration modal.
- **FR-002**: The configuration modal MUST present a dedicated section for each of the four
  connector types: ServiceNow, Azure DevOps Boards, LLM (language model provider), and
  Microsoft Teams.
- **FR-003**: Each connector section MUST expose all fields required to establish and authorize
  a connection to that service. Required fields vary by connector:
  - **ServiceNow**: instance URL, username, password or API token.
  - **Azure DevOps**: organization URL, project name, personal access token.
  - **LLM**: provider endpoint or base URL, API key, model name/identifier.
  - **Microsoft Teams**: channel webhook URL.
- **FR-004**: Fields that hold secret values (passwords, API keys, tokens, webhook URLs
  containing embedded secrets) MUST be masked after save. The raw value MUST NOT be readable
  through the UI after it has been stored — only the presence of a stored value is visible.
- **FR-005**: All connector settings MUST be persisted to a durable store so they survive
  application restarts and redeployments without re-entry.
- **FR-006**: All connector settings MUST be editable at any time through the modal. Editing
  a non-secret field MUST preserve stored secret values unless the operator explicitly replaces
  them.
- **FR-007**: Each connector section MUST include a "Test Connection" action that exercises the
  connector beyond network reachability — it MUST prove that the stored credentials are valid
  and the target resource is accessible and operational.
- **FR-008**: The ServiceNow functional test MUST authenticate with the stored credentials and
  perform a lightweight authenticated query against the configured instance (e.g., retrieve a
  system record) to confirm the instance, credentials, and permissions are all operational.
- **FR-009**: The Azure DevOps functional test MUST authenticate with the stored token and
  retrieve a known resource from the configured organization and project (e.g., project
  properties) to confirm the organization, project name, and token are correct and have
  sufficient permissions.
- **FR-010**: The LLM functional test MUST submit a minimal inference request to the configured
  model and confirm a valid model response is returned — proving that the endpoint, API key, and
  model name are all correctly configured and the model is reachable and responding.
- **FR-011**: The Microsoft Teams functional test MUST deliver a labeled test message to the
  configured channel endpoint and confirm the channel accepted the delivery — proving the webhook
  URL is valid, reachable, and the channel is active.
- **FR-012**: After a test completes, the modal MUST display the result in plain language:
  success with a brief description of what was confirmed, or failure with a specific, actionable
  reason (wrong credentials, wrong endpoint, model not found, insufficient permissions, etc.).
- **FR-013**: The operator MUST be able to save connector settings without first running the
  test (to support incremental setup or unavailable external services at save time). The modal
  MUST visually distinguish connectors that have been saved but not yet successfully tested.
- **FR-014**: Changes to connector settings MUST be available to the pipeline on the next run
  without requiring an application restart.
- **FR-015**: The modal MUST display a status summary for each connector: not configured,
  configured (untested), or configured with the last test result and its timestamp.
- **FR-016**: Empty or invalid required fields MUST be caught by inline validation before any
  network call or save attempt is made, with clear field-level error messages.
- **FR-017**: When an operator updates any field in a connector section, the connector's test
  status MUST revert to "untested" until a new test is run and passes with the updated values.
- **FR-018**: Before allowing a pipeline run to begin, the system MUST perform a live pre-flight
  connectivity check against all required connectors in parallel. Each check is equivalent to
  the connector's functional test (FR-008 through FR-011) — not a cached result. If any
  connector fails its live pre-flight check, the system MUST block the run and present a
  diagnostic list identifying every failing connector with its failure reason. No pipeline step
  may execute until all required connectors pass their live pre-flight checks.
- **FR-019**: Connector secret values MUST be stored in encrypted form at rest. A secret value
  must never be persisted in plaintext in any storage layer, log, or audit record.
- **FR-020**: The system MUST record the timestamp of the most recent settings update for each
  connector. This "last updated at" value MUST be visible in the configuration modal alongside
  the connector's status, giving operators a lightweight change history without requiring
  identity tracking.

### Key Entities *(include if feature involves data)*

- **Connector Configuration**: The set of fields required to connect to one external service.
  Each connector type has its own field shape. One configuration record exists per connector type
  per pipeline instance. Carries a "last updated at" timestamp updated on every save.
- **Secret Field**: A configuration field whose value must never be returned to the UI after
  save. The system records its presence (set / not set) but not the raw value.
- **Connection Test Result**: The outcome of a functional test against one connector — a
  pass/fail verdict plus a plain-language diagnostic message and the timestamp of the test.
- **Connector Status**: The observable state of a connector, derived from its saved configuration
  and its most recent test result: not configured | configured (untested) | last tested [pass/fail]
  at [timestamp].

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All four connectors can be configured, tested, and saved from a single modal
  without the operator navigating away from the dashboard.
- **SC-002**: Connector settings survive an application restart with zero re-entry required —
  after initial setup, the pipeline runs on first attempt without configuration prompts.
- **SC-003**: Each connector's functional test completes (pass or fail) within 30 seconds under
  normal network conditions.
- **SC-004**: A failing test provides enough information for an operator to identify and correct
  the root cause without consulting application logs — 100% of failure messages name a specific,
  actionable cause.
- **SC-005**: Secret field values are never visible in the UI after save in 100% of cases.
- **SC-006**: Updating one connector's settings leaves all other connectors' settings unchanged
  in 100% of cases.
- **SC-007**: The status summary on the modal reflects the true configured/tested state of each
  connector at all times, with no stale or misleading indicators.
- **SC-008**: A pipeline run that is blocked by the pre-flight connector check presents a
  diagnostic list identifying every failing connector and its failure reason within 30 seconds
  of the run being triggered (all four live checks run in parallel), before any pipeline step
  executes.

## Assumptions

- **Settings are global (shared)**: Connector settings apply to the whole pipeline instance,
  not per-user. Any authorized dashboard user can view and edit them. Role-based access control
  is out of scope for this POC.
- **Microsoft Teams via incoming webhook**: Teams connectivity is an incoming webhook URL. The
  functional test POSTs a labeled test message to the URL and confirms Teams accepted it (200
  response from Teams' own endpoint, not from the pipeline server). If Teams moves to a bot
  or Graph API approach in future, that is a separate feature.
- **LLM is the existing pipeline model**: The LLM connector configures credentials and model
  identifier for the language model already integrated into the pipeline. The functional test
  sends a minimal, low-cost prompt (e.g., "Respond with the single word READY.") and confirms
  a valid model response is returned — not an error, quota rejection, or timeout.
- **Secret handling — write-only after save**: Once a credential is stored, the system does not
  return the raw value to any client. Updating a secret field replaces the stored value; leaving
  the field blank on update preserves the existing stored value.
- **Durable persistence store already available**: The project has an existing persistence
  mechanism. Connector configuration is added as a new entity within it rather than introducing
  a new data store. ("Database" in the user's request refers to durable server-side persistence,
  not a specific database product.)
- **One configuration set per pipeline instance**: This is a single-tenant POC; there are no
  per-tenant or per-user configuration namespaces.
- **Non-blocking save**: The operator can save settings even if the connection test has not been
  run or has failed. The UI signals the untested/failed state visually but does not block saving.

## Dependencies

- The Pipeline dashboard must exist as a host for the configuration entry point.
- Each external service (ServiceNow instance, Azure DevOps organization, LLM provider endpoint,
  Teams channel) must be reachable from the environment where the pipeline runs for functional
  tests to return meaningful results.
- A durable, server-side persistence mechanism must be available to the running application for
  storing connector settings.
