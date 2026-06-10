# Changelog — AzureWorkflowPOC

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
- Proxy step name changed from `"hitl-proxy"` to `"hitl_proxy"` — SK rejects plugin names containing hyphens

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
