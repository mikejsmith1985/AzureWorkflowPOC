# Implementation Plan: Point at a Repo, Run Its App in a Throwaway Container, Monitor It

**Branch**: `feature/013-repo-app-monitoring` | **Date**: 2026-06-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/013-repo-app-monitoring/spec.md`

## Summary

Add the ability to register a target repository (by **local filesystem path**) as a monitored
"app", **build and run that repo's application inside its own throwaway, isolated container**, and
**link any saved workflow** (from the existing gallery) as the pipeline that **monitors** the
running app — with the app lifecycle, console surfaces, container build/run model, workflow→app
linking, and monitoring/close-the-loop behaviour mirroring the reference LangGraph application
(`C:\ProjectsWin\DBAI` workflow-poc). The build/run work is performed behind an `IAppExecutor` seam
with two implementations — a **simulated** executor (default; synthesizes outcomes, no container)
and a **real Docker** executor (Docker Engine API) — exactly the Sim/real split the reference uses.
Everything else is reuse of existing machinery: the saved-workflow store and gallery, the
workflow-execution + run-recording + observer + SignalR surfaces, the connector-style config and
encrypted-secret pattern, the EF Core SQLite store, and the primary navigation. See
[research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/),
[quickstart.md](./quickstart.md).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (SDK pinned via user-local `global.json`, 8.0.422)

**Primary Dependencies**: Existing — Semantic Kernel Process Framework, ASP.NET Core Blazor Server,
EF Core (SQLite), ASP.NET Core Data Protection (`ISecretProtector`), SignalR. **New** —
`Docker.DotNet` (official Docker Engine API client) for the real container executor; a hosted
`BackgroundService` for the monitoring loop. No new LLM/agent framework (the monitoring workflow
reuses the existing execution path).

**Storage**: Existing SQLite via `PipelineDbContext` + idempotent `CREATE TABLE IF NOT EXISTS`. New
tables: `MonitoredApps`, `AppMonitoringHeartbeats`, `AppRaisedIssues` (close-the-loop dedup). No
change to existing tables; reuses `WorkflowDefinitions`/`WorkflowBuilderRuns`/
`WorkflowExecutionEvents` unchanged.

**Testing**: xUnit unit tests (mocked `IAppExecutor`, `SimAppExecutor`, mocked repositories);
env-gated integration tests for `DockerAppExecutor` against a tiny fixture repo with a real Docker
engine; Playwright E2E for the Apps pages (`scripts/run-e2e.ps1`). Red → Green → Refactor.

**Target Platform**: Windows/Linux server (Kestrel), long-running Blazor Server app. Real container
mode requires a reachable Docker engine; absent that, the simulated executor runs the full flow.

**Project Type**: Web application (.NET multi-project solution).

**Performance Goals**: Not latency-sensitive. Build/run are background operations (seconds–minutes)
with a configurable hard timeout (default 900 s, mirroring the reference `RUN_TIMEOUT_SECONDS`).
Monitoring cycle interval configurable (default ~60 s). Live status pushed via SignalR.

**Constraints**: Each build and run uses a fresh, disposable container that is removed afterward
(FR-007); a clone/access token is never persisted (Article IX); captured logs are secret-redacted
(FR-009); no operation may hang — timeouts and start-failures always resolve to a recorded failure
(FR-008); the monitoring workflow runs on the **existing** execution path with no special-casing
(FR-011); config changes take effect without restart.

**Scale/Scope**: A handful of registered apps per instance; **one monitoring workflow per app** at a
time; single-node (no distributed scheduler).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Article | Gate | Status |
|---------|------|--------|
| I — Prime Directive (best route) | Reuse existing workflow/run/observer/config/secret/nav machinery; one clean `IAppExecutor` seam with Sim + Docker; mirror a proven reference design | ✅ PASS |
| II — Process Protection | No wildcard process/container kills; the Docker executor targets containers by the specific id it created (named/labelled), never by image wildcard | ✅ PASS (enforced in impl) |
| III — Branching | Work on `feature/013-repo-app-monitoring`, PR to merge; isolated worktree (separate from the concurrent `012-azure-container-deploy`) | ✅ PASS |
| IV — Code Quality | PascalCase/camelCase/`_camel`; predicate booleans; `Async`+`CancellationToken`; nullable honored; XML docs; <40-line methods; guard clauses | ✅ PASS (enforced in impl) |
| V — Testing (3-layer) | Unit (mocked executor + Sim), integration (real Docker executor, env-gated, real fixture repo), E2E (Playwright Apps pages). Red→Green | ✅ PASS |
| VI — Documentation | `CHANGELOG.md` updated in the PR; only spec-tree artifacts otherwise | ✅ PASS |
| VII — **Framework-First** | Monitoring workflow runs via existing `WorkflowExecutionOrchestrator` (no new engine/bus); records via existing run/observer/SignalR; config + secrets via existing connector/`ISecretProtector` pattern; storage via existing `PipelineDbContext`. Container orchestration is a **documented gap** → official `Docker.DotNet` SDK (no hand-rolled Docker API); monitoring loop is a standard ASP.NET `BackgroundService` | ✅ PASS — see justification |
| VIII — Release | No release in this feature | ✅ N/A |
| IX — Secrets | Optional clone/access token used only to obtain a repo, **never stored**; per-app secrets (if any) via existing `ISecretProtector`; logs redacted; never in non-secret fields or logs | ✅ PASS |
| X — Verification & Proof | Quickstart scenarios + unit/integration/E2E tests provide behavioral evidence (status transitions observed, real container build/run, monitoring run produced) | ✅ PASS |
| XI — Output & Dashboard Restraint | No scratch dashboards; only the per-feature spec tree | ✅ PASS |

**Article VII justification (recorded at the custom component)**: The only genuinely new
infrastructure is orchestrating a target repo's build/run inside a disposable container — no
Semantic Kernel or ASP.NET primitive provides this. It is built on the **official `Docker.DotNet`
Engine API client** (we do not shell out to the `docker` CLI or hand-roll the HTTP API). The
monitoring loop is a stock `BackgroundService`. The monitoring *workflow itself* is **not** a new
engine — it is an ordinary saved workflow executed through the existing
`WorkflowExecutionOrchestrator`, satisfying the framework-first gate. Close-the-loop de-duplication
reuses the reference's signature-hash approach backed by the existing SQLite store.

**No violations** → Complexity Tracking section omitted.

## Project Structure

### Documentation (this feature)

```text
specs/013-repo-app-monitoring/
├── plan.md              # This file
├── spec.md              # Feature spec (clarified)
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/           # Phase 1 (app-registry-repository, app-executor, app-monitoring, apps-ui-surface)
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── DBAIAzure.Core/
│   ├── Models/
│   │   ├── MonitoredApp.cs                # NEW — registered app (name, repo path, branch, build/run cmd, status, results, linked workflow)
│   │   ├── AppStatus.cs                   # NEW — Registered/Building/Ready/BuildFailed/Running
│   │   ├── AppBuildResult.cs              # NEW — succeeded, summary, redacted logs, at
│   │   ├── AppRunResult.cs                # NEW — outcome (Succeeded/Failed/TimedOut), summary, logs, at
│   │   ├── AppExecutionRequest.cs         # NEW — inputs handed to an executor (mirrors reference build/run env)
│   │   └── AppMonitoringHeartbeat.cs      # NEW — last cycle time/ok/error per app
│   └── Interfaces/
│       ├── IAppRegistryRepository.cs      # NEW — persist apps + status/result setters
│       ├── IAppExecutor.cs                # NEW — BuildAsync/RunAsync seam (Sim + Docker)
│       ├── IAppMonitoringService.cs       # NEW — run a monitoring cycle for a linked app
│       └── IAppHeartbeatStore.cs          # NEW — record/read monitoring heartbeats + raised-issue dedup
├── DBAIAzure.Connectors/
│   └── Apps/
│       ├── SimAppExecutor.cs              # NEW — synthesizes build/run outcomes (default/dev; mirrors reference SimAppExecutor)
│       ├── DockerAppExecutor.cs           # NEW — Docker.DotNet: build container + run container, throwaway, log capture, timeout
│       ├── BuildCommandAutoDetector.cs    # NEW — ecosystem heuristics (npm/pip/dotnet/Dockerfile)
│       └── ContainerLogRedactor.cs        # NEW — strip known secrets from captured logs
├── DBAIAzure.Processes/
│   └── Monitoring/
│       ├── AppMonitoringService.cs        # NEW — runs the linked saved workflow via WorkflowExecutionOrchestrator; close-the-loop
│       └── AppMonitoringBackgroundService.cs # NEW — hosted loop cycling enabled links, writing heartbeats
├── DBAIAzure.Storage/
│   ├── Entities/
│   │   ├── MonitoredAppRecord.cs          # NEW — EF entity (JSON for results)
│   │   ├── AppMonitoringHeartbeatRecord.cs# NEW
│   │   └── AppRaisedIssueRecord.cs        # NEW — dedup signatures
│   ├── Repositories/
│   │   ├── SqliteAppRegistryRepository.cs # NEW
│   │   └── SqliteAppHeartbeatStore.cs     # NEW
│   └── PipelineDbContext.cs               # ADD DbSets + OnModelCreating config
└── DBAIAzure.Web/
    ├── Pages/
    │   ├── Apps.razor                     # NEW (/apps) — list + register form + per-app Build/Run/Link/Remove
    │   └── AppDetail.razor                # NEW (/apps/{AppId}) — status, build/run summaries + full logs, link workflow, monitoring health
    ├── Shared/
    │   ├── MainLayout.razor               # ADD "Apps" nav link
    │   └── AppStatusBadge.razor           # NEW — status indicator (parallels ConnectorStatusBadge)
    ├── Hubs/WorkflowRunHub.cs             # REUSE for live app build/run/monitor status (or thin AppStatusHub)
    └── Program.cs                         # DI: register executors, repos, monitoring service + hosted loop; idempotent CREATE TABLE

tests/
├── DBAIAzure.Tests/
│   └── Apps/
│       ├── AppLifecycleTests.cs           # status machine: Registered→Building→Ready/BuildFailed→Running→Ready
│       ├── SimAppExecutorTests.cs         # synthesized outcomes, never hangs
│       ├── BuildCommandAutoDetectorTests.cs
│       ├── ContainerLogRedactorTests.cs   # secrets removed from logs
│       ├── AppRegistryValidationTests.cs  # duplicate name / missing path / missing run cmd
│       └── AppMonitoringServiceTests.cs    # runs linked workflow; dedup of recurring issue (close-the-loop)
│   └── Integration/
│       └── DockerAppExecutorTests.cs       # env-gated: real build+run of a fixture repo in a throwaway container
└── DBAIAzure.E2ETests/Tests/
    └── AppsPageTests.cs                    # register → build → run → link workflow, status badges, logs (sim mode)
```

**Structure Decision**: Keep the existing multi-project layout. Domain models/interfaces live in
`DBAIAzure.Core`; container execution (the "where external work happens" seam) lives under
`DBAIAzure.Connectors/Apps` alongside the other external-system clients; the monitoring loop and the
workflow-running glue live in `DBAIAzure.Processes/Monitoring` next to the existing execution code;
persistence follows the existing entity+repository+`PipelineDbContext` convention; UI follows the
existing gallery/connector-settings page patterns. **No new project** is introduced.

## Phasing (delivery order — each independently shippable)

1. **Phase A — Registry + simulated executor + Apps UI** (no Docker, no monitoring): app entity +
   repository + status machine; `SimAppExecutor`; `Apps`/`AppDetail` pages with register, list,
   Build, Run, Remove and log surfaces; nav link; live status. Delivers US1, US2 (simulated), US4.
2. **Phase B — Real Docker executor**: `DockerAppExecutor` (build container + run container, bind-mount
   the local repo, named-volume artifact, throwaway cleanup, log capture + redaction, timeout,
   start-failure handling); auto-detect build command; executor selection (Docker when available,
   else Sim). Delivers US2 (real).
3. **Phase C — Workflow monitoring link + loop**: link a saved workflow to an app; `AppMonitoringService`
   runs it via the existing orchestrator; `AppMonitoringBackgroundService` cycles enabled links;
   heartbeat + close-the-loop dedup; monitoring health surface. Delivers US3.

(`/speckit-tasks` will expand each phase into dependency-ordered, test-first tasks.)

## Complexity Tracking

No constitution violations — section intentionally empty.
