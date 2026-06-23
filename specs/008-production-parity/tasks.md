---
description: "Task list for Production Platform Parity implementation"
---

# Tasks: Production Platform Parity — Azure-Stack Completeness

**Input**: Design documents from `specs/008-production-parity/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: INCLUDED — Article V mandates Red→Green→Refactor TDD with three-layer separation
(unit mocked, integration real, Playwright E2E). Write each test before its implementation
and confirm it fails first.

**Organization**: Tasks grouped by user story (US1–US7) for independent delivery.
US1 and US2 are P0 blockers — all other stories depend on the foundational persistence layer.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unresolved dependencies)
- **[Story]**: US1–US7 (Setup, Foundational, Polish phases have no story label)
- File paths are repo-relative.

## Path Conventions

`src/DBAIAzure.Core` · `src/DBAIAzure.Storage` · `src/DBAIAzure.Web`
`src/DBAIAzure.Processes` · `src/DBAIAzure.Connectors`
`tests/DBAIAzure.Tests` (xUnit) · `tests/DBAIAzure.E2ETests` (Playwright)

---

## Phase 1: Setup

**Purpose**: Packages, folder scaffolding, and configuration keys.

- [X] T001 Add NuGet packages to solution: `Microsoft.Bot.Connector.Authentication` (Teams JWT), `Microsoft.Graph` (Graph API Adaptive Cards), `Microsoft.ApplicationInsights.AspNetCore` (Azure Monitor), `Microsoft.EntityFrameworkCore.SqlServer` (Azure SQL provider) — update relevant `.csproj` files.
- [X] T002 [P] Create folder `src/DBAIAzure.Core/Configuration/` (namespace `DBAIAzure.Core.Configuration`).
- [X] T003 [P] Create folder `src/DBAIAzure.Web/Rules/` (namespace `DBAIAzure.Web.Rules`) for DoR rule implementations.
- [X] T004 [P] Create folder `src/DBAIAzure.Web/Hubs/` (namespace `DBAIAzure.Web.Hubs`) for SignalR hubs.
- [X] T005 [P] Create folder `src/DBAIAzure.Web/Controllers/` (namespace `DBAIAzure.Web.Controllers`) for webhook controller.
- [X] T006 [P] Add config keys to `src/DBAIAzure.Web/appsettings.json`: `Storage:ConnectionString` (null by default — triggers SQL Server provider when set), `RetentionDays` (default 30), `DorRules:DisabledRuleNames` (empty array).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Domain types, interfaces, EF entities, and migration that every user story depends on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain Models

- [X] T007 [P] Create `WorkflowEventType` enum (StepStarted, StepCompleted, StepFailed, StepSkipped, LlmCallCompleted, RunPaused, RunResumed) in `src/DBAIAzure.Core/Models/WorkflowEventType.cs`.
- [X] T008 [P] Create `WorkflowRunRecord` record (RunId, WorkflowId, WorkflowName, Status, TriggeredBy, StartedAt, SuspendedAt?, ResumedAt?, CompletedAt?, FailureReason?) in `src/DBAIAzure.Core/Models/WorkflowRunRecord.cs` per data-model.md.
- [X] T009 [P] Create `WorkflowExecutionEvent` record (EventId, RunId, NodeId, NodeLabel, EventType, OccurredAt, DurationMs?, Outcome?, LlmModelName?, LlmInputTokens?, LlmOutputTokens?) in `src/DBAIAzure.Core/Models/WorkflowExecutionEvent.cs` per data-model.md.
- [X] T010 [P] Create `HitlPendingItem` record (RunId, WorkflowName, NodeLabel, Question, ApproverChain, CurrentApproverIndex, SuspendedAt, TimeoutAt?, EscalationPolicy) in `src/DBAIAzure.Core/Models/HitlPendingItem.cs` per data-model.md.
- [X] T011 [P] Create `DorRuleResult` record (Passed, RuleName, FailureReason?) in `src/DBAIAzure.Core/Models/DorRuleResult.cs`.
- [X] T012 [P] Create `WorkflowGenerationResult` record (Nodes: GeneratedNode[], Edges: GeneratedEdge[], ClarifyingQuestion?) plus `GeneratedNode` (Id, NodeType, Label, GoalPrompt?) and `GeneratedEdge` (SourceNodeId, TargetNodeId) in `src/DBAIAzure.Core/Models/WorkflowGenerationResult.cs`.
- [X] T013 [P] Create `DorRuleSettings` record (DisabledRuleNames: string[]) in `src/DBAIAzure.Core/Configuration/DorRuleSettings.cs`; bind to `"DorRules"` config section.

### Interfaces

- [X] T014 [P] Create `IWorkflowRunRepository` interface (CreateAsync, UpdateAsync, GetAsync, ListByStatusAsync, ListAsync, PurgeTerminalRunsOlderThanAsync) in `src/DBAIAzure.Core/Interfaces/IWorkflowRunRepository.cs` per contracts/iworkflow-run-repository.md.
- [X] T015 [P] Create `IWorkflowObserver` interface (RecordEventAsync) in `src/DBAIAzure.Core/Interfaces/IWorkflowObserver.cs` per contracts/iworkflow-observer.md.
- [X] T016 [P] Create `IWorkflowApprovalNotifier` interface (NotifyAsync, EscalateAsync) in `src/DBAIAzure.Core/Interfaces/IWorkflowApprovalNotifier.cs` per contracts/iworkflow-approval-notifier.md.
- [X] T017 [P] Create `IWorkflowReadinessRule` interface (RuleName, Description, CheckAsync) in `src/DBAIAzure.Core/Interfaces/IWorkflowReadinessRule.cs` per contracts/iworkflow-readiness-rule.md.
- [X] T018 [P] Create `IWorkflowPreRunValidator` interface (ValidateAsync) in `src/DBAIAzure.Core/Interfaces/IWorkflowPreRunValidator.cs` per contracts/iworkflow-readiness-rule.md.

### EF Core Entities & Migration

- [X] T019 [P] Create `WorkflowRunEntity` class (RunId PK, WorkflowId indexed, WorkflowName, Status indexed, TriggeredBy, StartedAt, SuspendedAt?, ResumedAt?, CompletedAt?, FailureReason?) in `src/DBAIAzure.Storage/Entities/WorkflowRunEntity.cs` per data-model.md EF config.
- [X] T020 [P] Create `WorkflowExecutionEventEntity` class (EventId PK Guid, RunId FK indexed, NodeId, NodeLabel, EventType, OccurredAt indexed, DurationMs?, Outcome?, LlmModelName?, LlmInputTokens?, LlmOutputTokens?) in `src/DBAIAzure.Storage/Entities/WorkflowExecutionEventEntity.cs` per data-model.md; composite index on (RunId, OccurredAt).
- [X] T021 Extend `PipelineDbContext` with `DbSet<WorkflowRunEntity> WorkflowRuns` and `DbSet<WorkflowExecutionEventEntity> WorkflowExecutionEvents` including cascade delete from WorkflowRuns → WorkflowExecutionEvents, in `src/DBAIAzure.Storage/PipelineDbContext.cs`.
- [ ] T022 Add EF Core migration `008_AddWorkflowRunsAndEvents` via `dotnet ef migrations add 008_AddWorkflowRunsAndEvents --project src/DBAIAzure.Storage --startup-project src/DBAIAzure.Web`; verify generated migration creates both tables with correct indexes.
- [X] T023 Update `Program.cs` provider-selection: if `Storage:ConnectionString` is present use `UseSqlServer(connectionString)`; otherwise fall back to `UseSqlite(sqlitePath)`; call `dbContext.Database.MigrateAsync()` at startup so new tables are created automatically in `src/DBAIAzure.Web/Program.cs`.

**Checkpoint**: Foundational types ready — user story implementation can begin.

---

## Phase 3: US1 — Run persistence survives restart (Priority: P0) 🎯 MVP start

**Goal**: Every workflow run's status is written to the database on each transition; all `Paused`
runs are rehydrated into the orchestrator on application startup.

**Independent Test**: Run a workflow to a `HumanApproval` pause, stop the application, restart,
open `/review-queue` and confirm the paused run is listed with correct data (quickstart Scenario 1).

### Tests (write first, confirm failing)

- [ ] T024 [P] [US1] Unit: `EfWorkflowRunRepository.CreateAsync` writes a record; `GetAsync` returns it with matching fields — in `tests/DBAIAzure.Tests/WorkflowRunRepositoryTests.cs` (in-memory EF Core SQLite provider).
- [ ] T025 [P] [US1] Unit: `ListByStatusAsync(Paused)` returns only Paused runs, ordered by StartedAt descending — in `tests/DBAIAzure.Tests/WorkflowRunRepositoryTests.cs`.
- [ ] T026 [P] [US1] Unit: `PurgeTerminalRunsOlderThanAsync` deletes Completed/Failed/TimedOut/Cancelled runs past cutoff; never touches Paused or Running runs — in `tests/DBAIAzure.Tests/WorkflowRunRepositoryTests.cs`.
- [ ] T027 [P] [US1] Integration: full round-trip — start run, pause, dispose DbContext, re-create context, assert Paused record exists — in `tests/DBAIAzure.Tests/WorkflowRunRepositoryIntegrationTests.cs`.

### Implementation

- [X] T028 [US1] Implement `EfWorkflowRunRepository` (IWorkflowRunRepository backed by EF Core; inject `IDbContextFactory<PipelineDbContext>`) in `src/DBAIAzure.Storage/Repositories/EfWorkflowRunRepository.cs`.
- [X] T029 [US1] Inject `IWorkflowRunRepository` into `WorkflowExecutionOrchestrator` constructor in `src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs`.
- [X] T030 [US1] Call `CreateAsync` in `StartRunAsync` immediately after adding to `_runs`; call `UpdateAsync` on every status transition (Running→Paused, →Completed, →Failed, →TimedOut, →Cancelled) — in `src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs`.
- [X] T031 [US1] Add startup rehydration: on `IHostedService.StartAsync` (or `Program.cs` after DI build), call `IWorkflowRunRepository.ListByStatusAsync(Paused)` and for each result add a `WorkflowRunState` to `_runs` with a fresh `ApprovalTcs`, so `SubmitApproval` can resolve it — in `src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs` or a dedicated startup service.
- [X] T032 [US1] Register `IWorkflowRunRepository → EfWorkflowRunRepository` (scoped) in `src/DBAIAzure.Web/Program.cs`.

---

## Phase 4: US2 — HITL close loop via Teams (Priority: P0)

**Goal**: On suspension, a Teams Adaptive Card is sent to the first approver; clicking Approve/Reject
in Teams resumes the workflow. Escalation chain fires on timeout.

**Independent Test**: Run a workflow to an approval pause; confirm Teams card arrives within 30s;
click Approve; confirm workflow reaches Completed (quickstart Scenario 2).

### Tests (write first, confirm failing)

- [ ] T033 [P] [US2] Unit: `WorkflowApprovalTeamsNotifier.NotifyAsync` calls Graph API with an Adaptive Card body containing `runId`, `workflowName`, and all `decisionOptions` as action buttons — mock Graph SDK in `tests/DBAIAzure.Tests/TeamsApprovalNotifierTests.cs`.
- [ ] T034 [P] [US2] Unit: `WorkflowApprovalTeamsNotifier.EscalateAsync` targets `approverChain[currentApproverIndex + 1]`; throws `InvalidOperationException` if chain is exhausted — in `tests/DBAIAzure.Tests/TeamsApprovalNotifierTests.cs`.
- [ ] T035 [P] [US2] Unit: `TeamsWebhookController` returns HTTP 401 when `Authorization` header is absent or JWT signature is invalid — in `tests/DBAIAzure.Tests/TeamsWebhookControllerTests.cs` (mock Bot Framework validator).
- [ ] T036 [P] [US2] Unit: `TeamsWebhookController` calls `IWorkflowExecutionOrchestrator.SubmitApproval(runId, approved)` when JWT validates and action payload is well-formed — in `tests/DBAIAzure.Tests/TeamsWebhookControllerTests.cs`.

### Implementation

- [X] T037 [US2] Implement `WorkflowApprovalTeamsNotifier` (send Adaptive Card via `GraphServiceClient.Users[upn].Chats.PostAsync` or channel message; embed `runId` + decision in `Action.Submit` data) in `src/DBAIAzure.Web/Services/WorkflowApprovalTeamsNotifier.cs`.
- [X] T038 [US2] Implement `WorkflowRunHub` (SignalR hub with `SendRunUpdate(runId, status)` method; group per runId) in `src/DBAIAzure.Web/Hubs/WorkflowRunHub.cs`.
- [X] T039 [US2] Implement `TeamsWebhookController` (minimal API controller; validate JWT via `BotFrameworkAuthentication`; parse `runId` + approved from action data; call `SubmitApproval`) in `src/DBAIAzure.Web/Controllers/TeamsWebhookController.cs`.
- [X] T040 [US2] Wire `IWorkflowApprovalNotifier.NotifyAsync` into `WorkflowExecutionOrchestrator.ExecuteRunAsync` immediately after the run transitions to `Paused`; log and swallow notification failures — in `src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs`.
- [ ] T041 [US2] Implement escalation timeout loop in `WorkflowExecutionOrchestrator`: after suspension, start a timer; on expiry check `EscalationPolicy` — call `EscalateAsync` (advance `CurrentApproverIndex`) or auto-resolve the `ApprovalTcs` per policy; repeat until chain exhausted or decision received — in `src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs`.
- [X] T042 [US2] Update `ApprovalNodeConfig` to include `ApproverChain: string[]`, `TimeoutMinutes: int`, `EscalationPolicy: string` in `src/DBAIAzure.Core/Models/NodeConfig/ApprovalNodeConfig.cs` (aligns with data-model.md HitlPendingItem).
- [X] T043 [US2] Register `IWorkflowApprovalNotifier → WorkflowApprovalTeamsNotifier`, `WorkflowRunHub`, and `TeamsWebhookController` (via `MapControllers`) in `src/DBAIAzure.Web/Program.cs`; add `services.AddControllers()` and `app.MapControllers()`.

---

## Phase 5: US3 — Review Queue (Priority: P1)

**Goal**: Operators see all paused workflows in one page; can approve/reject inline; queue updates
in real time without a page refresh.

**Independent Test**: Pause two workflows; open `/review-queue`; confirm both appear; submit one
decision; confirm it leaves pending and enters Resolved without manual refresh (quickstart Scenario 3).

### Tests (write first, confirm failing)

- [ ] T044 [P] [US3] Unit: `ReviewQueue.razor` renders one row per `HitlPendingItem` returned by `IWorkflowRunRepository.ListByStatusAsync(Paused)` — bUnit component test in `tests/DBAIAzure.Tests/ReviewQueueComponentTests.cs`.
- [ ] T045 [P] [US3] Unit: clicking Approve button calls `IWorkflowExecutionOrchestrator.SubmitApproval(runId, true)` — bUnit test in `tests/DBAIAzure.Tests/ReviewQueueComponentTests.cs`.

### Implementation

- [X] T046 [US3] Implement `ReviewQueue.razor` page at route `/review-queue`; list `HitlPendingItem` projections from `IWorkflowRunRepository`; Approve/Reject buttons call `SubmitApproval`; Resolved section shows terminal items with outcome + timestamp — in `src/DBAIAzure.Web/Pages/ReviewQueue.razor`.
- [ ] T047 [US3] Subscribe `ReviewQueue.razor` to `WorkflowRunHub` via `HubConnection`; on `RunStatusChanged` event re-query and re-render without user action — in `src/DBAIAzure.Web/Pages/ReviewQueue.razor`.
- [ ] T048 [US3] Extend `WorkflowExecutionOrchestrator` to invoke `WorkflowRunHub.SendRunUpdate(runId, status)` (via `IHubContext<WorkflowRunHub>`) on every status transition — in `src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs`.
- [X] T049 [US3] Add **Review Queue** nav link to `src/DBAIAzure.Web/Shared/MainLayout.razor` and `src/DBAIAzure.Web/Shared/WorkflowBuilderLayout.razor`.

---

## Phase 6: US4 — Execution History & LLM Tracing (Priority: P1)

**Goal**: Every step and every LLM call is recorded; `/runs` lists all runs; `/runs/{id}` shows a
per-step timeline with model name and token counts for AI steps.

**Independent Test**: Run a workflow with one AI node; navigate to `/runs/{id}`; confirm AI step
row shows model name and token counts (quickstart Scenario 4).

### Tests (write first, confirm failing)

- [ ] T050 [P] [US4] Unit: `SqlWorkflowObserver.RecordEventAsync` writes a `WorkflowExecutionEventEntity` to the DB; swallows exceptions rather than propagating — in `tests/DBAIAzure.Tests/WorkflowObserverTests.cs`.
- [ ] T051 [P] [US4] Unit: SK `IFunctionInvocationFilter` implementation emits a `LlmCallCompleted` event with non-null `LlmModelName`, `LlmInputTokens`, `LlmOutputTokens` — mock `IFunctionInvocationContext` in `tests/DBAIAzure.Tests/WorkflowObserverTests.cs`.
- [ ] T052 [P] [US4] Unit: observer fan-out — when first observer throws, second observer still receives the event — in `tests/DBAIAzure.Tests/WorkflowObserverTests.cs`.

### Implementation

- [X] T053 [US4] Implement `SqlWorkflowObserver` (IWorkflowObserver; writes to `WorkflowExecutionEvents` via `IDbContextFactory<PipelineDbContext>`; catches and logs all exceptions) in `src/DBAIAzure.Web/Services/SqlWorkflowObserver.cs`.
- [X] T054 [US4] Implement `SignalRWorkflowObserver` (IWorkflowObserver; pushes event to `WorkflowRunHub` via `IHubContext`; fire-and-forget) in `src/DBAIAzure.Web/Services/SignalRWorkflowObserver.cs`.
- [X] T055 [US4] Implement `AzureMonitorWorkflowObserver` (IWorkflowObserver; calls `TelemetryClient.TrackEvent` with event properties; conditionally registered) in `src/DBAIAzure.Web/Services/AzureMonitorWorkflowObserver.cs`.
- [X] T056 [US4] Implement SK `WorkflowFunctionInvocationFilter` (IFunctionInvocationFilter; captures model id and usage tokens from `FunctionResult`; emits `LlmCallCompleted` via `IEnumerable<IWorkflowObserver>`) in `src/DBAIAzure.Web/Services/WorkflowFunctionInvocationFilter.cs`.
- [X] T057 [US4] Register observers: `services.AddScoped<IWorkflowObserver, SqlWorkflowObserver>()`, `services.AddScoped<IWorkflowObserver, SignalRWorkflowObserver>()`, conditional Azure Monitor registration, and `WorkflowFunctionInvocationFilter` on kernel factory — in `src/DBAIAzure.Web/Program.cs`.
- [ ] T058 [US4] Wire observer fan-out into `WorkflowExecutionOrchestrator`: emit `StepStarted`/`StepCompleted`/`StepFailed` events per node state transition by resolving `IEnumerable<IWorkflowObserver>` — in `src/DBAIAzure.Processes/Pipeline/WorkflowExecutionOrchestrator.cs`.
- [X] T059 [US4] Implement `RunHistory.razor` at route `/runs`: paginated table of `WorkflowRunRecord` (workflow name, status badge, duration, triggered-by, started-at); filter controls for status and date range — in `src/DBAIAzure.Web/Pages/RunHistory.razor`.
- [X] T060 [US4] Implement `RunHistoryDetail.razor` at route `/runs/{runId}`: chronological timeline of `WorkflowExecutionEvent` rows; AI steps show model + token counts; error steps show `Outcome` in plain language — in `src/DBAIAzure.Web/Pages/RunHistoryDetail.razor`.
- [X] T061 [US4] Add **Run History** nav link to `src/DBAIAzure.Web/Shared/MainLayout.razor` and `src/DBAIAzure.Web/Shared/WorkflowBuilderLayout.razor`.

---

## Phase 7: US5 — Connector Config UI (Priority: P1)

**Goal**: Administrator can add, edit, delete, and health-check named Azure DevOps, Teams, and
Email connector instances from a UI page; credentials stored via Key Vault reference.

**Independent Test**: Add an Azure DevOps connector; trigger health check; confirm Healthy; set an
invalid PAT; re-check; confirm Unhealthy; confirm no credential appears in DB (quickstart Scenario 5).

### Tests (write first, confirm failing)

- [ ] T062 [P] [US5] Unit: `ConnectorHealthChecker` returns cached result on second call within 60 seconds (no second HTTP call made) — in `tests/DBAIAzure.Tests/ConnectorHealthCheckerTests.cs`.
- [ ] T063 [P] [US5] Unit: `ConnectorSettingsPanel.razor` renders one row per saved `ConnectorConfig`; Delete button calls `IConnectorConfigRepository.DeleteAsync` — bUnit test in `tests/DBAIAzure.Tests/ConnectorSettingsPanelTests.cs`.

### Implementation

- [ ] T064 [US5] Implement `ConnectorSettingsPanel.razor` at route `/settings/connectors`: list named connectors with type, health status, last-checked; Add / Edit / Delete / Check Health actions; credential fields marked `type="password"` and never echoed back — in `src/DBAIAzure.Web/Shared/ConnectorSettingsPanel.razor`.
- [ ] T065 [US5] Add 60-second result cache to `ConnectorHealthChecker.CheckAsync` using `IMemoryCache`; keyed by connector instance id — in `src/DBAIAzure.Connectors/ConnectorHealthChecker.cs` (EXTEND existing).
- [ ] T066 [US5] Configure Key Vault credential provider in `Program.cs`: if `KeyVault:Uri` is set, add `AddAzureKeyVault(vaultUri, credential)` to the configuration builder so `IConfiguration["Connectors:<name>:Secret"]` resolves from Key Vault; fall back to user secrets in dev — in `src/DBAIAzure.Web/Program.cs`.
- [ ] T067 [US5] Add **Settings** nav link (→ `/settings/connectors`) to `src/DBAIAzure.Web/Shared/MainLayout.razor` and `src/DBAIAzure.Web/Shared/WorkflowBuilderLayout.razor`.

---

## Phase 8: US6 — Whole-workflow generation from chat (Priority: P2)

**Goal**: Plain-English description in the chat panel generates a fully connected, canvas-ready
workflow within 30 seconds; compatible with node realization without additional wiring.

**Independent Test**: Type a 5-node workflow description; confirm graph appears on canvas within 30s,
fully wired; realize and run without additional manual steps (quickstart Scenario 6).

### Tests (write first, confirm failing)

- [ ] T068 [P] [US6] Unit: `WorkflowDesignSkillService.GenerateWorkflowAsync` with mocked `IStructuredCompletionService` returns a `WorkflowGenerationResult` whose nodes match expected `WorkflowNodeType` values and edges form a connected graph — in `tests/DBAIAzure.Tests/WorkflowGenerationTests.cs`.
- [ ] T069 [P] [US6] Unit: when `WorkflowGenerationResult.ClarifyingQuestion` is non-null, `GenerateWorkflowAsync` returns an empty node list and a non-empty question string — in `tests/DBAIAzure.Tests/WorkflowGenerationTests.cs`.

### Implementation

- [X] T070 [US6] Define `WorkflowGenerationResult` JSON schema for `IStructuredCompletionService` (GeneratedNode array with id/nodeType/label/goalPrompt, GeneratedEdge array with sourceNodeId/targetNodeId, optional clarifyingQuestion) in `src/DBAIAzure.Web/Services/WorkflowGenerationSchema.cs`.
- [X] T071 [US6] Add `GenerateWorkflowAsync(string description, CancellationToken)` method to `WorkflowDesignSkillService` (calls `IStructuredCompletionService.GetStructuredAsync<WorkflowGenerationResult>`; maps result to `WorkflowDefinition` nodes + edges; returns null on clarifying question path) in `src/DBAIAzure.Web/Services/WorkflowDesignSkillService.cs` (EXTEND).
- [X] T072 [US6] Add chat-generation UI to `WorkflowBuilder.razor`: text input + **Generate Workflow** button in the chat panel; on submit call `GenerateWorkflowAsync`; if result has `ClarifyingQuestion` display it; else render nodes + edges on canvas via `WorkflowBuilderService` — in `src/DBAIAzure.Web/Pages/WorkflowBuilder.razor` (EXTEND).

---

## Phase 9: US7 — Definition of Ready validation (Priority: P2)

**Goal**: Configurable DoR rules block a run when the workflow is not ready; failing checks are
listed in plain language above the Run button; rules can be disabled without a deployment.

**Independent Test**: Run a workflow with an unrealized node; confirm Run is blocked with a
plain-language reason; realize all nodes; confirm Run proceeds (quickstart Scenario 7).

### Tests (write first, confirm failing)

- [ ] T073 [P] [US7] Unit: `TriggerNodePresentRule.CheckAsync` fails when workflow has no trigger node; passes when one exists — in `tests/DBAIAzure.Tests/WorkflowPreRunValidatorTests.cs`.
- [ ] T074 [P] [US7] Unit: `AllNodesRealizedRule.CheckAsync` fails when any node has `IsConfigured = false` — in `tests/DBAIAzure.Tests/WorkflowPreRunValidatorTests.cs`.
- [ ] T075 [P] [US7] Unit: `ConnectorsHealthyRule.CheckAsync` fails when any bound connector's last health check is Unhealthy — in `tests/DBAIAzure.Tests/WorkflowPreRunValidatorTests.cs`.
- [ ] T076 [P] [US7] Unit: `ApprovalNodesConfiguredRule.CheckAsync` fails when an approval node's `ApprovalNodeConfig.ApproverChain` is empty — in `tests/DBAIAzure.Tests/WorkflowPreRunValidatorTests.cs`.
- [ ] T077 [P] [US7] Unit: `WorkflowPreRunValidator.ValidateAsync` skips rules whose `RuleName` appears in `DorRuleSettings.DisabledRuleNames`; returns failing rules first — in `tests/DBAIAzure.Tests/WorkflowPreRunValidatorTests.cs`.

### Implementation

- [X] T078 [P] [US7] Implement `TriggerNodePresentRule` in `src/DBAIAzure.Web/Rules/TriggerNodePresentRule.cs`.
- [X] T079 [P] [US7] Implement `AllNodesRealizedRule` in `src/DBAIAzure.Web/Rules/AllNodesRealizedRule.cs`.
- [X] T080 [P] [US7] Implement `ConnectorsHealthyRule` (resolves `IConnectorHealthChecker` from DI; checks all connectors bound to realized nodes) in `src/DBAIAzure.Web/Rules/ConnectorsHealthyRule.cs`.
- [X] T081 [P] [US7] Implement `ApprovalNodesConfiguredRule` in `src/DBAIAzure.Web/Rules/ApprovalNodesConfiguredRule.cs`.
- [X] T082 [US7] Implement `WorkflowPreRunValidator` (resolves `IEnumerable<IWorkflowReadinessRule>`; respects `IOptionsMonitor<DorRuleSettings>`; returns results with failing rules first) in `src/DBAIAzure.Web/Services/WorkflowPreRunValidator.cs`.
- [X] T083 [US7] Register rules and validator: `services.AddScoped<IWorkflowReadinessRule, TriggerNodePresentRule>()` × 4 rules + `services.AddScoped<IWorkflowPreRunValidator, WorkflowPreRunValidator>()` + `services.Configure<DorRuleSettings>(...)` — in `src/DBAIAzure.Web/Program.cs`.
- [X] T084 [US7] Inject `IWorkflowPreRunValidator` into `WorkflowBuilder.razor`; call `ValidateAsync` before enabling Run button; render failing rule reasons as a blocking list above the button — in `src/DBAIAzure.Web/Pages/WorkflowBuilder.razor` (EXTEND).

---

## Final Phase: Polish & Cross-cutting Concerns

- [X] T085 [P] Implement retention `IHostedService` (`WorkflowRunRetentionService`) that runs daily and calls `IWorkflowRunRepository.PurgeTerminalRunsOlderThanAsync(DateTimeOffset.UtcNow - TimeSpan.FromDays(retentionDays))`; `retentionDays` read from `IConfiguration["RetentionDays"]` (default 30) — in `src/DBAIAzure.Web/Services/WorkflowRunRetentionService.cs`.
- [X] T086 [P] Register `WorkflowRunRetentionService` as `services.AddHostedService<WorkflowRunRetentionService>()` in `src/DBAIAzure.Web/Program.cs`.
- [ ] T087 [P] E2E Playwright test `ReviewQueueTests.OperatorApprovalFlow`: launch app, run workflow to approval pause, open `/review-queue`, confirm paused item, click Approve, confirm queue updates to Resolved and run reaches Completed — in `tests/DBAIAzure.E2ETests/Tests/ReviewQueueTests.cs`.
- [X] T088 [P] E2E Playwright test `RunHistoryTests.RunListAndDrillDown`: complete a workflow, open `/runs`, confirm run row exists with correct status badge, click through to `/runs/{id}`, confirm step timeline is populated with at least one event row — in `tests/DBAIAzure.E2ETests/Tests/RunHistoryTests.cs`.
- [X] T089 [P] E2E Playwright test `RunHistoryTests.AiStepShowsTokenCounts`: run a workflow containing one AI node, open `/runs/{id}`, assert the AI step row shows non-empty model name and input/output token count columns — in `tests/DBAIAzure.E2ETests/Tests/RunHistoryTests.cs`.
- [ ] T090 [P] E2E Playwright test `ConnectorSettingsTests.AddHealthCheckDelete`: navigate to `/settings/connectors`, add an Azure DevOps connector entry (using test credentials from user secrets), trigger health check, assert Healthy badge appears, delete the connector, assert list is empty — in `tests/DBAIAzure.E2ETests/Tests/ConnectorSettingsTests.cs`.
- [ ] T091 [P] [US5] Create `IConnectorAdapter` interface (ConnectorType, ExecuteAsync, HealthCheckAsync) in `src/DBAIAzure.Core/Interfaces/IConnectorAdapter.cs`; implement `AzureDevOpsConnectorAdapter` (create work item only) in `src/DBAIAzure.Connectors/AzureDevOpsConnectorAdapter.cs`; implement `TeamsConnectorAdapter` (send message) in `src/DBAIAzure.Connectors/TeamsConnectorAdapter.cs`.
- [ ] T092 [US5] Update `WorkflowRealizationService` to resolve connector bindings by name: when realizing a node of type `Notify`, `Data`, or `Approval`, query `IConnectorConfigRepository` for a healthy connector of the matching type; if none found, set node status to `Blocked` with message "No healthy connector of required type configured" — in `src/DBAIAzure.Web/Services/WorkflowRealizationService.cs` (EXTEND spec-007 service).
- [X] T093 [P] [US4] Implement `WorkflowPromptRenderFilter` (IPromptRenderFilter; logs SHA-256 hash of rendered prompt to `ILogger<WorkflowPromptRenderFilter>` — never the prompt text itself; emits no observer event in V1; serves as a registered placeholder for future prompt-logging per Article IX) in `src/DBAIAzure.Web/Services/WorkflowPromptRenderFilter.cs`; register on kernel factory in `src/DBAIAzure.Web/Program.cs`.
- [ ] T094 [P] [US5] Integration test: `ConnectorCredentialSecurityTests.CredentialNeverPersistedToDatabase` — save a connector config with a known test-only PAT string, query all EF Core entity tables, assert the PAT string does not appear in any stored column value — in `tests/DBAIAzure.Tests/ConnectorCredentialSecurityTests.cs`.
- [X] T095 Update `CHANGELOG.md` with feature entry under `[Unreleased]`: run persistence, HITL Teams loop, Review Queue, Execution History + LLM tracing, Connector Config UI, whole-workflow chat generation, Definition of Ready validation.

---

## Dependency Graph

```
Phase 1 (Setup)
    └── Phase 2 (Foundational)
            ├── Phase 3 (US1 — Persistence) ──────────────┐
            │       └── Phase 4 (US2 — HITL Loop) ────────┤
            │               ├── Phase 5 (US3 — Review Queue)
            │               └── Phase 6 (US4 — Exec History)
            ├── Phase 7 (US5 — Connector UI)  [parallel with US3/US4]
            ├── Phase 8 (US6 — Chat Gen)       [parallel with US3-US5]
            └── Phase 9 (US7 — DoR Rules)      [parallel with US3-US6]
                        └── Final Phase (Polish)
```

**Notes**:
- US3 (Review Queue) requires US1 (persistence) for the `ListByStatusAsync` query and US2 (HITL loop) for the `SubmitApproval` integration.
- US4 (Execution History) only requires US1 for `WorkflowRunRepository` reads; the observer fan-out is self-contained.
- US5, US6, US7 depend only on Foundational phase — they can be implemented in parallel with US3 and US4.

---

## Parallel Execution Examples

### Sprint 1 — Two developers, P0 unblock

| Dev A | Dev B |
|-------|-------|
| T007–T023 (Foundational domain + EF) | T007–T023 (same foundation — merge on PR) |
| T024–T032 (US1 Persistence) | — |
| T033–T043 (US2 HITL Loop) | T044–T049, T062–T067 (US3 Review Queue + US5 Connector UI) |

### Sprint 2 — Three developers, P1 parallelism

| Dev A | Dev B | Dev C |
|-------|-------|-------|
| T050–T061 (US4 Exec History) | T068–T072 (US6 Chat Gen) | T073–T084 (US7 DoR) |
| T085–T088 (Polish) | | |

---

## Implementation Strategy

**MVP scope** (P0 critical path — unblocks all human-in-the-loop scenarios):
Phase 1 + Phase 2 + Phase 3 (US1) + Phase 4 (US2) = **T001–T043** (43 tasks)

**Phase 1 delivery** adds Review Queue and full execution traceability:
+ Phase 5 (US3) + Phase 6 (US4) + Phase 7 (US5) = T044–T067

**Phase 2 delivery** completes the feature:
+ Phase 8 (US6) + Phase 9 (US7) + Polish = T068–T095 (95 tasks total)
