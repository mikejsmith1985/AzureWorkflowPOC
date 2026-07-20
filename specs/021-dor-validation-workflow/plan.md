# Implementation Plan: Intelligent DoR Validation Workflow

**Branch**: `feature/dor-validation-workflow` | **Date**: 2026-07-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/021-dor-validation-workflow/spec.md`

## Summary

Ship a single, opinionated, **config-driven Definition-of-Ready validation workflow** as the Workflow Builder's
default starter (replacing the "Support Request Flow" example), and enhance the Automation tab so its node kinds
(AI DoR review, human conversation, Jira update/transition, SLA-aware notification) are first-class. On Jira
ticket-created, the workflow hydrates the ticket, loads a configurable DoR document, and evaluates it with the
LLM (structured output). Ready tickets auto-transition; not-ready tickets open a **durable human-in-the-loop
conversation** in Slack that re-evaluates each reply, enforces business-hours SLAs with multi-tier escalation,
and ends in either an auto-resolution (whitelisted Jira writes + transition) or a clean, audited manual handoff.

**Approach**: reuse the existing MAF execution engine and its native HITL primitives (`RequestPort` +
`CheckpointManager` + restart rehydration), `IStructuredCompletionService` for AI review, the Jira work-tracker
adapter, the Slack MCP messaging gateway, the encrypted connector-config store, and the append-only audit/event
logs. Build only the five documented gaps: **Jira read-into-payload**, **Jira status-transition**, **Slack
thread-reply capture over MCP**, a **loadable DoR-document source** (`inline`/`url` behind a seam), and **durable
SLA clocks with multi-tier escalation**. Everything behaviorally significant lives in a new `DorWorkflow`
connector-config namespace, hot-reloaded per run; a global dry-run flag gates every write.

## Technical Context

**Language/Version**: C# / .NET 8 (pinned via `global.json`, user-local SDK).

**Primary Dependencies**: Microsoft Agent Framework (`Microsoft.Agents.AI.Workflows`) — GA only; `Microsoft.Extensions.AI` (`IChatClient`, `ChatResponseFormat.ForJsonSchema`); EF Core; Blazor Server; ModelContextProtocol SDK (Slack MCP gateway). No new packages anticipated.

**Storage**: `PipelineDbContext` over SQLite (dev) / SQL Server (prod). New tables: `DorWorkflowInstances` (state machine + SLA clock + counters), reusing `WorkflowCheckpoints` (MAF), `WorkflowExecutionEvents` + cost ledger (audit), and `ConnectorConfigRecord` (config + encrypted secrets).

**Testing**: xUnit (unit, 100% mocked, <10ms), integration (real EF/SQLite, real HTTP handlers/fakes), bUnit (builder node config panels), Playwright (`scripts/run-e2e.ps1`) for the builder default + config UI.

**Target Platform**: Linux container (Azure Container Apps, scale-to-zero) + Windows dev host.

**Project Type**: Web (Blazor Server admin console) with backing MAF workflow services.

**Performance Goals**: Not latency-sensitive — SLAs are measured in hours; AI-call latency dominates a run. Reply-capture poll latency (tens of seconds) is immaterial.

**Constraints**: Durable pause/resume across service restarts (checkpointed); secrets zero-knowledge (vault by reference, Article IX); AI-editable field whitelist enforced programmatically (never trust the model); audit append-only. **Deployment note (analyze A1)**: the SLA/reply-poll `BackgroundService` does not run while an instance is scaled to zero, so SLA/escalation timers would stall until the app is next awoken. The DoR deployment therefore requires **`min-replicas ≥ 1`** (or an external scheduled nudge). Local dev is always-on and unaffected.

**Scale/Scope**: Single active DoR configuration per instance; unbounded concurrent tickets, one durable workflow instance each, isolated by run id.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Article | Assessment |
|---|---|
| I — Prime Directive (best route) | PASS — reuse GA MAF primitives over bespoke; no quick-but-dirty pause/resume. |
| II — Process Protection | PASS — no wildcard kills; stop-web.ps1 targets a specific PID. |
| III — Branching | PASS — work on `feature/dor-validation-workflow`, PR to main. |
| IV — Code Quality | PASS — self-documenting names, `Async`+`CancellationToken`, nullable honored, ≤40-line methods, XML docs. |
| V — Testing (three-layer, TDD) | PASS — Red→Green; unit mocked, integration real infra, Playwright for UI; every new file gets a test. |
| VI — Documentation | PASS — CHANGELOG updated; only the `specs/021-*` tree added, no ad-hoc status docs. |
| **VII — Framework-First (MAF)** | **PASS (gated)** — orchestration/state = MAF Workflows; HITL = `RequestPort`+`RequestInfoEvent`+`SendResponseAsync`; durable pause = `CheckpointManager`+`EfCheckpointStore`; model access = `IChatClient` + `ForJsonSchema`. The five build-gaps are documented framework gaps (no MAF primitive exists for Jira transitions, Slack reply-read, DoR-doc loading, or business-hours SLA clocks) — justification recorded in research.md D1–D5. GA/stable packages only. |
| VIII — Release | PASS — local pipeline / `dotnet publish`; no GitHub Actions. |
| IX — Secrets | PASS — all tokens (Jira, Slack, webhook, AI) referenced by name, resolved at runtime, never logged. |
| X — Verification & Proof | PASS — quickstart proves end-to-end against real Jira + Slack; tests exercise each state transition and restart-resume. |
| XI — Output Restraint | PASS — no internal phase-name narration; no scratch output committed. |

**Result: PASS.** No violations; Complexity Tracking not required.

## Project Structure

### Documentation (this feature)

```text
specs/021-dor-validation-workflow/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D10
├── data-model.md        # Phase 1 — entities + state machine + config schema
├── quickstart.md        # Phase 1 — end-to-end validation guide
├── contracts/           # Phase 1 — interface contracts
│   ├── dor-config-schema.md
│   ├── ai-prompt-contracts.md
│   ├── jira-adapter-additions.md
│   ├── slack-reply-capture.md
│   ├── dor-document-source.md
│   └── state-machine.md
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── DBAIAzure.Core/
│   ├── Models/DorWorkflow/            # DorWorkflowInstance, DorState, DorReviewResult, ConversationTurn, SlaClock
│   ├── Models/DorWorkflow/Config/     # DorWorkflowConfig (6 namespaces) + secrets record
│   └── Interfaces/                    # IDorConfigResolver, IDorDocumentSource, ISlaClock, IChatReplyReader,
│                                      #   IDorWorkflowInstanceStore  (+ additions to IWorkTrackerAdapter)
├── DBAIAzure.Connectors/
│   └── DorWorkflow/                   # DorWorkflowTester (health), business-hours SLA calculator
├── DBAIAzure.Processes/
│   ├── Pipeline/Maf/MafDorWorkflowFactory.cs      # the MAF graph (predicate edges + RequestPort HITL)
│   ├── Executors/Dor/                             # HydrateExecutor, DorReviewExecutor, PassTransitionExecutor,
│   │                                              #   GapOutreachExecutor, ReplyEvalExecutor, EscalationExecutor,
│   │                                              #   TicketUpdateExecutor, ManualExitExecutor, AuditExecutor
│   └── Pipeline/DorWorkflowOrchestrator.cs        # drive/suspend/resume + SLA integration (mirrors PipelineOrchestrator)
├── DBAIAzure.Storage/
│   ├── Entities/DorWorkflowInstanceEntity.cs      # + EF config
│   └── Repositories/EfDorWorkflowInstanceStore.cs
└── DBAIAzure.Web/
    ├── Integrations/Jira/                         # JiraWorkTrackerAdapter += TransitionAsync/ReadIssueAsync
    ├── Integrations/Messaging/SlackMcpReplyReader.cs   # thread-read over the MCP gateway
    ├── Services/Dor/                              # DorConfigResolver, DorDocumentSource (inline/url), SlaClock,
    │                                              #   DorSlaSweeperService (BackgroundService), DorRunRehydrationService
    ├── Controllers/JiraWebhookController.cs       # HMAC-validated issue_created trigger
    ├── Pages/ConnectorSettings.razor             # + DoR Workflow config card
    ├── Components/WorkflowBuilder/               # richer config panels for DoR node kinds
    └── Services/DefaultWorkflowProvider.cs        # replaces BuildExampleWorkflow() → DoR starter graph

tests/
├── DBAIAzure.Tests/Dor/              # unit: config resolver, SLA calc (business-hours), reply-eval routing,
│                                     #   whitelist enforcement, dry-run gate, state transitions, doc source
├── DBAIAzure.Tests/Dor/Integration/  # MAF graph run: pass path, fail→resolve, escalation, manual exit, restart-resume
└── DBAIAzure.E2ETests/Tests/         # builder loads DoR default; DoR config card; make-it-real realization
```

**Structure Decision**: Extend the existing 5-project solution in place (no new projects). Orchestration/executors live in `DBAIAzure.Processes` alongside the intake/phase-handler pipelines; the visual default + config UI live in `DBAIAzure.Web`; shared models/interfaces in `DBAIAzure.Core`; persistence in `DBAIAzure.Storage`. This mirrors the spec-018/019/020 layering and keeps MAF/Jira/Slack integrations behind Core interfaces.

## Complexity Tracking

> No Constitution violations — section intentionally empty.
