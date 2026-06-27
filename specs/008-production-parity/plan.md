# Implementation Plan: Production Platform Parity — Azure-Stack Completeness

**Branch**: `feature/production-parity` | **Date**: 2026-06-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/008-production-parity/spec.md`

---

## Summary

Close the gap between the workflow builder canvas (ahead of the Python POC) and everything behind it
(persistence, HITL close loop, observability, connectors, DoR validation). Seven capabilities are
added in a single feature branch, all constrained to the Azure/Microsoft stack and the existing
Semantic Kernel Process Framework primitives:

1. **Run persistence** — `WorkflowExecutionOrchestrator` persists every status transition to Azure SQL
   (SQLite in dev) via a new `IWorkflowRunRepository`, surviving server restarts.
2. **HITL close loop** — on suspension, `IWorkflowApprovalNotifier` sends a Teams Adaptive Card to
   the configured approver chain; the inbound webhook resumes the process via `SubmitApproval`.
3. **Review Queue** — `/review-queue` Blazor page lists all paused runs; operators act without
   opening Teams.
4. **Execution History & LLM Tracing** — `IWorkflowObserver` writes step events and LLM telemetry
   to DB (and optionally Azure Monitor); `/runs` and `/runs/{id}` pages expose the timeline.
5. **Connector Config UI** — `/settings/connectors` page manages Azure DevOps, Teams, and Email
   connector instances; credentials stored in Azure Key Vault.
6. **Whole-workflow chat generation** — `WorkflowDesignSkillService.GenerateWorkflowAsync` produces
   a full node-and-edge graph from a plain-English description.
7. **Definition of Ready validation** — `IWorkflowReadinessRule` strategy pattern with four default
   rules evaluated before every Run.

---

## Technical Context

**Language/Version**: C# / .NET 8 (pinned via `global.json`).

**Primary Dependencies**: Semantic Kernel Process Framework (`SKEXP0080`); EF Core 8 + SQLite
(dev) / SQL Server (prod); Microsoft Graph SDK (Teams Adaptive Cards, Office 365 Email); Azure
Monitor SDK (`TelemetryClient`); Blazor Server + SignalR; `Microsoft.Bot.Connector.Authentication`
(Teams JWT validation); existing `IStructuredCompletionService`, `IConnectorConfigRepository`,
`IConnectorHealthChecker`, `WorkflowDesignSkillService`.

**Storage**: EF Core `PipelineDbContext` extended with two new DbSets (`WorkflowRuns`,
`WorkflowExecutionEvents`). Provider selected at startup: SQL Server when `Storage:ConnectionString`
is set; SQLite otherwise (`Storage:SqlitePath`). New tables use EF Core migrations (not raw SQL).

**Testing**: xUnit unit tests (in-memory `IWorkflowRunRepository`, mock `IWorkflowApprovalNotifier`,
mock `IWorkflowObserver`); xUnit integration tests (real EF Core + SQLite, real Teams JWT validation
logic, real Graph API against a test tenant); Playwright E2E (`scripts/run-e2e.ps1`) for Review
Queue HITL flow and Execution History drill-down.

**Target Platform**: Blazor Server web app on Kestrel. SignalR hub for real-time push (no Azure
SignalR Service needed at ≤50 concurrent runs — research R3).

**Scale**: ≤50 concurrent in-flight runs (clarification Q3). Direct Graph API delivery; no Service
Bus. Single-server SignalR.

**Constraints**: Credentials never in DB or logs (Article IX / FR-22.2); Teams webhook authenticated
via Microsoft-signed JWT (clarification Q1 / FR-19.3); `IProcessStateManager` (full SK process
blob) deferred — V1 persists run records only (research R2); no wildcard process kills (Article II).

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Article | Gate | Assessment |
|---------|------|------------|
| I — Prime Directive | Production-ready, no shortcuts | ✅ Persistence, JWT auth, DoR gating, and observer decoupling are all production patterns — no demo-only stubs. |
| II — Process Protection | No wildcard kills | ✅ N/A to design; honored operationally. |
| III — Branching | Feature branch | ✅ `feature/production-parity`. |
| IV — Code Quality | Naming, XML docs, <40-line methods, nullable | ✅ All new interfaces and services follow existing naming conventions; no magic numbers; injected dependencies via constructor. |
| V — Testing (3-layer + TDD) | Unit mocked, integration real, Playwright E2E, Red→Green | ✅ Unit (mock repo + notifier + observer); integration (real EF Core + SQLite + Teams JWT logic); E2E (Review Queue HITL flow, Execution History). Tests authored before implementation. |
| VI — Docs | CHANGELOG, no ad-hoc status docs | ✅ CHANGELOG updated at PR; only `specs/008-*` pipeline artifacts created here. |
| **VII — Framework-First** | Reuse SK + existing primitives | ✅ **Persistence**: new `IWorkflowRunRepository` backed by EF Core — not a hand-rolled state store. **HITL**: existing `IExternalKernelProcessMessageChannel` + `SubmitApproval`; not a new pause/resume mechanism. **LLM tracing**: SK `IFunctionInvocationFilter` / `IPromptRenderFilter` hooks — not wrapper methods. **Structured output**: existing `IStructuredCompletionService` for workflow generation. **Connectors**: existing `IConnectorConfigRepository` + `IConnectorHealthChecker`. No parallel registries. |
| VIII — Release | Reproducible build | ✅ N/A to design. |
| IX — Secrets | No secrets in source/logs/config | ✅ Connector credentials stored in Azure Key Vault via `IConfiguration` Key Vault provider; never in `ConnectorConfigRecord`; never in `IWorkflowObserver` payloads. |
| X — Verification & Proof | Evidence, not "it compiled" | ✅ SC-1 (persistence) verified by 10-restart cycle integration test; SC-2 (Teams latency) verified by integration test with timing assertion; SC-4 (traceability) verified by injecting deliberate step failures and querying the event log. |
| XI — Output restraint | No phase narration / stray dashboards | ✅ Honored. |

**Result**: PASS.

---

## Project Structure

### Documentation (this feature)

```text
specs/008-production-parity/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/DBAIAzure.Core/
├── Interfaces/
│   ├── IWorkflowRunRepository.cs         # NEW — CRUD + status-filter queries for workflow runs
│   ├── IWorkflowObserver.cs              # NEW — single write path for execution events
│   ├── IWorkflowApprovalNotifier.cs      # NEW — HITL notification for builder (≠ IHitlNotifier)
│   ├── IWorkflowReadinessRule.cs         # NEW — strategy contract for one DoR rule
│   └── IWorkflowPreRunValidator.cs       # NEW — evaluates all registered DoR rules
├── Models/
│   ├── WorkflowRunRecord.cs              # NEW — persisted run snapshot (separate from RunRecord)
│   ├── WorkflowExecutionEvent.cs         # NEW — step-level audit record (incl. LLM metadata)
│   ├── HitlPendingItem.cs               # NEW — projected view for Review Queue
│   ├── DorRuleResult.cs                  # NEW — pass/fail + human-readable reason per rule
│   └── WorkflowGenerationResult.cs       # NEW — structured LLM output for whole-workflow chat
└── Configuration/
    └── DorRuleSettings.cs                # NEW — disabled-rule names list (IOptionsMonitor)

src/DBAIAzure.Storage/
├── Entities/
│   ├── WorkflowRunEntity.cs              # NEW — EF Core entity (WorkflowRuns table)
│   └── WorkflowExecutionEventEntity.cs   # NEW — EF Core entity (WorkflowExecutionEvents table)
├── Repositories/
│   └── EfWorkflowRunRepository.cs        # NEW — IWorkflowRunRepository impl (EF Core)
└── PipelineDbContext.cs                  # EXTEND — add DbSet<WorkflowRunEntity> + DbSet<WorkflowExecutionEventEntity>; provider selection at startup

src/DBAIAzure.Web/
├── Pages/
│   ├── ReviewQueue.razor                 # NEW — paused-run list + inline approve/reject
│   ├── RunHistory.razor                  # NEW — all workflow runs, filterable
│   └── RunHistoryDetail.razor            # NEW — per-run execution event timeline
├── Services/
│   ├── SqlWorkflowObserver.cs            # NEW — IWorkflowObserver → EF Core writes
│   ├── SignalRWorkflowObserver.cs        # NEW — IWorkflowObserver → SignalR push
│   ├── AzureMonitorWorkflowObserver.cs   # NEW — IWorkflowObserver → TelemetryClient (conditional)
│   ├── WorkflowPreRunValidator.cs        # NEW — IWorkflowPreRunValidator orchestrator
│   └── WorkflowApprovalTeamsNotifier.cs  # NEW — IWorkflowApprovalNotifier → Graph API Adaptive Card
├── Rules/
│   ├── TriggerNodePresentRule.cs         # NEW — DoR: at least one trigger
│   ├── AllNodesRealizedRule.cs           # NEW — DoR: no unrealized/blocked nodes
│   ├── ConnectorsHealthyRule.cs          # NEW — DoR: all bound connectors healthy
│   └── ApprovalNodesConfiguredRule.cs    # NEW — DoR: all approval nodes have ≥1 approver
├── Hubs/
│   └── WorkflowRunHub.cs                 # NEW — SignalR hub (review queue + run status push)
├── Controllers/
│   └── TeamsWebhookController.cs         # NEW — inbound Teams action receiver (JWT validation + SubmitApproval)
└── Shared/
    └── ConnectorSettingsPanel.razor      # NEW — connector CRUD + health check UI

src/DBAIAzure.Web/Services/
└── WorkflowDesignSkillService.cs         # EXTEND — add GenerateWorkflowAsync(description)

src/DBAIAzure.Web/Shared/
└── MainLayout.razor                      # EXTEND — add Review Queue + Run History nav links

src/DBAIAzure.Processes/Pipeline/
└── WorkflowExecutionOrchestrator.cs      # EXTEND — inject IWorkflowRunRepository,
                                          #   IWorkflowApprovalNotifier, IWorkflowObserver;
                                          #   persist on every status transition;
                                          #   fire notifier on Paused transition;
                                          #   rehydrate Paused runs on startup

tests/DBAIAzure.Tests/
├── WorkflowRunRepositoryTests.cs         # NEW — in-memory EF Core; CRUD + status queries
├── WorkflowPreRunValidatorTests.cs       # NEW — each DoR rule + composite pass/fail
├── WorkflowObserverTests.cs              # NEW — SQL observer writes; SignalR observer pushes
├── TeamsApprovalNotifierTests.cs         # NEW — JWT validation reject + Graph API mock
└── WorkflowGenerationTests.cs            # NEW — mocked IStructuredCompletionService → graph

tests/DBAIAzure.E2ETests/Tests/
└── ReviewQueueTests.cs                   # NEW — Playwright: pause → notify → queue → submit → resume
```

---

## Key Risks & Decisions (carried into research.md)

1. **ApprovalTcs rehydration on restart.** When the server restarts, the in-memory `ApprovalTcs` is
   gone. V1 resume: on startup, rehydrated `Paused` runs have their `ApprovalTcs` recreated; if the
   operator submits via the Review Queue, `SubmitApproval` resolves the TCS and the background Task
   re-runs the process from the start-event — the `HumanApprovalStep` is reached again and the
   pre-resolved TCS returns the decision immediately. This is idempotent only if the steps before the
   approval gate are idempotent (true for current node types). Full process-state-blob persistence
   (SK `IProcessStateManager`) is deferred to a follow-on spec.
2. **EF Core migration on startup.** Two new tables (`WorkflowRuns`, `WorkflowExecutionEvents`) are
   added via `dotnet-ef` generated migrations. The startup raw-SQL migration pattern in `Program.cs`
   is replaced by `dbContext.Database.MigrateAsync()` for the new tables to be provider-portable.
3. **Teams JWT validation library.** `Microsoft.Bot.Connector.Authentication` NuGet package is
   added to `DBAIAzure.Web`. The webhook endpoint is a minimal API controller (`MapControllers`)
   separate from the Blazor SignalR pipeline.
4. **Observer fan-out.** `WorkflowExecutionOrchestrator` resolves `IEnumerable<IWorkflowObserver>`
   and calls each in a fire-and-forget fashion (individual failures logged, not propagated). Step
   events are emitted at step entry and exit via the `RunUpdated` event chain — no new callback from
   the SK process framework needed for V1.
