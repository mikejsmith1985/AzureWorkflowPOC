# Changelog — AzureWorkflowPOC

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — Trigger Node, Directional Links & Node Deletion (`specs/004-workflow-trigger-links-delete`)
- **Trigger node (FR-09)** — new `WorkflowNodeType.Trigger` (value 0) added as the explicit entry point for every workflow; emerald colour scheme (`border-emerald-500`, `bg-emerald-950`, `bg-emerald-700` header); "Start here" subtitle on canvas; two plain-language config fields ("What starts this workflow?" and "What information is available at the start?"); a second Trigger is blocked at drop time with an amber toast; Trigger is always first in the palette under the new "Triggers" category; `WorkflowNode.CreateNew(Trigger, ...)` returns a node with zero input ports and one "Begin" output port; `_isTriggerMissing` state chain feeds the toolbar badge, Run button gate, and Run Output Panel advisory message
- **Structural validation (FR-09)** — new `IWorkflowValidator` / `WorkflowValidator` service registered as a singleton; enforces VAL-001 (no Trigger), VAL-002 (two+ Triggers), VAL-003 (island node) before every save; `WorkflowValidationException` carries user-displayable messages; `WorkflowBuilderService.SaveAsync` throws on validation failure; `WorkflowBuilder.razor` catches and surfaces each message as an amber canvas toast
- **Directional connection arrowheads (FR-10)** — `WorkflowEdgeModel` constructor now sets `TargetMarker = LinkMarker.NewArrow(20, 14)` so every edge displays a visible arrowhead pointing source → target
- **Mid-line directional accent & execution-flow animation (FR-10.5)** — `workflow-canvas-animations.css` added; `.workflow-edge path.edge-path` carries a `stroke-dasharray: 8 16` directional cue; `.edge-flow-active` applies a cyan `drop-shadow` and triggers the SMIL `animateMotion` travelling-dot animation; `WorkflowEdgeModel.IsAnimating` property drives the CSS class toggle when a source node goes Active
- **Input-port drag hint (FR-10.2)** — dragging a link from an input (left) port shows a 3-second directional hint banner (`input-port-hint` CSS class) explaining connections must start from output (right) ports
- **Node deletion via Delete key (FR-11)** — `KeyboardShortcutsDefaults.DeleteSelection` replaced with a custom `HandleDeleteSelected` method; pushes a reversible `UndoDeleteNodeCommand` onto the undo stack; removes the node and all attached edges; badges former neighbours that become islands
- **Node deletion via right-click context menu (FR-11.6)** — `@oncontextmenu:preventDefault` on `WorkflowNodeRenderer`; canvas-relative coordinates computed using a cached `IJSRuntime.InvokeAsync<BoundingRect>` result; context menu overlay with accessible keyboard navigation (Enter/Escape); same undo-delete path as keyboard deletion
- **Undo-delete fidelity (FR-11.4)** — `UndoDeleteNodeCommand` sealed class restores the node at its exact pre-deletion position together with all attached edges; integrates with the existing 50-depth undo/redo stack; island badge is cleared on undo
- **Palette disambiguation (US4)** — `PaletteEntry` extended with `string[] SearchTags`; search filter matches tags in addition to label/subtitle/tooltip; Trigger tags: `start trigger begin entry first`; FunctionRoute tags: `branch decide route condition smart switch if choose`; canonical tooltip text updated for all node types; `GetEntryClass` returns emerald hover for Trigger
- **14 new unit tests** — `WorkflowNodeTypeTests` (T001–T008: enum value, factory port topology, ID uniqueness) and `WorkflowValidatorTests` (T001–T006: VAL-001/002/003 + valid workflow); all passing

### Added — Visual Workflow Builder (`specs/003-visual-workflow-builder`)
- **Drag-and-drop canvas** — Z.Blazor.Diagrams 3.0.4.1 canvas at `/workflow-builder`; supports six node types (AgenticReason, HumanApproval, FunctionRoute/Transform/Notify/Data); port-direction enforcement (output→input only); snap-to-grid toggle; 50-entry undo/redo command stack
- **Node palette** — left sidebar with grouped node types, debounced search filter (<100 ms), hover tooltips (plain language, no jargon), and click-to-reveal animated detail panel with I/O example
- **Node configuration panel** — right sidebar opens on double-click; GoalPrompt field for agentic nodes; input/output label fields; amber unconfigured badge cleared on save; label mirrors goal for readability
- **Chat + code generation** — resizable chat sidebar backed by `IWorkflowCodeGenerator` (Semantic Kernel + Anthropic); streaming token display; code diff overlay (Myers algorithm); Copy and Save code buttons; "Your workflow changed — regenerate?" banner; LLM unavailability banner with `ILlmAvailabilityMonitor` 30 s polling
- **Persistence & gallery** — `WorkflowBuilderService`: upsert save with SVG thumbnail (`WorkflowThumbnailGenerator`), duplicate with "(copy)" suffix, delete with existence guard, 60 s auto-save debounce; gallery page at `/workflow-gallery` with card grid (thumbnail, node count, last-modified, delete confirmation modal)
- **Execution UI** — Run button opens `WorkflowRunInputModal` (plain-English scenario input, LLM translation); `WorkflowRunOutputPanel` shows per-node status badge (Active/Completed/Failed/Skipped/TimedOut); node animation rings on canvas (`node-active`, green/red/grey ring); `WorkflowSettingsPanel` for execution timeout (1–60 min)
- **Keyboard shortcuts** — Ctrl+S saves; Ctrl+Z/Y undo/redo (wired via WorkflowCanvas command stack)
- **WCAG AA** — all controls carry `aria-label`; `focus:outline-none` only used with a replacement border/ring indicator; all panel text meets ≥4.5:1 contrast ratio
- **New services** — `WorkflowTopologySerializer`, `LlmAvailabilityMonitor`, `WorkflowCodeGenerator` (Myers diff), `WorkflowDesignSkillService` (SK plugin), `WorkflowBuilderService`, `WorkflowThumbnailGenerator`
- **New models** — `WorkflowRunInput`
- **Test coverage** — 232 unit tests (all passing); covers canvas, undo/redo, LLM monitor, serializer, code generator, design skill service, node config panel, builder service, runtime builder, execution orchestrator, run output panel, palette tooltip quality

### Added — Pipeline Connector Configuration Modal (`specs/002-pipeline-connector-config`)
- **Connector configuration modal** accessible from a gear icon on the Threads dashboard; configures all four pipeline connectors (ServiceNow, Azure DevOps, LLM/Anthropic, Microsoft Teams) without restarting the app
- **Persisted settings** — connector non-secret configuration and encrypted credentials stored in `ConnectorConfigs` table (SQLite via EF Core); survives server restarts and is always editable
- **Encrypted secrets at rest** — ASP.NET Core Data Protection (`IDataProtectionProvider`) encrypts every secret field via `SqliteConnectorConfigRepository`; plaintext never enters the database, a log, or this codebase (FR-019, Article IX)
- **Per-connector functional tests** — each "Test Connection" button calls a genuine live check rather than a simple ping: ServiceNow reads `sys_properties`, Azure DevOps reads the project record, Anthropic sends a 5-token inference, Teams posts a labelled Adaptive Card; specific failure reasons are surfaced in the modal
- **Hot-reload** — LLM model/endpoint and all connector credentials are resolved from the DB at the start of every pipeline run (not at server start-up), so reconfiguring a connector takes effect immediately
- **Live parallel pre-flight gate** — both `PipelineOrchestrator` and `PhaseHandlerOrchestrator` run `IConnectorHealthChecker.CheckAllAsync()` (four tests in parallel) before any SK process step executes; failing connectors block the run and surface the specific diagnostic (FR-018, SC-008)
- `ConnectorStatusBadge.razor` — four-state status chip (not configured / untested / pass / fail) shown per connector in the modal header row
- `ConnectorSection.razor` — per-connector configuration panel with inline field validation and write-only secret semantics (unchanged masked field sends `null` to preserve the existing encrypted blob)
- Unit tests: `SqliteConnectorConfigRepositoryTests` (CRUD, encryption round-trip, null-secret preservation, concurrent-write uniqueness), `ConnectorHealthCheckerTests` (all-pass, single-fail, pre-flight diagnostic, exception safety), `ConnectorStatusBadgeTests` (four display-state rules)
- Integration test stubs in `tests/DBAIAzure.Tests/Integration/ConnectorFunctionalTests.cs` (skipped unless `Category=Integration` and real credentials supplied via environment variables)

### Fixed — Code-review bugs (self-review, PR #2)
- **PlanArtifactParser task flood** — `ParseTaskLines` previously created one ADO Task per checkbox line in `tasks.md` regardless of count; a mature feature's 52-task implementation backlog now correctly falls through to `plan.md` section headings (plan-level granularity) when the count exceeds `MaxPlanTasksFromTasksMd = 20`. Two new tests verify both the happy path and the fallthrough.
- **Path-traversal guard in `FileSystemArtifactReader`** — bare `StartsWith(specsRootFull)` would allow a sibling directory named `specs-evil` to pass; fixed by appending a separator so `(fullPath + sep).StartsWith(specsRoot + sep)` is the comparison.
- **Auto-created Epic not persisted** — `ResolveOrCreateEpicIdAsync` created a fallback Epic but never wrote it to the repository, causing a duplicate Epic on any subsequent Specify signal; fixed by upserting a synthetic Specify `PhaseHandlerState` immediately after the Epic write.
- **RunId mismatch on repeat signal** — a repeat `(feature, phase)` signal wrote a new run but the DB row kept the old RunId (primary key), so `GET /run/{newRunId}` returned 404; fixed by deleting the stale row and reinserting with the new RunId, carrying prior work-item ids forward so the idempotency anchor survives. New test covers resolvability by new RunId.
- **`WaitForApprovalAsync` no timeout** — background task leaked indefinitely when a reviewer never responded; fixed with a 72-hour `CancellationTokenSource` and `.WaitAsync(token)` that transitions the run to `Failed` on expiry.
- **`ValidateSecret` duplication** — identical secret-header validation logic lived independently in both webhook controllers; extracted to `WebhookSecretValidator` static helper used by both.

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
