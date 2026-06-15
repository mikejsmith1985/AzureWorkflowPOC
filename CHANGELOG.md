# Changelog — AzureWorkflowPOC

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — Spec Kit Phase Handler (`specs/001-speckit-phase-handler`)
- **Second SK Process Framework pipeline** that turns Spec-Driven Development phase completions into human-approved Azure DevOps Boards work items; runs alongside the existing ticket pipeline without modifying it
- **Inbound signals** — `POST /api/webhook/speckit-phase` (phase complete) and `POST /api/webhook/speckit-approval` (decision-card callback) on `SpecKitWebhookController`, guarded by an `X-SpecKit-Secret` shared secret
- **Artifact validation** — `ReadArtifactsStep` reads `specs/NNN-feature/` files (bounded by `SpecKit:MaxArtifactBytes`/`MaxArtifactFiles`); `PhaseValidationStep` produces a schema-bound summary + flagged gaps
- **Structured LLM output** — `AnthropicChatCompletionService.GetStructuredAsync<T>` uses Anthropic forced tool-use (non-streaming) bound to a typed record, replacing free-text JSON parsing (closes constitution Article VII drift)
- **Human-in-the-loop approval** — `ApprovalExternalChannel` + `ApprovalPauseStep` pause the run via `IExternalKernelProcessMessageChannel`; `ForgeApprovalNotifier` pushes summary + gaps + portal link to the decision card; **no board write occurs before an approved decision**
- **Work item creation by phase** — Specify→Epic, Plan→one Task per planned unit (parsed from `tasks.md` when present, else `plan.md` sections via `PlanArtifactParser`), Implement→Bug; Plan/Implement linked under the feature's Epic (auto-created if missing, no orphans)
- **Non-destructive idempotent upsert** — a repeat `(feature, phase)` signal refreshes the existing work item's fields and appends a timestamped Discussion comment via `System.History`, never duplicating and never overwriting prior content (Azure DevOps revisions retain history)
- **Azure DevOps integration** — `AzureDevOpsBoardsClient` (`Microsoft.TeamFoundationServer.Client`) behind the `IBoardsClient` seam (PAT auth from configuration)
- **Persistence** — `PhaseRunRecord` + `SqlitePhaseRunRepository` (unique `(FeatureKey, Phase)` index) record outcomes and created work item ids for audit and idempotency
- Tests: 54 new xUnit tests (structured-output parsing, each step, orchestrator gate/reject/failure paths, hierarchy linking, idempotent upsert, repository); a skipped live Azure DevOps integration test
- `AzureDevOpsBoardsClient` connects to Azure DevOps **lazily** (on the first board write) instead of in its constructor — so signal intake, artifact validation, and the approval pause never require Boards connectivity (the write is gated behind approval anyway). Surfaced by a live end-to-end run, which also verified the full Specify-phase loop against the real Anthropic API up to the approval gate with no work item created (FR-006).
- `FileSystemArtifactReader` now reads the feature directory **recursively** (e.g. `contracts/`, `checklists/`) with feature-relative file names, so validation sees the whole feature rather than only top-level files (a live run had flagged `contracts/` as missing). Still bounded by the configured file-count and per-file byte caps.

### Added — LangGraph admin console parity (Phase 2)
- **Threads list** (`Index.razor`) — search by ticket ID/title, filter by status and source, source badges (Manual/SNow/Replay), paginated 20/page from SQLite; real-time refresh via `RunUpdated`
- **Run detail tabs** (`RunDetail.razor`) — four tabs: Events (existing log), State Inspector (before/after JSON per step), Live Stream (accumulated LLM tokens), Graph (Mermaid topology of current run with active step highlighted)
- **State inspector** — per-step before/after `TicketState` JSON panels; "Replay from here" button deserialises the input snapshot and starts a new run at that checkpoint (time-travel parity with LangGraph)
- **Graph tab** (`Graph.razor` + embedded in RunDetail) — Mermaid.js `flowchart LR` with color-coded nodes for entry points, HITL path, and terminal states; current step highlighted amber during live runs
- **Pipeline topology page** (`/graph`) — standalone full-page Mermaid diagram with step reference table (trigger event, output events, purpose)
- **`SourceBadge`** shared Blazor component — cyan for SNow, purple for Replay, gray for Manual
- **Mermaid.js** CDN + JS interop helpers (`window.mermaidRender`, `window.scrollToBottom`) added to `_Host.cshtml`
- **`DBAIAzure.Storage`** added to `DBAIAzure.sln` solution
- Fixed `PipelineRun._snapshotLock`: replaced .NET 9 `Lock` type with `object` for .NET 8 compatibility
- **ServiceNow webhook intake** — `POST /api/webhook/servicenow` with `X-SNow-Secret` header validation; maps SNow payload to `TicketState` with `Source="servicenow"`, `SnowNumber`, `SnowPriority`, `SnowCategory`
- **Teams HITL notifier** — `TeamsHitlNotifier` posts JSON to a Power Automate HTTP trigger URL when a run pauses for PO input; non-blocking, failure-tolerant
- **SQLite persistence** (`DBAIAzure.Storage`) — `PipelineDbContext` (EF Core 8), `SqliteRunRepository` implementing `IRunRepository`; run history and step snapshots survive server restarts
- **LLM streaming** — all 6 steps use `GetStreamingChatMessageContentsAsync`; tokens flow through `IProgressReporter.ReportToken` into `PipelineRun.TokenStream`
- **Step snapshots** — each step calls `IProgressReporter.ReportSnapshot(before, after)`; stored in-memory and persisted to SQLite `StepSnapshots` table
- **Time-travel replay** — `PipelineOrchestrator.ReplayFromSnapshot` creates a new run from a saved `TicketState`; replay runs are tagged `Source="replay"` with a timestamped ticket ID

### Added — Blazor Server web UI (`DBAIAzure.Web`)
- `DBAIAzure.Web` Blazor Server project — live pipeline dashboard, new-ticket form, and run-detail view with real-time event log
- `PipelineOrchestrator` (singleton) — manages background pipeline runs, exposes `RunUpdated` event so Blazor components re-render on progress
- `PipelineRun` — per-run state container with `ConcurrentQueue<PipelineEvent>` events and `TaskCompletionSource<string>` HITL gate
- `BoundProgressReporter` — routes step-level events from SK process steps into the run's event queue
- `IProgressReporter` interface and `ReportLevel` enum added to `DBAIAzure.Core.Models` — steps call this when registered in the kernel's DI container
- All 6 pipeline steps instrumented with `IProgressReporter` calls — null-safe, no-op when running in the console runner
- `AnthropicChatCompletionService` moved from `DBAIAzure.Runner` to `DBAIAzure.Connectors` (namespace `DBAIAzure.Connectors`) — shared by Runner and Web
- `StatusBadge` Blazor component with colour-coded status (cyan/amber/emerald/rose)
- Tests: `PipelineRunTests` (state machine, HITL unblocking), `BoundProgressReporterTests` (event routing)

### Fixed
- `IRunRepository` registered as singleton instead of scoped — `SqliteRunRepository` depends only on the singleton `IDbContextFactory` and creates its own short-lived `DbContext` per call, so scoped lifetime risked a captive-dependency error
- Proxy step name changed from `"hitl-proxy"` to `"hitl_proxy"` — SK rejects plugin names containing hyphens

### Changed — Dev tooling
- `build-web.cmd` resolves the user-local .NET 8 SDK (`%LOCALAPPDATA%\Microsoft\dotnet`) so builds work without a system-wide SDK or admin rights
- `.gitignore` now excludes the runtime SQLite database (`*.db`/`-wal`/`-shm`) and the machine-specific `global.json` SDK-resolution file

### Added
- README: architecture Mermaid diagram, Fibonacci anchor table, setup instructions, provider swap guide, and interview talking points
- HITL resume loop: `HitlExternalChannel` implements `IExternalKernelProcessMessageChannel`; receives `AwaitHuman` via a proxy step and lets the runner collect `Console.ReadLine()` before restarting the process with `HumanResponded`
- Proxy step in `IntakePipelineBuilder` (`AddProxyStep` + `EmitExternalEvent`) routes the internal `AwaitHuman` event out of the process boundary — the SK PF equivalent of LangGraph's `interrupt()`
- Runner `RunTicketAsync` loops up to 3 clarification rounds, matching `ValidationStep`'s `ClarificationRound >= 3 → Blocked` cap
- Spectre.Console output in every step: intake normalisation, DoR verdict with reasoning, Fibonacci estimate with anchor justification, gap-analysis questions, HITL pause banner, and final summary table (ticket ID, story points, Jira URL)
- `LocalKernelProcessFactory.RunToEndAsync` replaces `StartAsync` — process now blocks until all async steps complete before returning
- Model updated from deprecated `claude-3-5-sonnet-20241022` to `claude-sonnet-4-6` in `appsettings.json`

### Fixed
- Happy-path steps were silently running fire-and-forget; `RunToEndAsync` ensures the runner waits for process completion before printing results

### Previous
- Full .NET 8 solution: DBAIAzure.Core, Processes, Connectors, Runner, Tests
- SK Process Framework intake pipeline with 6 steps (IntakeStep → ValidationStep → GapAnalysisStep → HitlPauseStep → EstimationStep → ActionStep)
- Custom IChatCompletionService backed by raw Anthropic Messages API (HttpClient, no SDK dependency)
- Azure Monitor OTLP tracing via AddAzureMonitorTraceExporter — all SK calls auto-traced
- HITL suspend/resume via SK external events (HitlPauseStep + HumanResponded)
- Fibonacci estimation with anchor-based reference class forecasting (EstimationStep)
- 13 passing xUnit tests covering DoR parsing, Fibonacci clamping, and record immutability
- Forge Workflow initialized with Forge Terminal Workflow Architect
