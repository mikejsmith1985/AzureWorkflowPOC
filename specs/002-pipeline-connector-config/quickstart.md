# Quickstart Validation Guide: Pipeline Connector Configuration Modal

**Date**: 2026-06-18 | **Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

This guide walks through the runnable scenarios that prove the feature works end-to-end.
Reference the [data model](data-model.md) for entity shapes and the
[contracts](contracts/) for service interfaces.

---

## Prerequisites

- Application running locally: `dotnet run --project src/DBAIAzure.Web`
- All four external services reachable from the machine:
  - A ServiceNow developer instance (free at developer.servicenow.com)
  - An Azure DevOps organization with a test project
  - An Anthropic API key with quota on a supported model
  - A Microsoft Teams channel with an incoming webhook configured
- Valid credentials for each of the above available (never commit them)

---

## Scenario 1 — Initial Configuration (US1 / FR-001 through FR-007)

**Goal**: Configure all four connectors from scratch and confirm settings persist.

1. Open `http://localhost:5000` (the Pipeline dashboard — `Index.razor`).
2. Confirm a settings gear icon is visible in the dashboard header or toolbar.
3. Click the gear icon. **Expected**: the configuration modal opens showing four sections
   (ServiceNow, Azure DevOps, LLM, Teams), each displaying status "Not configured."
4. In the **ServiceNow** section, enter the instance URL and username (non-secret fields),
   then the password or API token (secret field, masked on entry).
5. Click **Test Connection**. **Expected**: within 30 seconds, a success message describes a
   genuine round-trip (e.g., "Authenticated as [username] — ServiceNow instance responded with
   system properties."). A failure reports a specific cause.
6. Click **Save** for ServiceNow. **Expected**: status changes to "Configured — last tested [timestamp], Pass."
7. Repeat steps 4–6 for Azure DevOps, LLM, and Teams.
8. Close the modal. Reopen it. **Expected**: all four connectors show their saved (non-secret)
   field values and masked placeholders for secret fields.
9. Restart the application (`Ctrl+C`, `dotnet run`). Reopen the modal.
   **Expected**: all four connectors are still configured with the same values — no re-entry
   required (SC-002).

---

## Scenario 2 — Credential Rotation (US2 / FR-006, FR-017)

**Goal**: Update one secret field without disturbing others.

1. With all four connectors configured and tested, open the modal.
2. In the **Azure DevOps** section, clear the PAT field and enter a new (valid) PAT.
   **Expected**: the connector's status immediately shows "Configured (untested)" — the prior
   test result is invalidated (FR-017).
3. Click **Test Connection** for Azure DevOps only. **Expected**: new PAT is verified; status
   updates to "Configured — last tested [new timestamp], Pass."
4. Click **Save** for Azure DevOps.
5. Close and reopen the modal. **Expected**: Azure DevOps shows the new PAT as a masked
   placeholder. ServiceNow, LLM, and Teams are unchanged (SC-006).

---

## Scenario 3 — Failed Functional Test (US1 Scenario 3 / FR-012)

**Goal**: Confirm that a wrong credential produces a specific, actionable error.

1. Open the modal. In the **LLM** section, replace the API key with an obviously invalid value
   (e.g., `bad-key`).
2. Click **Test Connection**. **Expected**: within 30 seconds, the test fails with a message
   that names the specific cause (e.g., "Authentication failed — the API key was rejected by the
   provider (401 Unauthorized). Check the key value and try again.") — not a generic error.
3. Restore the correct API key. Click **Test Connection** again. **Expected**: passes.

---

## Scenario 4 — Pre-Flight Blocks a Run (FR-018, SC-008)

**Goal**: Confirm a pipeline run is blocked when a connector is not configured.

1. Open the modal. In the **Teams** section, delete the webhook URL (or leave it blank without
   saving). Save Teams as unconfigured.
2. Navigate to the pipeline run trigger (e.g., submit a new ticket via the "New Ticket" page
   or POST to `/api/webhook/servicenow`).
3. **Expected**: the run does not start. Within 30 seconds, the dashboard or API response
   surfaces a diagnostic message listing Teams as the blocking connector with reason
   "Connector is not configured — no credentials stored."
4. Reconfigure and test Teams. Retry the run trigger.
   **Expected**: the run starts normally.

---

## Scenario 5 — Pre-Flight Uses Live Credentials (Clarification Q3)

**Goal**: Confirm the pre-flight does not use a cached test result.

1. With all connectors configured and tested (all passing), revoke the Azure DevOps PAT in the
   Azure DevOps portal (simulate credential expiry).
2. Trigger a pipeline run.
3. **Expected**: the run is blocked with a pre-flight failure for Azure DevOps
   (e.g., "Authentication failed — the Personal Access Token was rejected (401).").
   The dashboard still shows the prior cached test result as "Pass" — the pre-flight result
   is the live check, which correctly reflects the revoked PAT.

---

## Scenario 6 — Secret Field Never Returns to UI (FR-004, FR-019, SC-005)

**Goal**: Confirm secrets are masked and never returned in plaintext.

1. Configure and save the LLM connector with a known API key.
2. Open the browser developer tools → Network tab.
3. Reopen the modal. Inspect all network requests made by the Blazor circuit.
   **Expected**: no request or response contains the raw API key value. The UI shows only a
   masked placeholder (e.g., `••••••••`).
4. Inspect the SQLite database file (`pipeline.db`) with a SQLite browser.
   **Expected**: the `EncryptedSecretsJson` column contains an opaque encrypted blob, not
   the raw API key (FR-019).

---

## Scenario 7 — Partial Save (FR-013)

**Goal**: Confirm the operator can save untested settings.

1. Open the modal with all connectors unconfigured.
2. Fill in the ServiceNow fields but do NOT click Test Connection.
3. Click **Save**. **Expected**: settings are persisted; status shows "Configured (untested)"
   with a visible warning indicator. The modal does not block the save.
4. Close and reopen the modal. **Expected**: the ServiceNow fields are pre-populated; status
   still shows "Configured (untested)."

---

## Automated Test Coverage Map

| Scenario | Test Type | Location |
|----------|-----------|----------|
| Repository CRUD + encryption | Unit (in-memory SQLite, mocked IDataProtector) | `SqliteConnectorConfigRepositoryTests.cs` |
| Pre-flight all-pass / any-fail | Unit (mocked connector clients) | `ConnectorHealthCheckerTests.cs` |
| SNow live test round-trip | Integration (real SNow dev instance) | `ConnectorFunctionalTests.cs` |
| ADO live test round-trip | Integration (real ADO org) | `ConnectorFunctionalTests.cs` |
| LLM live inference round-trip | Integration (real Anthropic API) | `ConnectorFunctionalTests.cs` |
| Teams live webhook delivery | Integration (real Teams channel) | `ConnectorFunctionalTests.cs` |
| Blazor modal open/close + field masking | Manual (Scenario 6) | — |
| Orchestrator pre-flight gate | Unit (mocked IConnectorHealthChecker) | `ConnectorHealthCheckerTests.cs` |
