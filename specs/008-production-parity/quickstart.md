# Quickstart Validation Guide: Production Platform Parity

Runnable scenarios that prove each capability works end-to-end.
Assumes the app runs on `http://localhost:5000` via `dotnet run --project src/DBAIAzure.Web`.

---

## Prerequisites

- Anthropic API key in user secrets or environment (`Anthropic:ApiKey`).
- SQLite database at default path (`Storage:SqlitePath = pipeline.db`) — created on first run.
- For Teams scenarios: valid `Teams:PowerAutomateUrl` or Graph API app registration configured.
- For Azure Monitor scenarios: `AzureMonitor:ConnectionString` set.
- E2E tests: `scripts/run-e2e.ps1` starts the app on port 5099.

---

## Scenario 1 — Run persistence survives restart (SC-1)

**Validates**: FR-18.1, FR-18.2, FR-18.3, US1

**Steps**:
1. Open the Workflow Builder, load a workflow with a `Human Approval` node configured with a prompt.
2. Click **Run** — the run starts and reaches the approval gate (status: `Paused`).
3. Without submitting approval, stop the application (`Ctrl+C`).
4. Restart the application (`dotnet run`).
5. Navigate to `/review-queue`.

**Expected**: The paused run appears in the Review Queue with the original workflow name, node label,
and approval question intact. Status is `Paused`.

**Integration test**: `WorkflowRunRepositoryTests.PausedRunSurvivesRoundTrip` — creates a `Paused`
run, disposes the DbContext, reloads it, and asserts the record is retrievable with correct fields.

---

## Scenario 2 — HITL loop closes via Teams (SC-2, SC-3)

**Validates**: FR-19.1–FR-19.4, US2, US3

**Steps**:
1. Configure a Teams connector and at least one approver UPN in connector settings.
2. Run a workflow with a Human Approval node.
3. Confirm a Teams Adaptive Card arrives in the configured approver's Teams chat within 30 seconds.
4. Click **Approve** in the Teams card.
5. Observe the workflow status transition from `Paused` to `Completed` in the builder UI.

**Expected**: Teams message arrives within 30s. Clicking Approve in Teams resumes the workflow.
The run detail page shows a `RunResumed` event in the timeline.

**Integration test**: `TeamsApprovalNotifierTests.NotifyAndWebhookRoundTrip` — mocks the Graph
API call; tests that `TeamsWebhookController` validates JWT and calls `SubmitApproval`.

---

## Scenario 3 — Review Queue operator flow (SC-3)

**Validates**: FR-20.1–FR-20.4, US3

**Steps**:
1. Create two workflows with Human Approval nodes. Run both; both pause.
2. Navigate to `/review-queue`.
3. Confirm both paused runs appear with node label, question, wait time, and notified party.
4. Select the first run and click **Approve**.
5. Confirm the item leaves the pending section and appears in the Resolved section with the outcome.
6. Confirm the second run remains in the pending section without a page refresh.

**Expected**: Real-time SignalR push updates the queue. Resolved items move to the Resolved section
with timestamp and resolver identity.

**E2E test**: `ReviewQueueTests.OperatorApprovalFlow` (Playwright).

---

## Scenario 4 — Execution history and LLM tracing (SC-4, SC-5)

**Validates**: FR-21.1–FR-21.5, US4

**Steps**:
1. Run a workflow with at least one AI (agentic) node through to completion.
2. Navigate to `/runs`.
3. Find the completed run and click through to `/runs/{id}`.

**Expected**: Timeline shows every step with name, type, start/end time, and outcome.
The AI step row shows model name and input/output token counts.

**Integration test**: `WorkflowObserverTests.LlmCallEventPopulatesTokenCounts` — fires a mocked
`IFunctionInvocationFilter`, asserts the resulting `WorkflowExecutionEvent` has non-null
`LlmModelName`, `LlmInputTokens`, `LlmOutputTokens`.

---

## Scenario 5 — Connector config and health check (SC-6, SC-7)

**Validates**: FR-22.1–FR-22.4, US5

**Steps**:
1. Navigate to `/settings/connectors`.
2. Add an Azure DevOps connector with a valid PAT (stored via Key Vault reference in production;
   user secrets in dev).
3. Click **Check health** — confirm the connector shows "Healthy" within 10 seconds.
4. Introduce an invalid PAT; click **Check health** again — confirm "Unhealthy".
5. Open the Workflow Builder with a `Notify` or `Data` node; confirm the connector dropdown lists
   the named instance.
6. Attempt to run a workflow with the unhealthy connector — confirm DoR blocks the run.

**Expected**: Credentials never appear in the application database or logs. Health check reflects
real connector state within 10 seconds.

---

## Scenario 6 — Whole-workflow generation from chat (SC-8)

**Validates**: FR-23.1–FR-23.4, US6

**Steps**:
1. Open the Workflow Builder chat panel.
2. Type: "When a new ticket arrives, summarise it with AI, get manager approval, then notify the
   customer by email."
3. Click **Generate**.

**Expected**: Within 30 seconds, a 4-node connected workflow (Trigger → AI → HumanApproval →
Notify) appears on the canvas, fully wired. No extra nodes. Each node is labelled in the user's
language. The workflow is compatible with realization (spec 007) without additional manual wiring.

**Unit test**: `WorkflowGenerationTests.GeneratesExpectedNodeTypes` — mocked
`IStructuredCompletionService` returns a `WorkflowGenerationResult`; asserts correct node types
and edges are rendered onto the canvas model.

---

## Scenario 7 — Definition of Ready blocks a run (SC-9)

**Validates**: FR-24.1–FR-24.4, US7

**Steps**:
1. Build a workflow with an unrealized AI node (no realization checkmark).
2. Click **Run**.

**Expected**: Run is blocked. A list of failing DoR checks appears above the Run button, including
"All nodes must be realized" in plain language. The Run button is disabled.

3. Realize all nodes (spec 007 flow), then click **Run** again.

**Expected**: DoR checks pass and the run starts normally.

**Unit tests**: `WorkflowPreRunValidatorTests.UnrealizedNodeBlocksRun`,
`WorkflowPreRunValidatorTests.AllRulesPassWhenWorkflowComplete`.

---

## Scenario 8 — Retention job purges old terminal runs (SC-10)

**Validates**: FR-18.4, US1 acceptance scenario 4

**Integration test only** (no manual UI flow):
`WorkflowRunRepositoryTests.PurgeTerminalRunsLeavesActivePausedIntact` — inserts:
  - 3 `Completed` runs with `CompletedAt` before the TTL cutoff.
  - 1 `Paused` run (any age).
  - 1 `Completed` run within TTL.
  
Calls `PurgeTerminalRunsOlderThanAsync(cutoff)`. Asserts: 3 rows deleted, `Paused` run untouched,
in-TTL `Completed` run untouched.

---

## Escalation path (FR-19.4)

**Manual test** (no automated E2E in V1):
1. Configure a Human Approval node with approver chain `["primary@co.com", "manager@co.com"]`,
   timeout 60 seconds, policy `escalate`.
2. Run the workflow; let 60 seconds elapse without acting on the first Teams card.
3. Confirm a second Teams card is sent to `manager@co.com`.
4. Approve via the second card; confirm the workflow resumes.
