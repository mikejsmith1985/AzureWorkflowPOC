# Feature Specification: Production Platform Parity — Azure-Stack Completeness

**Feature Branch**: `feature/production-parity`

**Created**: 2026-06-23

**Status**: Draft — ready for `/speckit-plan`

**Input**: Implement all missing features and improvements identified in the DBAI (LangGraph POC) vs AzureWorkflowPOC comparison, constrained to the Azure/Microsoft technology stack.

---

## Clarifications

### Session 2026-06-23

- Q: How should incoming Teams Adaptive Card action requests be authenticated on the webhook receiver endpoint? → A: Validate Microsoft-signed JWT bearer token on every incoming request (standard Teams channel auth) — no shared secrets; verified via Microsoft Bot Framework or lightweight middleware.
- Q: Which status enum should WorkflowRun support? → A: `Running / Paused / Completed / Failed / TimedOut / Cancelled` — fine-grained, distinct terminal states. (Code naming: `Paused` = Suspended, `Completed` = Succeeded — matches existing `WorkflowRunStatus` enum; spec updated throughout.)
- Q: What is the expected peak number of concurrent in-flight workflow runs? → A: Under 50 — departmental scale; direct Graph API delivery is sufficient, no Service Bus or SignalR backplane required.
- Q: How are approvers identified for a human-approval node, and how does escalation work? → A: Ordered static chain within the node — a priority-ordered list of email/UPN addresses configured at design time; on timeout the system automatically notifies the next approver in the chain. No separate escalation node required; no dynamic Graph people-lookup.
- Q: Which Azure DevOps Boards operations must the connector support in this release? → A: Create work item only — minimum viable connector covering the intake-to-ADO write-back scenario; update/query/transition are follow-on.

---

## Overview

The workflow builder canvas and node-realization pipeline are ahead of the Python POC. Everything behind the canvas — persistence, human-in-the-loop close loop, observability, integration connectors, and validation rules — is absent or skeletal. This feature brings the platform to production parity: a workflow that parks for human approval survives a server restart; a human is notified and can reply; every execution is traceable; connectors to real Microsoft/Azure services are configurable; and completed runs are queryable after the fact.

All infrastructure choices are constrained to the Azure/Microsoft stack: Azure SQL (EF Core), Azure Monitor / Application Insights, Microsoft Teams (Graph API), Azure DevOps Boards, and Azure Service Bus where async delivery is needed.

---

## User Scenarios & Testing

### User Story 1 — A paused workflow survives a server restart (Priority: P0)

A triage workflow parks at a Manager Approval node. The server is redeployed overnight. The next morning the operator opens the Review Queue, sees the pending approval with its original context, submits a decision, and the workflow resumes from exactly where it stopped — no data lost, no re-submission needed.

**Why P0**: Without persistence, every HITL interaction is demo-only. All other stories depend on this foundation.

**Acceptance Scenarios**:

1. **Given** a running workflow that reaches a human-approval node, **When** the process suspends, **Then** full process state is written to Azure SQL before the suspension acknowledgement is returned.
2. **Given** persisted process state, **When** the application restarts, **Then** all suspended workflows are automatically rehydrated and remain in "awaiting input" status.
3. **Given** a rehydrated workflow, **When** a resume signal is injected, **Then** the process continues from the exact suspension point with no data loss or duplication.
4. **Given** a completed workflow, **When** the retention job runs after the configured TTL, **Then** the run record is purged and no active-run query returns it.

---

### User Story 2 — A human is notified and can reply to approve or reject (Priority: P0)

The same triage workflow parks at Manager Approval. The configured approver receives a Teams message with the workflow name, the pending question, and two action buttons (Approve / Reject). Clicking Approve in Teams resumes the workflow without requiring the approver to open the web app.

**Why P0**: Parking without notification is invisible. This closes the loop DBAI proved is required for real use.

**Acceptance Scenarios**:

1. **Given** a workflow suspends at a human-approval node, **When** the suspension is persisted, **Then** a Teams message is sent to the configured approver(s) within 30 seconds containing the workflow name, the pending question, and clear decision options.
2. **Given** the approver clicks a decision in Teams, **When** the webhook is received, **Then** the corresponding resume signal is injected and the workflow continues.
3. **Given** no response after a configurable timeout, **When** the timeout elapses, **Then** the workflow auto-escalates or auto-rejects per the node's configured policy, and the approver is notified of the timeout outcome.
4. **Given** the notification channel is misconfigured, **When** suspension occurs, **Then** the workflow still parks safely and a fallback alert appears in the Review Queue UI — the primary flow is never blocked by a notification failure.

---

### User Story 3 — An operator reviews and acts on all paused workflows in one place (Priority: P1)

An operator opens the Review Queue page. They see every suspended workflow instance, its pending question, how long it has been waiting, and who was notified. They select one, read the context, type a reply, and submit. The queue updates in real time to reflect that the item is no longer pending.

**Why P1**: Teams action buttons cover the happy path; the Review Queue is the fallback and the audit surface.

**Acceptance Scenarios**:

1. **Given** one or more suspended workflow instances, **When** the operator opens the Review Queue, **Then** every pending instance is listed with: workflow name, node label, pending question, wait time, and notified party.
2. **Given** the operator selects an item and submits a decision, **Then** the resume signal is injected, the item leaves the queue, and the workflow status updates within 5 seconds.
3. **Given** a workflow times out or auto-resolves, **When** the operator views the queue, **Then** the item is moved to a "resolved" section with the outcome and timestamp — not silently removed.

---

### User Story 4 — Every workflow run is traceable after the fact (Priority: P1)

A workflow completed (or failed) three hours ago. An operator opens the Execution History page, finds the run, and drills into a timeline showing every step, its start/end time, its outcome, and — for AI steps — the LLM model used and approximate token cost. They can identify exactly which step failed and why.

**Why P1**: Without post-run traceability, debugging production failures is blind.

**Acceptance Scenarios**:

1. **Given** a workflow run completes (success or failure), **When** the operator opens Execution History, **Then** the run appears with status, duration, start time, and triggered-by identity.
2. **Given** the operator drills into a run, **Then** a chronological timeline of every step is shown: step name, type, start/end time, outcome (success/failure/skipped), and for AI steps: model name and token count.
3. **Given** a step failed, **When** the operator views it, **Then** the error message and last known state are shown in plain language — not a raw stack trace.
4. **Given** a completed run, **When** the retention TTL elapses, **Then** the run record and its timeline are purged together — no orphaned events remain.

---

### User Story 5 — Connectors to Azure DevOps, Teams, and Email are configurable in the UI (Priority: P1)

An administrator opens the Connector Settings page, adds an Azure DevOps connection (org URL + PAT stored in Azure Key Vault), a Teams webhook, and an Office 365 email account. Each connector shows a health-check status. Workflow nodes referencing a connector type now bind to a real, named connector instance rather than a placeholder.

**Why P1**: Connectors are the prerequisite for realized workflows to actually call external services.

**Acceptance Scenarios**:

1. **Given** the Connector Settings page, **When** an admin provides credentials for Azure DevOps, Teams, or Email, **Then** the credentials are stored in Azure Key Vault (never in the application database) and a reference name is saved.
2. **Given** saved connectors, **When** the admin triggers a health check, **Then** the UI shows pass/fail for each connector within 10 seconds.
3. **Given** a healthy saved connector, **When** a workflow node of the matching type is realized, **Then** the node can bind to that connector instance by name.
4. **Given** a connector whose credentials have expired, **When** the health check runs, **Then** the connector is marked unhealthy and any bound realized nodes show an "out of date" warning.

---

### User Story 6 — A non-technical user generates a full workflow from a sentence (Priority: P2)

A product owner types: "When a ServiceNow ticket arrives, summarise it with AI, ask a manager to approve, and notify the customer by email." The system generates a complete, connected workflow on the canvas — all nodes placed, labelled, and wired — ready for the user to review and then realize.

**Why P2**: Valuable on-ramp; builds on the existing canvas and realization pipeline. Secondary to the P0/P1 foundations.

**Acceptance Scenarios**:

1. **Given** a plain-English workflow description in the chat panel, **When** the user submits it, **Then** a fully connected workflow (nodes + edges) appears on the canvas within 30 seconds.
2. **Given** the generated workflow, **When** the user reviews it, **Then** every node is labelled in their original language and the edges match the described flow — no extra or missing nodes.
3. **Given** the generated workflow, **When** the user clicks "Make it real," **Then** the realization pipeline (spec 007) processes it without requiring any additional setup.

---

### User Story 7 — Definition of Ready rules validate a workflow before it runs (Priority: P2)

Before a workflow is submitted for execution, the system validates it against a configurable Definition of Ready checklist: at least one trigger node, no unrealized nodes, all connectors healthy, all HITL nodes have a configured approver. Failed checks are shown as a blocking list before the Run button activates.

**Why P2**: Prevents trivially broken workflows from entering execution; mirrors the DoR pattern proven in DBAI.

**Acceptance Scenarios**:

1. **Given** a workflow missing a trigger node, **When** the user clicks Run, **Then** the run is blocked and the missing-trigger rule is listed as a failing check in plain language.
2. **Given** a workflow where all DoR rules pass, **When** the user clicks Run, **Then** the run proceeds without any validation prompt.
3. **Given** an administrator changes the DoR rule set in settings, **Then** subsequent run attempts use the updated rules within one page refresh.

---

### Edge Cases

- Process state write fails mid-suspension → the suspension is rolled back; the workflow retries or surfaces an error rather than entering a phantom-suspended state.
- Teams webhook delivery fails → the workflow parks; the Review Queue shows the item; a retry is attempted after a configurable backoff.
- Azure Key Vault is unreachable at connector health-check time → health check returns "unknown" (not "healthy"); no credentials are cached in memory beyond the request.
- Two operators simultaneously submit decisions for the same suspended workflow → first-writer wins; the second receives a "already resolved" notice and the workflow is not corrupted.
- Generated workflow description is ambiguous → the chat assistant asks one clarifying question before generating; it does not silently guess.
- Execution log for a very long workflow (50+ steps) → timeline is paginated; the full run is still queryable.

---

## Functional Requirements

### FR-18 Process Persistence

- **FR-18.1** All process state for suspended and in-flight workflow runs must be persisted to Azure SQL via EF Core before any external action (notification, API call) is taken.
- **FR-18.2** On application startup, all suspended run records must be rehydrated into the SK Process runtime automatically, without operator intervention.
- **FR-18.3** A `WorkflowRun` entity must be maintained (run_id, workflow_id, status, created_at, suspended_at, resumed_at, completed_at) queryable independently of the full state blob. The `status` field must be a closed enum: `Running | Paused | Completed | Failed | TimedOut | Cancelled`.
- **FR-18.4** A background `IHostedService` retention job must purge completed and failed runs older than a configurable TTL (default: 30 days), never touching suspended runs.
- **FR-18.5** An in-memory `IWorkflowRunRepository` implementation backed by an in-memory EF Core SQLite provider must be used in unit tests — no Azure SQL dependency in the unit-test layer.

### FR-19 HITL Close Loop

- **FR-19.1** When a workflow suspends at a human-approval node, a notification must be dispatched via an `IWorkflowApprovalNotifier` interface immediately after the state transition is committed; failures must be logged as non-fatal and must never roll back the run state persist.
- **FR-19.2** A Teams adapter implementing `IWorkflowApprovalNotifier` must send an Adaptive Card to the configured approver(s) via the Microsoft Graph API, containing: workflow name, node label, the question, and action buttons for each decision option.
- **FR-19.3** An inbound webhook endpoint must receive Teams Adaptive Card action responses and map them to `IWorkflowExecutionOrchestrator.SubmitApproval(runId, decision)` calls on the correct suspended process instance. Every inbound request must be authenticated by validating the Microsoft-signed JWT bearer token issued by Teams — unauthenticated requests must be rejected with HTTP 401.
- **FR-19.4** Each human-approval node must support a configurable timeout (duration + policy: auto-reject / auto-approve / escalate) and a priority-ordered static list of approver email/UPN addresses. On timeout with policy `escalate`, the system notifies the next approver in the list; if the list is exhausted, the node falls back to auto-reject. No dynamic org-chart lookup or separate escalation canvas node is required.
- **FR-19.5** Notification failures must never block workflow suspension; they must be logged as a non-fatal event and the item surfaced in the Review Queue.

### FR-20 Review Queue

- **FR-20.1** A Review Queue page must list all suspended workflow instances with: workflow name, node label, pending question, wait duration, notified party, and current status.
- **FR-20.2** An operator must be able to submit a decision from the Review Queue UI, which calls `IWorkflowExecutionOrchestrator.SubmitApproval(runId, decision)` and updates the run status in real time via SignalR.
- **FR-20.3** Resolved items (timed-out, auto-resolved, or operator-resolved) must move to a "Resolved" section with outcome, resolution timestamp, and resolver identity — they are never silently removed.
- **FR-20.4** The queue must refresh via SignalR push — no manual page reload required to see state changes.

### FR-21 Execution History & Observability

- **FR-21.1** A `WorkflowExecutionEvent` entity must be written by each SK step on entry and exit (step_name, event_type, started_at, ended_at, outcome, error_message).
- **FR-21.2** For AI steps, each execution event must additionally record: model name, input token count, output token count, and latency.
- **FR-21.3** An `IWorkflowObserver` interface must be the single write path for execution events; a default implementation writes to Azure SQL; a second implementation forwards to SignalR for live UI streaming — both can be active simultaneously.
- **FR-21.4** SK `IFunctionInvocationFilter` and `IPromptRenderFilter` hooks must capture LLM call data and route it to `IWorkflowObserver` without any step-level code change.
- **FR-21.5** An Execution History page must show all runs (filterable by workflow, status, date range) and drill-down to a per-run timeline view satisfying US4 acceptance criteria.
- **FR-21.6** Azure Monitor / Application Insights must receive a telemetry event for every workflow start, step completion, HITL suspension, and workflow end — enabling alerting and dashboarding outside the app.

### FR-22 Connector Configuration

- **FR-22.1** A Connector Settings page must allow an administrator to add, edit, delete, and health-check named connector instances for: Azure DevOps Boards, Microsoft Teams, and Office 365 Email (via Microsoft Graph API). The Azure DevOps connector must support **create work item only** in this release; update, query, and state-transition operations are explicitly deferred.
- **FR-22.2** All connector credentials (PATs, client secrets, connection strings) must be stored in Azure Key Vault via the Forge Vault / `IConfiguration` / Key Vault provider pattern — never in the application database or source.
- **FR-22.3** A connector health check must be triggerable on demand and must run automatically on application startup; results must be cached for no longer than 60 seconds.
- **FR-22.4** The node realization pipeline (spec 007) must be updated to resolve connector bindings by name from the configured connector registry, and flag nodes as "blocked — needs setup" when no healthy connector of the required type exists.
- **FR-22.5** A pluggable `IConnectorAdapter` interface must back each connector type so additional connector types can be registered without modifying existing steps.

### FR-23 Whole-Workflow Generation from Chat

- **FR-23.1** The Chat panel in the workflow builder must accept a plain-English workflow description and, on submission, invoke the LLM to generate a complete node-and-edge graph.
- **FR-23.2** The generated graph must be rendered on the canvas as a connected workflow (nodes placed, labelled in the user's language, edges wired) — ready for user review and realization.
- **FR-23.3** If the description is ambiguous (missing trigger, missing terminal node, or conflicting instructions), the chat assistant must ask exactly one clarifying question before generating.
- **FR-23.4** The generated workflow must be compatible with the realization pipeline (spec 007) without any additional manual step.

### FR-24 Definition of Ready Validation

- **FR-24.1** Before a workflow run is submitted, a configurable set of DoR rules must be evaluated; any failing rule must block the run and be presented in plain language.
- **FR-24.2** Default DoR rules must include: at least one trigger node present, all nodes realized and unblocked, all bound connectors healthy, all human-approval nodes have a configured approver.
- **FR-24.3** An administrator must be able to enable or disable DoR rules from the Settings page without a code deployment. Rule ordering is fixed by DI registration sequence at deployment time; dynamic reordering is deferred.
- **FR-24.4** DoR rule evaluation must be exposed as an `IWorkflowReadinessRule` interface so custom rules can be registered via DI without modifying the core validator.

---

## Success Criteria

1. **Persistence survives restart**: A suspended workflow run is recoverable after a full application restart in 100% of tested cases — measured by 10 restart cycles with a parked workflow.
2. **HITL loop closes**: From workflow suspension to Teams notification delivery takes under 30 seconds in a healthy environment — measured end-to-end in integration tests.
3. **Review Queue accuracy**: 100% of suspended runs appear in the Review Queue within 5 seconds of suspension, and leave the queue within 5 seconds of resolution — verified by real-time SignalR tests.
4. **Execution traceability**: Every step of every run produces a queryable execution event; a failed run's root cause is identifiable from the timeline alone — verified by injecting deliberate failures and querying the log.
5. **LLM cost visibility**: Token counts and model name are recorded for every AI step in 100% of runs — verified by comparing recorded counts against raw API response metadata.
6. **Connector health gating**: A workflow with an unhealthy connector cannot reach "production-ready" status and cannot be run — verified with connectors intentionally misconfigured.
7. **Credentials never in DB**: Zero connector credential values appear in any application database table or application log — verified by security scan of DB and log output after connector setup.
8. **Whole-workflow generation**: A 5-node workflow described in one sentence appears on the canvas, fully connected, within 30 seconds, and can be realized and run without any additional manual wiring.
9. **DoR blocking**: A workflow missing a trigger node, an unrealized node, or an unhealthy connector cannot be submitted for execution — verified with each failure mode individually.
10. **Retention correctness**: Completed runs older than the configured TTL are purged; suspended runs are never purged regardless of age — verified by retention job unit tests and integration tests.

---

## Key Entities

| Entity | Description |
|--------|-------------|
| **WorkflowRun** | A single execution instance of a workflow: run_id, workflow_id, status (`Running | Paused | Completed | Failed | TimedOut | Cancelled`), timestamps (created, suspended, resumed, completed), triggered-by identity. |
| **WorkflowExecutionEvent** | A step-level audit record: run_id, step_name, event_type, started_at, ended_at, outcome, error_message. AI steps also carry model name, input/output token counts, latency. |
| **ConnectorInstance** | A named, configured connector: instance_id, type (AzureDevOps / Teams / Email), display_name, Key Vault secret reference, health_status, last_checked_at. |
| **HitlPendingItem** | A projected view of a suspended run awaiting human input: run_id, node_label, pending_question, approver_chain (ordered list of email/UPN), current_approver_index, suspended_at, timeout_at, escalation_policy (auto-reject / auto-approve / escalate). |
| **DoRRule** | A configurable validation rule: rule_id, name, description, enabled, implementation_type (resolved via DI). Rule ordering is by DI registration sequence (fixed at deployment time). |

---

## Assumptions

1. Azure SQL is available as the persistence target; EF Core is the ORM. SQLite is acceptable for local development only if connection-string driven and not hard-coded.
2. Microsoft Graph API access (Teams + Email) is available via an app registration with delegated or application permissions; the app registration is pre-provisioned by the operator.
3. Azure Key Vault is reachable from the application host; the Managed Identity or service principal that the app runs as has `secrets/get` permission.
4. Azure Monitor / Application Insights connection string is provided via configuration; if absent, telemetry is silently suppressed (no-op implementation).
5. V1 persistence uses `IWorkflowRunRepository` backed by EF Core — status transitions are written to Azure SQL on each change, and Paused runs are rehydrated on startup. SK's `IProcessStateManager` (SKEXP0080 experimental) is deferred to a follow-on spec to avoid binding to an unstable API surface; hand-rolling a parallel state machine remains forbidden by Article VII.
6. Resume signals are injected via `IWorkflowExecutionOrchestrator.SubmitApproval(runId, decision)`, which resolves the in-memory `ApprovalTcs`. `IExternalKernelProcessMessageChannel` may replace this in a follow-on spec when full SK process state persistence (IProcessStateManager) is introduced.
7. Single-tenant deployment in this release; multi-tenant isolation of connector credentials and run data is out of scope.
8. Peak concurrent in-flight workflow runs will not exceed 50. No Azure Service Bus, SignalR backplane, or Application Insights adaptive sampling is required. Direct Graph API delivery to Teams is sufficient at this scale.
9. Connector credentials are entered by a human administrator — the application never generates, rotates, or derives them.

---

## Dependencies

- EF Core `IWorkflowRunRepository` — V1 persistence (SK `IProcessStateManager` deferred; see Assumption 5).
- SK `IFunctionInvocationFilter` / `IPromptRenderFilter` — LLM observability hooks.
- Microsoft Graph API — Teams Adaptive Cards and Office 365 Email.
- Azure DevOps REST API — Boards work-item connector.
- Azure Key Vault — credential storage (via `IConfiguration` Key Vault provider).
- Azure Monitor / Application Insights SDK — external telemetry.
- EF Core + Azure SQL provider — persistence layer.
- SignalR (already in project) — real-time Review Queue and Execution History updates.
- Spec 007 (Node Realization) — connector binding resolution during realization.
- Existing `IWorkflowReadinessService` — extended to enforce DoR rules.

---

## Out of Scope

- Multi-tenant isolation or per-tenant credential namespacing.
- Connector types beyond Azure DevOps, Teams, and Email in this release (ServiceNow, Jira, GitHub are follow-on).
- Credential rotation, automatic secret refresh, or Key Vault lifecycle management.
- Fine-grained Azure RBAC for Review Queue access (all authenticated users can act; role-based access control is a follow-on).
- A mobile-optimised Teams app or bot framework integration (Adaptive Card webhook is sufficient).
- Definition of Done (post-execution) validation — scoped to Definition of Ready (pre-execution) in this release.
- LLM prompt caching strategies or cost optimisation beyond recording token counts.
