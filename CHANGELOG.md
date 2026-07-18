# Changelog — AzureWorkflowPOC

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added — Work Tracking System config bridge (spec-020, in progress)

Bridging the connector-settings UI to the spec-018 work-tracker adapter layer so the active tracker — Azure
DevOps or **Jira** — is chosen and configured entirely in the UI, with credentials resolved per run (no
restart). Foundation landed: a generic `ConnectorType.WorkTracker` identity with a `provider` discriminator,
the `IWorkTrackerConfigResolver` that reads the active connector (provider + decrypted secret) from the store
per run, and the supporting `WorkTrackerProvider` / `JiraConnectorConfig` / `ResolvedWorkTrackerConfig` types.
Behaviour is unchanged so far (additive; the generic UI card, per-run adapter selection, Jira Test Connection,
and the one-time ADO→WorkTracker migration are the remaining increments).

## [2.0.0] - 2026-07-13

> **BREAKING CHANGE — Semantic Kernel removed.** The agent stack was modernized onto the **Microsoft
> Agent Framework (MAF) Workflows** (GA) and the experimental Semantic Kernel Process Framework
> (`1.77.0-alpha`, `SKEXP0080`) was deleted entirely (spec-019). MAF is now the only runtime — the
> `Maf:Enabled` flag is gone, the model layer moved from SK `IChatCompletionService` to the
> provider-neutral `Microsoft.Extensions.AI.IChatClient`, and the three orchestrators' constructors
> changed (no kernel factory). All `Microsoft.SemanticKernel*` packages are removed. Behaviour is
> preserved end-to-end (every pipeline and HITL surface runs, resumes, and rehydrates on MAF), but any
> code or configuration that depended on the SK types, the `Maf:Enabled` flag, or the old orchestrator
> constructors must be updated. Also ships: two-dimensional AI cost tracking (spec-017), the
> ADO/Jira work-tracker adapter (spec-018), accurate AI-usage telemetry (spec-016), and the console
> empty-state treatment (spec-014).

### Changed — MAF modernization: atomic cutover, Semantic Kernel removed (spec-019, T050/T051/T056)

The Semantic Kernel Process Framework is **gone**. This is the atomic cutover: MAF Workflows is now the only
runtime, unconditionally (the `Maf:Enabled` flag is removed). Deleted every SK Step, the
`IntakePipelineBuilder` / `PhaseHandlerPipelineBuilder` / `WorkflowRuntimeBuilder`, the HITL/approval external
channels, the two SK cost filters, `AnthropicChatCompletionService` / `HotReloadAnthropicService`, and the
`Events` / `WorkflowNodeEvents` / `WorkflowInputTranslator` helpers; `FunctionTransformStep` /
`FunctionDataStep` became pure static helpers (their mapping/description logic is reused by the MAF
executors). All `Microsoft.SemanticKernel*` package references and every `SKEXP0080` pragma/NoWarn were
stripped from `src/` and `tests/`; `SemanticKernelRemovedGateTests` is a permanent grep gate asserting zero
`Microsoft.SemanticKernel` / `SKEXP` matches across the tree (FR-003 / SC-002).

The three orchestrators are MAF-only, constructed directly with `IChatClient` (no kernel factory, no flag).
The phase-handler's board-write dependencies — previously sourced from the SK kernel container via the
interim `CompositeServiceProvider` shim — are now injected directly as an explicit `PhaseWorkItemWriterDeps`,
and `MafPhaseHandlerWorkflowFactory.Build` takes those deps as parameters. The design-time AI services
(Workflow Builder assistant, code generator, availability monitor, node realization) were migrated from SK
`IChatCompletionService` to `Microsoft.Extensions.AI.IChatClient`. The **Runner** console host was rewritten
to build the intake pipeline via `MafIntakeWorkflowFactory` and drive the `RequestPort` console HITL loop over
a `MafWorkflowSession`, with the model client from `AnthropicChatClientProvider`.

Obsolete SK-only tests were removed (they exercised deleted infrastructure covered on MAF by parity/executor
tests): the SK-vs-MAF perf benchmark, the SK-runtime node-realization test, the `AnthropicChatCompletionService`
usage/structured-output tests, the `HotReloadAnthropicService` key-resolution test, and the SK step/channel
unit tests (`ValidationStep`, `PhaseValidationStep`, `ReadArtifactsStep`, `HitlExternalChannel`,
`AdoTelemetryPreflightStep`). The remaining phase-handler/orchestrator tests were migrated to the MAF
constructors + a scripted `IChatClient`. **Cutover gate (T056): `dotnet test` green** — 634 passing, 1 skipped,
1 pre-existing unrelated failure (`ConnectorSettingsPanel`); grep gate clean. The migration is complete: every
pipeline and HITL surface runs, resumes, and rehydrates on MAF Workflows with no Semantic Kernel dependency.

### Added — MAF modernization: provider-neutral IChatClient seam (spec-019, Setup + Foundational)

First increment of the Microsoft Agent Framework migration (spec-019). Additive and behavior-neutral —
Semantic Kernel still runs the pipelines; this lays the provider-neutral model seam underneath. New
`IChatClientProvider` + `IChatClientProviderRegistry` (Core) with the default `AnthropicChatClientProvider`
reaching Claude through the official `Anthropic` SDK's `.AsIChatClient()` (GA package — not the prerelease
`Microsoft.Agents.AI.Anthropic`), a `HotReloadChatClient` that re-resolves the active provider/model per
call and rebuilds only on change, and a `ChatClientProviderRegistry` that fails loud (naming the provider)
with no silent fallback — the groundwork for bring-your-own-AI. Constitution **Article VII** was amended to
name MAF as the governing framework. Packages added: `Microsoft.Extensions.AI` 10.7.0, official `Anthropic`
12.35.1.

Second Foundational slice adds the metering seam and its test harness (still additive — not yet wired into
DI, which lands with US1). `CostCapturingChatClient : DelegatingChatClient` re-homes the two retired
Semantic Kernel cost filters (`IFunctionInvocationFilter` + `IPromptRenderFilter`) onto the model call
itself — the correct seam under MAF/M.E.AI, where token usage rides `ChatResponse.Usage` (streaming: the
final `UsageContent`) rather than a function hook. It maps `UsageDetails` onto the existing `LlmUsage` and
reports through the existing `ILlmUsageReporter`, so the cost ledger, binding key, and ingest downstream are
fed exactly as before (parity), and it logs only a prompt SHA-256 (never the text). A deterministic
`RecordedChatClient` record/replay harness (fixed token `UsageDetails` + streaming updates) lets the coming
parity tests assert framework equivalence against pinned model output instead of a live LLM. TDD: 3 new
unit tests (green); full unit suite 627 passing (one pre-existing, unrelated `ConnectorSettings` bUnit
failure). Deferred to the "wire-live" slice (coupled with US1, which migrates the SK pipeline steps):
`ChatClientStructuredCompletionService` (T011), design-time-consumer migration (T011a), the DI pipeline
composition + SK-registration retirement (T012), and the OTel repoint (T013).

US1 (orchestration → MAF Workflows) begins **parity-tests-first**. Three failing parity tests
(`Parity/{IntakePipeline,PhaseHandler,WorkflowRuntime}ParityTests`) drive the real GA runtime via a new
`Parity/MafWorkflowRunner` harness (runs a `Workflow` through `InProcessExecution` and folds the event
stream — step sequence from `ExecutorInvokedEvent`, final state from `WorkflowOutputEvent`, HITL from
`RequestInfoEvent`), with model output pinned by the `RecordedChatClient`. They assert the migrated
pipelines reproduce the SK step sequences exactly (intake ready/not-ready branches, phase-handler
approval gate, visual route port-routing). The MAF seam is scaffolded under
`Processes/Pipeline/Maf/` (`MafExecutorIds` + three workflow factories, currently throwing
`NotImplementedException`); `Microsoft.Agents.AI.Workflows` 1.13.0 added to `DBAIAzure.Processes` and the
test project. A reflection probe of the shipped 1.13.0 assembly corrected the design (research
**D1-reality**): the GA API has **no `AddSwitch`** — port-label routing is a labelled **conditional edge**
(`AddEdge(src, tgt, condition, label)`). The parity tests are tagged `[Trait("Category","US1Parity")]`
so regression runs exclude the intentional Reds; the rest of the suite stays green (627 passing).

US1 first pipeline is **green**: the ticket-intake pipeline now runs on real MAF Workflows. Five executors
(`Executors/{Intake,Validation,GapAnalysis,Estimation,Action}Executor` + a shared `ExecutorLlm`) each port
their retired SK step's exact prompt and JSON parse (parity); the graph
(`MafIntakeWorkflowFactory`) wires them with a ready/not-ready **conditional edge** on the ticket and a HITL
`RequestPort`. The intake parity test passes both branches — ready path completes, not-ready path suspends
at the request port (`RunStatus.PendingRequests`). Three more verified MAF mechanics drove the design:
executors must declare `[SendsMessage]`/`[YieldsOutput]`; routing is broadcast + conditional edges (no
`AddSwitch`); `WatchStreamAsync` completes on idle/pending/ended. Also landed the deferred **T011**
`ChatClientStructuredCompletionService` (structured output atop `IChatClient` via
`ChatResponseFormat.ForJsonSchema`), which the phase-handler validation executor will use. The parity
harness gained a 20s run-timeout so a non-terminating workflow fails loudly instead of hanging the suite.

**All three pipelines now run on MAF Workflows with parity (US1 core, T014–T021 green).** The phase-handler
(`ReadArtifacts`→`PhaseValidation`→approval `RequestPort`) and the visual runtime join intake. The visual
`MafWorkflowRuntimeFactory` translates a `WorkflowDefinition` into a `Workflow` — one executor per node
(id == node id), edges as `AddEdge`, terminal nodes via `WithOutputFrom` — and a `FunctionRoute` node picks
an output port from schema-bound model output and **directs** the run to that port's target node
(`SendMessageAsync(payload, targetNodeId)`), the GA analogue of the SK route step's port-label event (no
`AddSwitch`). `FunctionRoute` and `FunctionNotify` node executors are ported; the remaining node types
(`AgenticReason`, `FunctionTransform`, `FunctionData`, `HumanApproval`) throw a clear pending-T018 error.
Full suite: **631 passing** (+4 parity), one pre-existing unrelated `ConnectorSettings` bUnit failure. Still
additive — SK continues to run production; the orchestrator rewire (T022) and SK retirement (cutover) follow.

Orchestrator rewire begins (US1 T022, intake). `PipelineOrchestrator` gains a **flag-gated** MAF execution
path: when `Maf:Enabled` is set it builds the intake `Workflow` and runs it via `InProcessExecution`
(`MafWorkflowExecution` folds the event stream to the terminal ticket / suspension; `MafExecutorServices`
hands the run-bound progress reporter to the executors, which self-report). The flag is **off by default**,
so production still runs on Semantic Kernel — no behaviour change until the atomic cutover; HITL resume on
the MAF path is US2. `Program.cs` now registers the provider-neutral **`IChatClient`** pipeline
(provider registry → `HotReloadChatClient`, which re-resolves the LLM key/model from the DB connector per
call → `CostCapturingChatClient`, feeding the existing usage reporter). A new orchestrator test drives a
ready ticket through the MAF path end-to-end and asserts completion; the SK-path tests are unchanged. Full
suite green (+1), same pre-existing `ConnectorSettings` failure.

The phase-handler orchestrator gains the same flag-gated MAF path (read → validate → suspend at the approval
`RequestPort`; create-on-decision resume is US2), wiring the artifact reader + binding-key minter through
`MafExecutorServices`. Building this surfaced a real MAF runtime issue: **`WatchStreamAsync` does not complete
at `RunStatus.PendingRequests` when the run executes on a background thread** (as the orchestrators do via
`Task.Run`) — the intake happy path completes on `Ended` and was fine, but every *suspending* path (intake
not-ready, phase-handler approval) hung. `MafWorkflowExecution` now **breaks the event stream as soon as a
`RequestInfoEvent` arrives** rather than waiting for the stream to end, surfaces `ExecutorFailedEvent`/
`WorkflowErrorEvent` instead of masking them, and caps active execution at 3 minutes. New tests cover the
intake not-ready suspend, the phase-handler approval suspend, and a background-thread regression guard.
Visual orchestrator follows next.

**US2 begins — durable HITL, intake resume (T024).** The intake clarification loop now works end-to-end on
MAF: a not-ready ticket suspends at the HITL `RequestPort`, the PO's answer is applied to the ticket and
sent back via `SendResponseAsync`, validation re-runs, and (once ready) the run completes. `MafWorkflowExecution`
was refactored into a `MafWorkflowSession` that keeps the `StreamingRun` alive across suspensions so the
orchestrator can drive segment→respond→segment; the intake port became `RequestPort<TicketState,TicketState>`
with a `hitl→validation` loop edge (`ValidationExecutor`'s existing max-round rule ends the loop). A subtle
routing bug — the ready/not-ready conditional edge keys on whether the ticket carries clarifying questions, so
the answered ticket must clear them — was fixed. New test drives suspend→answer→complete. This is in-process
resume; durable checkpoint/restore across restart (T030–T032) and the SK-paused-run migration (T033) follow.

**Durable checkpointing (T030 + T025 core).** `EfCheckpointStore : JsonCheckpointStore` persists MAF
workflow checkpoints to the pipeline database (new `WorkflowCheckpoints` table, keyed by session + id and
parent-linked); the manager is built with `CheckpointManager.CreateJson(store, options)` and passed to
`RunStreamingAsync`/`ResumeStreamingAsync`. Proven end-to-end: a phase-handler run paused at its approval
gate is resumed from its checkpoint by a **brand-new store and workflow instance over the same database**,
re-emitting the outstanding request — the restart-recovery mechanic (SC-003). Because the executors are
stateless (state flows in messages), the framework's automatic checkpoint captures the run without custom
`OnCheckpointingAsync`. Index ordering is done client-side (SQLite cannot `ORDER BY` a `DateTimeOffset`).
Still off-production; the startup rehydration wiring (T032) and SK-paused-run migration (T033) follow.

**Checkpointing wired live (T032).** `MafWorkflowSession` now threads a `CheckpointManager` — when one is
supplied the run is checkpointed at every super-step — and exposes `ResumeAsync(workflow, checkpoint, manager)`
as the restart-recovery entry point. Both migrated orchestrators accept and pass the manager, and `Program.cs`
registers `EfCheckpointStore` + `CheckpointManager.CreateJson(...)`. A test drives a not-ready ticket through
the intake orchestrator (with checkpointing) to its clarification suspension and asserts durable checkpoints
were persisted for the run's session — so a paused run is now recoverable. The startup hosted-service that
iterates paused runs and resumes them on boot is the remaining piece (the visual `WorkflowRunRehydrationService`
waits on the visual-orchestrator migration). Still `Maf:Enabled`-gated / off-production.

**SK-paused-run migration (T033, core).** `SkPausedRunMigration` idempotently converts a run paused under the
retired SK framework into a durable MAF checkpoint so it resumes in place at cutover (FR-006a). Because MAF
checkpoints are opaque, it reconstructs one by running a **resume-seed workflow** — `BuildResumeWorkflow` +
`IntakeResumeSeedExecutor` forward the already-paused ticket straight to the HITL `RequestPort` (no
re-normalising, re-validating, or re-asking — **no model call**), and running that with checkpointing writes a
checkpoint at the same suspension. Re-running the migration skips already-converted runs. A test proves SC-009
for the intake surface: convert → idempotent skip → resume from the checkpoint → recover the outstanding
clarification request → answer → complete through the real validation loop. Remaining: the deploy-time startup
hook that enumerates SK-paused records, and the phase-handler resume-seed (needs its create-on-approval downstream).

**Boot-resume for intake (T032 complete).** `PipelineOrchestrator.RehydratePausedRun(pausedTicket, checkpoint)`
rebuilds a run in memory, resumes its MAF workflow from the checkpoint (found via
`EfCheckpointStore.GetLatestCheckpointAsync`, now ordered by a monotonic per-session `Sequence` column —
`CreatedAt` ties were selecting the wrong checkpoint), and re-enters the clarification loop so a PO answer
submitted *after* a restart drives it to completion. `BootResumeTests` proves it end-to-end: one orchestrator
runs to the clarification gate and "crashes", a **fresh orchestrator with empty memory** rehydrates the run
from its checkpoint over the same database and completes it on the answer. Fixed an arming race — pre-setting
`AwaitingHuman` before the drive loop armed the HITL gate let an answer be lost.

The **hosted startup service** now wires this for production: `PausedRunRehydrationService` (registered,
`Maf:Enabled`-gated) runs once at boot, enumerates persisted awaiting-human intake runs, loads each run's
ticket and latest checkpoint, and calls `RehydratePausedRun` — a run without a checkpoint is left to the
one-time SK migration. A test drives the whole boot path: one orchestrator runs to the clarification gate and
"crashes", then the service — over the same database with a fresh orchestrator — rehydrates the persisted run
and completes it on the answer. (Tests use a shared-cache named in-memory SQLite database so the orchestrators'
concurrent background tasks each open their own connection.) Phase-handler/visual rehydration remain.

**US3 metering / structured-output parity (T035/T036/T039).** With the seam already built (T010/T011), this
proves equivalence to the pre-migration build. Cost parity: `CostParityTests` pins a response's token usage
and asserts `CostCapturingChatClient` captures the same input/output/cache tokens and model, so
`ModelPricing.EstimateCostUsd` returns an identical estimate (0% delta, SC-004). Structured-output parity:
`ChatClientStructuredCompletionService` binds `RouteDecision` and `PhaseValidationResult` to identical typed
records off the SK forced-tool path (FR-011). SC-005 gate: `ExecutionPathSkFreeTests` reflects over the MAF
executor / workflow-factory namespaces and asserts none takes a Semantic Kernel chat-completion dependency,
while the model-using executors inject `IChatClient` — so the MAF execution path is provably SK-free even
though SK still backs the pre-cutover production default. (Streaming *cost capture* from the final
`UsageContent` update was already built and tested in `CostCapturingChatClient`; the Run Detail Stream tab UI
(T037/T038) is the remaining US3 piece.)

**US6 bring-your-own-AI (T040–T044).** The provider seam is now genuinely multi-provider. A second built-in
provider — `OpenAiChatClientProvider` — reaches any OpenAI-compatible endpoint (OpenAI, Azure OpenAI, Ollama,
LM Studio via `AiProviderConfig.Endpoint`) through the GA `Microsoft.Extensions.AI.OpenAI` package (a stable
release, not prerelease — FR-003), exposed as an `IChatClient`. Both providers are registered; `Program.cs`
selects the active one by `AI:Provider` (default `anthropic`, per instance), reading a non-default provider's
key/model/endpoint from `AI:<Provider>:*` (secret by reference). Tests prove the seam: each provider resolves
by config id; an unknown provider throws the named `AiProviderException` with no silent fallback; and swapping
the active provider runs the intake flow with an **identical executor sequence** — zero change to any pipeline
or executor (SC-008). Claude remains the default, so nothing else is required to run out of the box.

**US4 MCP tool delivery (T045–T047).** The finding here is that no migration was required: `McpMessageGateway`
and `MessageDelivery` (in `DBAIAzure.Connectors/Messaging`) were **never** Semantic-Kernel-coupled — they call
the MCP send-message tool via the official MCP SDK (`ModelContextProtocol.Core`) and fall back to a platform
webhook, all framework-neutral. Delivery is a deterministic tool call, not an LLM-driven one, so there is
nothing to re-express onto the MAF/IChatClient tool model. A new test verifies the end-to-end path from a MAF
workflow: a MAF intake run suspends at its clarification gate and its human-in-the-loop notification reaches
the MCP tool through the production chain (`MessagingHitlNotifier` → `MessageDelivery` → `IMcpMessageGateway`),
recorded by the fake gateway — MCP-backed delivery works from a MAF pipeline exactly as before.

**US5 observability — traces to Azure Monitor (T013/T048/T049).** The Web `IChatClient` pipeline is now
wrapped in `OpenTelemetryChatClient` under the `AiTelemetrySourceNames.ChatClient` source, so every model call
emits gen_ai OpenTelemetry spans (tokens, latency, model) in place of the retired SK telemetry filters. The
Runner's tracer provider registers the MAF/M.E.AI sources (`ChatClient` + `Agents`) **alongside** the legacy
`Microsoft.SemanticKernel*` source — both flow to Azure Monitor during the migration so there is no trace gap;
the SK source is removed at the atomic cutover, and the Azure Monitor exporter is unchanged. A validation test
uses an `ActivityListener` to confirm a model call emits an `Activity` from the registered source.

**Visual orchestrator on MAF (T022 complete).** The third and last pipeline — the visual workflow builder's
`WorkflowExecutionOrchestrator` — now runs on MAF Workflows behind `Maf:Enabled`. A persisted
`WorkflowDefinition` is translated to a live MAF `Workflow` (`MafWorkflowRuntimeFactory`: Trigger →
pass-through transform, Agentic/Route/Transform/Notify/Data → executors, HumanApproval → `RequestPort`) and
driven to completion via `MafWorkflowSession`, with suspension mapped to `Paused`. The model client, flag,
connector repository, and checkpoint manager are wired through DI in `Program.cs`. A test runs a
Trigger→Agentic→Notify workflow end-to-end on MAF to `Completed`. **All three pipelines (intake,
phase-handler, visual) now execute on MAF**; SK stays the default until atomic cutover.

**US3 streaming UI — Run Detail Stream tab (T037/T038).** The MAF intake LLM executors now stream their model
output. `ExecutorLlm.CompleteStreamingAsync` calls `IChatClient.GetStreamingResponseAsync` and forwards each
text chunk to the run-bound `IProgressReporter.ReportToken`, which feeds `PipelineRun.TokenStream` — the exact
source the Run Detail **Stream** tab already renders. The four steps that streamed under SK
(Intake/Validation/GapAnalysis/Estimation) stream on MAF; Route/Agentic stay non-streaming (the SK steps did
not report tokens to the UI). Cost capture is unaffected — `CostCapturingChatClient.GetStreamingResponseAsync`
still reads usage from the final `UsageContent` update (0% delta preserved under streaming). Two tests cover
it: an orchestrator-level proof that a MAF run enqueues streamed tokens via the streaming request path, and a
`RunDetailStreamTabTests` bUnit render asserting the Stream tab shows the tokens grouped by step. This closes
US3.

**Phase-handler approval resume on MAF (US2 T024/T027 core).** The phase-handler pipeline now completes a run
end-to-end on MAF: read → validate → suspend at the approval `RequestPort` → resume on the reviewer's decision
→ write the board. To make the reviewer-decided state (not just the bare decision) reach the create step, the
approval port is now `RequestPort<PhaseHandlerState, PhaseHandlerState>` (mirroring the intake HITL port) with
an `approval → createWorkItem` edge. The 340-line board-write logic was extracted verbatim from the SK
`CreateWorkItemStep` into a framework-neutral `PhaseWorkItemWriter` (dependencies passed as an explicit
`PhaseWorkItemWriterDeps` struct instead of `kernel.Services`), and both the SK step and the new MAF
`CreateWorkItemExecutor` now delegate to it — so both frameworks produce identical board state (FR-015). The
orchestrator's MAF path drives a `MafWorkflowSession<PhaseHandlerState>` across the suspension, reusing the
run's existing approval gate (`WaitForApprovalAsync`/`SubmitApproval`) and the 72-hour timeout, and sources the
create executor's board-write dependencies from the same SK kernel container the SK path uses (single DI source
of truth; the MAF executors never touch the kernel's SK chat service, so the path stays SK-free). No board
write occurs before an approved decision (FR-006). `PhaseHandlerMafResumeTests` proves approve→create-Epic and
reject→no-write; the 5 existing SK `CreateWorkItemStepTests` still pass unchanged, confirming the extraction
preserved behaviour. Remaining: the visual approval resume and phase-handler rehydration on restart.

**Visual approval resume on MAF (US2 T024/T027).** The visual workflow builder's HumanApproval gate now
suspends and resumes on MAF: `WorkflowExecutionOrchestrator.AwaitApprovalAndResumeAsync` parks the run as
`Paused` at the approval `RequestPort`, arms the run's existing `ApprovalTcs` and the 24-hour auto-reject
watchdog, and on `SubmitApproval` responds to the port with the decided `WorkflowStepData` (`IsApproved` set)
and drives the session to `Completed` — recursing if a second approval gate lies downstream. Parity with the
SK gate, which continues past the gate carrying the decision whatever it is (the port, like the SK builder's
edge, forwards unconditionally). `VisualOrchestratorApprovalResumeTests` proves pause→approve→complete and
pause→reject→complete. **With this, all three HITL surfaces (intake clarification, phase-handler approval,
visual approval) suspend and resume on MAF Workflows.** Remaining: phase-handler/visual rehydration across an
app restart, and the polish/atomic-cutover tasks.

**Phase-handler rehydration across restart (US2 T032).** A phase-handler run paused at the approval gate now
survives an application restart on MAF. `PhaseHandlerOrchestrator.RehydratePausedRun(placeholder, checkpoint)`
resumes the run from its checkpoint and drives it so a reviewer's decision submitted after the restart still
writes the board; the fresh-start and rehydration paths share a single `DriveApprovalSessionAsync` that reads
the paused state from the checkpoint's re-emitted request rather than local memory (which is empty after a
restart). `IPhaseRunRepository.ListByStatusAsync` was added so the startup `PausedRunRehydrationService` can
enumerate `AwaitingApproval` phase runs alongside the intake ones. Building this surfaced and fixed a real
arming race: the rehydrated run was seeded at `AwaitingApproval`, so a reviewer callback could observe the
status and submit *before* the resumed run armed its gate (`ProvideApproval` requires `_hasPaused`), dropping
the decision — the run is now seeded at a non-awaiting status and only advertises `AwaitingApproval` after the
gate is armed. Two tests prove it: `PhaseHandlerBootResumeTests` (orchestrator-level restart→approve→create)
and `PausedRunRehydrationServiceTests.StartupService_RehydratesPausedPhaseRun_ThatCreatesOnApprove` (the full
boot-service path).

**Visual rehydration across restart (US2 T032).** The visual builder's HumanApproval gate now survives a
restart too. `WorkflowExecutionOrchestrator.RehydratePausedRun(record, definition, checkpoint)` truly resumes
the MAF session from its checkpoint (`ResumeAsync` → the shared `AwaitApprovalAndResumeAsync`) so approving a
rehydrated run drives the workflow to completion — previously it only reconstituted the in-memory approval
TCS with no live session, so an approval after restart did nothing. A new owner-less
`IWorkflowRepository.GetByIdAsync` lets the boot-time `WorkflowRunRehydrationService` reload a paused run's
definition (the run record carries no owner) and its latest checkpoint and call the resume overload (falling
back to reconstitute-only when either is missing). The visual orchestrator is now registered as a concrete
singleton (forwarded to the interface) so the service can reach the checkpoint-resume overload. The rehydrated
run is seeded `Running` (not `Paused`) so the drive loop — which arms the gate before advertising `Paused` — is
authoritative. `VisualBootResumeTests` proves restart → rehydrate → approve → complete. **With this, all three
HITL surfaces (intake clarification, phase-handler approval, visual approval) suspend, resume, and rehydrate
across an application restart on MAF Workflows.**

**Performance-budget check (T055 / SC-010).** `FrameworkPerfBaselineTests` drives the intake pipeline through
both the SK Process Framework and MAF Workflows with an identical instant scripted model on both paths — so
the measured delta is pure framework/orchestration overhead, the model's own latency (identical on both)
factored out — over 40 interleaved timed runs after warmup, each framework driven directly to completion (no
fire-and-forget polling artifact). Result: **MAF median 1.94 ms/run vs SK 10.48 ms/run — a −81.4% change
(~5.4× faster), far inside the ≤10% budget**; per-model-call overhead 0.65 ms (MAF) vs 3.49 ms (SK). MAF
Workflows is materially lighter than the SK Process Framework, so the performance gate does not block the
atomic cutover. (Real end-to-end latency is dominated by the live model call, which is unchanged; this
isolates the framework layer. Tagged `[Perf]`.)

**Polish — factory decomposition (T052).** `MafWorkflowRuntimeFactory.Build` (70 lines) was decomposed into
`CreateBindings` / `DeclareTerminalOutputs` / `RecordRoutePortLabels` helpers so the orchestration method reads
in ~20 lines (Article IV). Two other long methods were reviewed and intentionally left: the intake executors'
`HandleAsync` length is dominated by the model-prompt *string literal* (data, not branching), and
`CostCapturingChatClient.GetStreamingResponseAsync` needs the manual-enumerator pattern (a `yield` cannot sit
inside the try/catch that meters a mid-stream failure) — decomposing either would reduce, not improve,
readability. Parity/visual tests stay green.

**Interop-shim inventory + cutover conditions (T053 / FR-016 / SC-007).** The migration is deliberately
additive: SK still runs production behind `Maf:Enabled` (default **off**). The temporary SK↔MAF shims and the
condition that retires each at the **atomic cutover** (T050/T056):

| Shim | Where | Removal condition |
|---|---|---|
| `Maf:Enabled` flag-gated dual path | all three orchestrators (`PipelineOrchestrator`, `PhaseHandlerOrchestrator`, `WorkflowExecutionOrchestrator`) | Flip the default to on, validate, then delete the SK branch in each orchestrator. |
| SK `Kernel` built to source board-write deps | `PhaseHandlerOrchestrator.ExecuteViaMafAsync` (via `CompositeServiceProvider` over `kernel.Services`) | Register the work-tracker/cost/telemetry deps in a MAF-native service bag (or app DI) and drop the `_kernelFactory` build on the MAF path. |
| Dual OTel sources | `DBAIAzure.Runner/Program.cs` registers `Microsoft.SemanticKernel*` **and** MAF/M.E.AI sources | Remove the `Microsoft.SemanticKernel*` source once no SK code emits spans. |
| SK Steps / `*Builder` / SK orchestrator branches retained | `src/DBAIAzure.Processes/Steps/*`, `*PipelineBuilder`, `WorkflowRuntimeBuilder` | T050 deletes them (and all `Microsoft.SemanticKernel*` packages + `SKEXP0080` pragmas) once MAF is the default and green. |

Not a shim (permanent): `PhaseWorkItemWriter` is the framework-neutral home for the board write; the SK
`CreateWorkItemStep` delegates to it today and is simply deleted at cutover while the writer stays. The MAF
execution path is already provably SK-free (`ExecutionPathSkFreeTests`, SC-005). **Cutover gates still open:**
phase-handler/visual rehydration across restart, the performance-budget baseline (T055, ≤10%), the T050 grep
gate (zero `Microsoft.SemanticKernel*` / `SKEXP0080`), and a fully green `dotnet test` + E2E (T056).

### Added — Consistent empty-state treatment across the console (spec-014 T036 / FR-022)

New shared `Shared/EmptyState.razor` component gives every empty list and panel the same friendly
"nothing here yet" treatment — an icon, a short heading, an optional supporting sentence, and an
optional call-to-action — instead of a bare one-line message or blank region. An `IsCompact` mode
tightens padding for inline panels. Applied to the Dashboard thread list, Workflow Gallery, Apps,
Review Queue, Run History, Run Detail (state + stream tabs), App Detail monitoring, and the Workflow
Builder realization panel (which also retired its last raw `text-gray-500` utility in favour of
tokens). Component behaviour is bUnit-tested. Closes the final open spec-014 task.

### Added — Spec-018 close-out: tracker-switch test + rollup/setup docs

Closes the multi work-tracker feature. Added a tracker-switch data-preservation test (FR-011): the cost
ledger + binding map are tracker-neutral, so changing `WorkTracker:Active` leaves existing rows intact and
resolvable. Documented rollup per tracker (`docs/work-tracker-rollup.md` — ADO Analytics native vs Jira
Advanced Roadmaps add-on) and Jira setup (`docs/jira-setup.md`). Spec-018 is feature-complete across PRs
#48–#52; the only open items are external-dependency follow-ups (Jira token-snapshot path; a live Jira
round-trip needing a real Jira Cloud site).

### Added — Jira field provisioning + tracker-neutral startup provisioning (spec-018 increment 3b)

`JiraFieldProvisioner` makes the telemetry/cost fields usable on Jira — idempotently find-or-create each
custom field by name, then ensure it has a (global) context so its value is writable on any project/issue
type via the REST API (screen association, for UI visibility only, is intentionally out of scope). Wired
into `JiraWorkTrackerAdapter.ProvisionFieldsAsync`. The startup field-provisioning hook now routes through
the **active** work-tracker adapter (`provider.GetAdapter().ProvisionFieldsAsync`) instead of calling the
ADO preflight directly — so ADO runs its preflight (incl. inherited-process handling) and Jira runs its
field/context provisioner, depending on `WorkTracker:Active`. Unit-tested (create + idempotent no-op);
full suite green (1 pre-existing failure).

### Added — Jira work-tracker adapter (spec-018 increment 3a)

A second `IWorkTrackerAdapter` implementation — `JiraWorkTrackerAdapter` (Jira Cloud REST v3): creates
issues, sets custom fields (resolved logical→`customfield_*` by name, cached), appends comments, upserts,
and resolves binding keys via the shared local map. Work items are referenced by issue key (`PROJ-123`).
Registered alongside the ADO adapter; `WorkTrackerAdapterProvider` selects the active one by the
`WorkTracker:Active` setting (default `AzureDevOps`). `GetRollupCapability` reports `RequiresAddOn`
("Jira Advanced Roadmaps") — honestly surfacing that Jira lacks native hierarchical rollup. Field
provisioning (field + context + screen) and the dev-token snapshot for Jira are follow-ups. Unit-tested
via a fake REST handler; full suite green (1 pre-existing failure).

### Changed — Pipeline creation now goes through the work-tracker adapter (spec-018 increment 2b)

`CreateWorkItemStep` creates/upserts work items via `IWorkTrackerAdapter` instead of `IBoardsClient`
directly, so the core pipeline no longer assumes Azure DevOps. `CreatedWorkItemRef.WorkItemId` is now a
tracker-neutral `WorkItemRef` (numeric for ADO, string-key for Jira); the adapter is injected into the
per-run phase kernel. `IBoardsClient` is retained as the ADO adapter's internal seam, so ADO behaviour is
unchanged (the binding stamp + cost projection route through the adapter, which re-prefixes logical field
names to `Custom.*`). The per-item token snapshot (`TelemetryWriteBackService`) still uses the numeric id
and is skipped for non-numeric refs — a Jira-snapshot follow-up. Full suite green (1 pre-existing failure).

### Added — Epic and Bug telemetry + cost fields (ADO telemetry config)

Extended `default-telemetry-config.json` so the run anchors — **Epic** (the Plan anchor) and **Bug** (the
Implement anchor) — carry the full telemetry + cost field set alongside User Story. Without this the cost
projection wrote `Custom.AIRuntimeCostUSD`/`Custom.AIDevCostUSD` (and the runtime ledger) to an Epic/Bug
that did not have the field. Added `Epic`/`Bug` work-item-type reference-name mappings to the preflight.
Verified live against the real Agile-inherited project: all fields now attach to **User Story, Task, Epic,
and Bug**. Re-run the preflight after deploy to provision them.

### Fixed — ADO telemetry preflight never attached fields on Agile / inherited processes

The field preflight created the custom telemetry + cost fields at org level but never attached them to
any work item type on an Agile (or any inherited) process, so they never appeared on work items and the
telemetry write-back / cost projection silently failed. Three compounding root causes, found by live
verification against a real Agile-inherited project:

- **Swapped process-template GUIDs** — the `Agile`/`Scrum` template-type GUID constants were reversed, so
  an Agile process (and an Agile-inherited process, whose `parentProcessTypeId` is the Agile GUID) was
  detected as **Scrum** and fields were attached to `ProductBacklogItem`, a Scrum-only WIT (404 NotFound).
  The test fixtures encoded the same swap, which masked it.
- **Process-id lookup matched the wrong field** — `_apis/process/processes` returns the GUID in `id` with
  `typeId` empty, but the lookup matched `typeId`, so the attach targeted `"unknown-process-id"`.
- **Inherited system WITs were not materialized** — a system work item type in an inherited process must
  be materialized as an inherited override before fields can be added (else `VS402805`). The preflight now
  materializes it and attaches to the resulting reference name. Existing fields are also (re)attached, not
  just newly-created ones.

Verified live: all telemetry + cost fields now attach to **User Story** and **Task** on the real project.

### Added — Two-dimensional AI cost tracking on the work hierarchy (spec-017)

Tracks AI spend as two dimensions — **runtime** (pipeline model calls) and **development** (coding-agent
sessions) — joined by a pipeline-minted, DoR-enforced source-neutral **binding key** and rolled up the
ADO work tree via **ADO Analytics** (no rollup engine — Framework-First).

- **Cost ledger** (`ICostLedger`/`SqlCostLedger`): append-only, dimension-split; per-ticket totals are
  cumulative sums (FR-007) and a multi-item run contributes once on its **anchor** (the Epic for a Plan
  run), never duplicated (FR-008).
- **Binding key** (`IBindingKeyMinter`): minted at intake, asserted at DoR (`PhaseValidationStep`),
  written to the ADO work item (`Custom.CostBindingKey`), resolved locally via `IBindingWorkItemMap`.
- **Runtime**: `CreateWorkItemStep` appends one runtime entry on the anchor; `CostProjectionService`
  projects cumulative `Custom.AIRuntimeCostUSD`/`Custom.AIDevCostUSD` for Analytics.
- **Development**: secret-gated `POST /api/telemetry/dev-usage` appends Development entries by binding
  key (re-priced via `ModelPricing`); unresolvable keys are recorded **unattributed**, never dropped (FR-010).
- Docs: `docs/ado-cost-analytics.md` (rollup view) + `docs/dev-agent-telemetry-setup.md` (org runbook).
- **Deferred (T037):** ServiceNow write-back of the binding key — the SNow integration is intake-only;
  the key lives on the ADO item + local map, which is sufficient for resolution.
- All capture is best-effort — a cost/telemetry failure never disrupts a run, validation, board write,
  or developer session (FR-011). Re-run the ADO field preflight to provision the new fields.

### Added — Triggering-user attribution on AI telemetry

The telemetry write-back now records **who triggered the run** on the work item's new
`Custom.AITriggeredBy` field, so a run's AI usage is attributable to a person, not just a RunId.
The phase signal carries an optional `triggered_by` (self-asserted by the secret-gated caller); when
absent, the approver (`decided_by`) is used as the fallback. The field is created by the preflight
(Bootstrap) or mapped to `System.Tags` (Adaptive). No authentication is added — attribution is
best-available identity within trusted infra. Re-run the ADO field preflight to provision the new field.

### Added — Accurate AI usage telemetry capture (spec-016)

The Anthropic connector discarded the response `usage` block, so the AI telemetry fields written to
ADO work items (#42) were silently empty. Capture is now real and covers both the workflow runner and
the phase-handler validation:

- The connector parses `usage` (input/output + `cache_read_input_tokens`/`cache_creation_input_tokens`)
  and the model, and reports every call (success or failure) through a new `ILlmUsageReporter` seam —
  the single capture point for both paths (SK function-invocation filters can't observe the direct
  `GetStructuredAsync` call). Run correlation flows via `LlmRunContext` (an ambient run id now set by
  both orchestrators — it was previously never assigned).
- New per-call event fields (`LlmCacheReadTokens`/`LlmCacheCreationTokens`) are persisted; the run
  aggregate gains cache sums, an AI **error count**, and a derived **cache-hit rate**.
- Write-back adds `Custom.AICacheTokens`, `Custom.AICacheHitRatePct`, and `Custom.AIAPIErrors`; the cost
  estimate now includes cache read/write contributions. Tool-accept rate remains out of scope (no
  capture source). All capture is best-effort — a telemetry failure never disrupts the run.

### Added — ADO telemetry write-back (LLM metrics → work item fields)

Closes the second half of the ADO telemetry feature: spec-009 *created* the custom fields; this
writes a run's captured AI telemetry into them. After the phase handler creates/upserts a work item,
`TelemetryWriteBackService` aggregates the run's `WorkflowExecutionEvents`, reads the preflight
manifest, and patches the work item — using the created **custom fields** (Bootstrap mode) or the
**native fallbacks** (Adaptive mode; string/picklist fields fold into a non-destructive `System.Tags`
entry, integers into Story Points).

- New: `IBoardsClient.UpdateFieldsAsync` (arbitrary-field patch with non-destructive tag merge),
  `IRunTelemetrySource` + `SqlRunTelemetrySource`, `IAdoTelemetryManifestReader`, `ITelemetryWriteBack`,
  `RunTelemetryAggregate`, and `ModelPricing` (estimated USD cost).
- Write-back is **best-effort** — a telemetry failure never undoes an approved board write.
- **Scope/limits:** only metrics the pipeline captures today are written — session id, model, input/
  output tokens, LLM call count, duration, and estimated cost. Cache tokens, tool-accept rate, API
  errors, and cache-hit rate have no capture source yet and are omitted (never fabricated). Fields are
  applied per the configured work item types (UserStory/Task); Epic/Bug have no configured telemetry
  fields until the config is extended.

## [1.6.0] - 2026-06-29

> The entries below shipped in **v1.6.0 or earlier** (tags `v1.2.x`–`v1.6.0`) but were never split out
> of `[Unreleased]` at release time. They are grouped here for accuracy; the git tags hold the precise
> per-release boundaries.

### Changed — Apps E2E test cleans up after itself

The `Register_ValidPath_AppearsInList` Playwright test now removes the app it registers (the
E2E and dev runs share a SQLite database, so registered apps previously persisted as orphan
`e2e-app-*` entries in Monitored Apps). Removal runs in a `finally` block so it never masks a
real test failure.

### Fixed — Messaging connector's default MCP argument template is now platform-aware (Slack)

A blank **MCP Argument Template** on a Slack connector previously produced `{"target":…,"text":…}`, whose
keys Slack's `slack_send_message` tool ignores — so the call failed with `no_text` even though auth, the
tool name, and the channel were all correct. The default is now resolved per platform: Slack gets its
verified `{"channel_id":"{{target}}","message":"{{message}}"}`, while platforms without a verified tool
schema keep the generic template (which operators override per tool).

- `McpArgumentTemplate.DefaultFor(MessagingPlatform)` returns the platform-specific default; `MessageDelivery`
  applies it when the operator leaves the template blank.
- The connector form's placeholder now shows the Slack-correct example.

### Added — Slack MCP token helper

- `scripts/mint-slack-mcp-token.ps1` mints a Slack **user** OAuth token (`xoxp-`) for the Messaging
  connector's MCP path. Slack's hosted MCP server (`mcp.slack.com`) requires a user token and does not
  support automatic (DCR) OAuth, so the script runs the manual authorization-code flow once — opens the
  consent screen, captures the redirect `code` on a loopback listener, exchanges it via
  `oauth.v2.access`, and prints the token to paste into the connector's MCP Auth Token field. The client
  secret is read from `$env:SLACK_CLIENT_SECRET` and never logged.

### Fixed — ADO telemetry preflight no longer emits a cryptic JSON error on bad credentials (spec-009)

The startup ADO telemetry preflight surfaced `ADO process detection failed: '<' is an invalid start of a
value` whenever Azure DevOps returned its **HTTP 200 HTML sign-in page** — which it does (instead of a
401) when the Personal Access Token is invalid/expired or the organization URL is wrong. The
`IsSuccessStatusCode` guard passed and `JsonDocument.Parse` then choked on the leading `<`.

- `AdoTelemetryPreflightService.DetectProcessTypeAsync` now checks the response is actually JSON (via a
  new `LooksLikeJson` content-type / leading-character guard) before parsing, and returns an actionable
  message — *"the Personal Access Token is likely invalid or expired, or the organization URL is
  wrong. Update them in Connector Settings."* — instead of the raw parser exception.
- The preflight remains fire-and-forget and non-fatal; the web app still starts normally.

### Fixed — ACA deploy script now runs against the real subscription (spec-012)

`deploy/aca/deploy.ps1` had never been executed end-to-end and failed on three real blockers; all are
now resolved so `./deploy/aca/deploy.ps1` produces a live public URL:

- **PowerShell parse error**: a native-command redirection (`2>$null`) inside a grouping `(...)`
  expression is a syntax error — split into a bare command + a separate `$LASTEXITCODE` check.
- **Server-side ACR build is disabled on this subscription** (`TasksOperationsNotAllowed`): replaced
  `az acr build` with a local `docker build` + `docker push`, gated by a Docker-running pre-check and
  tagged with a unique immutable tag (git SHA + UTC timestamp) so ACA never serves a stale `:latest`.
- **One Container Apps environment per region cap** (`MaxNumberOfRegionalEnvironmentsInSubExceeded`):
  reuse the shared `dbai-poc-env` environment instead of creating a new one; the registry now lives in
  its own resource group (`-AcrResourceGroup`) since its name is globally unique. Env-create is now
  reuse-or-create, and app-create / FQDN resolution halt on failure instead of printing an empty URL.

### Added — Admin Console look-and-feel redesign (spec-014, in progress)

Reworking the console to the reference "Admin Console / Control Plane" look-and-feel (gh #31): a
persistent left-sidebar shell, grouped sections with sub-tabs, a dark-first themeable token system, and
the standalone Graph folded into the Workflow Builder. The intelligent/agentic Assistant is a separate
feature (spec-015). Landing incrementally:

- **Shell foundations**: `design-tokens.css` semantic CSS variables (dark theme) + a runtime
  `tailwind.config` mapping semantic colour aliases, so a future light theme needs no per-screen edits;
  a `NavModel` single source for the five sidebar sections + User Guide; a `UiPreferenceService` that
  persists text size and Assistant-panel state via the existing `localStorage` interop; and an
  `AppShell` layout skeleton.
- **Console shell live (US1)**: the flat top-nav `MainLayout` is replaced by an `AppShell` — a
  persistent left `SidebarNav` (branded "Admin Console / Control Plane", five sections + separated
  User Guide + version footer, accent active-state, collapses to an icon rail below `lg`), a `TopBar`,
  and a right-hand `AssistantPanel` rail — set as the default layout. The Workflow Builder now renders
  inside a full-bleed variant of the same shell (`WorkflowBuilderLayout`) so the canvas keeps its space
  while gaining the sidebar/top bar. The onboarding banner and field-tooltip portal moved into the shell.
- **Graph folded into the Builder (US2)**: the standalone Graph page and its fixed intake-pipeline
  diagram are retired (the Builder already renders the loaded workflow's own graph); the old `/graph`
  route now redirects to the Workflow Builder so existing links never 404. Mermaid is retained for the
  per-run step graph on the Run detail page. Obsolete "Topology/Full page" links removed from RunDetail.
- **Grouped sub-tabs (US3)**: a `SectionTabs` strip renders a section's views as sub-tabs (Monitor →
  Threads / Run History; Automation → Workflow Builder / Workflow Gallery) with the active tab tracking
  the route; single-view sections show none. Active section/sub-view resolution is most-specific-prefix
  (with an alternate prefix so the intake run detail also maps to Monitor); 19 NavModel unit tests cover
  it. Every pre-redesign route resolves under a section (no orphans).
- **Persistent, collapsible Assistant rail (US4)**: the right-hand `AssistantPanel` is now shell-wide
  chrome on every screen — a header, intro, suggestion chips, and an input — that collapses to a compact
  re-open strip (reclaiming the content width) and remembers its open/closed state across navigation and
  reload via `UiPreferenceService` (now initialised on first render in both shell layouts). On the
  Workflow Builder the rail hosts the existing `WorkflowChatPanel` through a Blazor `SectionOutlet`/
  `SectionContent` seam, so the Builder keeps ownership of the panel's reference and callbacks while the
  toolbar Chat toggle, the panel's close control, and the rail collapse all drive the one shared open
  state. Generate/diff/save behaviour is unchanged; the seam is left open for the intelligent Assistant
  (spec-015).
- **In-app User Guide (US6)**: a new `/user-guide` destination (the separated sidebar entry) documents
  what the Admin Console is and, for every primary section, what it does, which screens it contains, and
  how to perform its key tasks. Section coverage is driven from the shared `NavModel`, so the guide stays
  verifiably in sync with the navigation inventory (SC-009) and an undocumented section cannot pass
  silently. Styled with the shell's semantic tokens; spec-015 later adopts this same content as the
  Assistant's knowledge base.
- **Top-bar text size + connection indicator (US5)**: the top bar gains a three-step text-size control
  (`TextSizeControl`) bound to `UiPreferenceService` that drives the root `--text-scale` token through a
  `setRootTextScale` JS helper, so all rem-based content rescales from one variable and the choice
  persists across reloads (FR-018/FR-020). A `ConnectionIndicator` names the connected host (base-URI
  authority) and flips between a connected/disconnected treatment by mirroring the Blazor circuit's
  reconnect overlay via a `connectionMonitor` JS watcher (FR-019).
- **Pages restyled to semantic tokens (Polish)**: the ten console screens (`Index`, `RunHistory`,
  `RunHistoryDetail`, `RunDetail`, `ReviewQueue`, `NewTicket`, `WorkflowGallery`, `Apps`, `AppDetail`,
  `ConnectorSettings`) now draw every colour from the semantic token aliases — no raw `gray-`/`cyan-`/
  status-palette utilities remain (SC-005). Added `--accent-subtle`/`--ok-subtle`/`--warn-subtle`/
  `--err-subtle` tokens so tinted banners/badges theme cleanly without opacity modifiers over
  CSS-variable colours.
- **E2E suite updated for the new IA (Polish)**: existing Playwright tests were re-pointed at the
  grouped sidebar (`data-testid` nav), switched to URL polling for Blazor client-side navigations,
  updated to the semantic-token selectors, and aligned with the chat-open-by-default rail. Suite result:
  73 passing; the remaining failures are pre-existing/environment-gated (the `/apps` page blocks
  server-prerender while probing the container executor — Docker-dependent, feature-013; and ADO/LLM
  credential-gated tests), not introduced by this redesign.

### Fixed

- **Run detail page 500 on SQLite**: `RunHistoryDetail` ordered execution events by a `DateTimeOffset`
  column in the database query, which SQLite cannot translate (`/runs/{id}` returned HTTP 500). Events
  are now materialised and ordered client-side, so the run detail page renders for every run.
- **`/apps` page hung ~20s on first load**: resolving `IAppExecutor` probed the Docker engine, and
  Docker.DotNet's ping can block at the OS connect layer (e.g. a missing Windows named pipe) without
  honoring its `CancellationToken`. The probe now runs on a worker task with a hard 3s wall-clock cap
  (`AppExecutorSelector.TryConnectDocker`), so the page renders immediately and falls back to the
  simulated executor when Docker is unreachable. Also corrected the repo-path placeholder, which
  displayed doubled backslashes (`C:\\ProjectsWin\\DBAI`).

### Added — Admin Console UX: first-run onboarding + field tooltips (spec-009)

Net-new guidance layer on top of the already-typed connector settings (the spec's earlier "retire the
JSON modal / build typed forms" work was already shipped).

- **First-run onboarding banner** (`OnboardingBanner` + `OnboardingStateService`): when the LLM
  connector isn't healthy yet, a dismissible banner guides the visitor to add their LLM key (the one
  required step) with optional deep-links to the other connectors. A failed/throwing health check is
  treated as "not healthy" so first-timers are always guided. Dismissal persists in `localStorage`.
- **Contextual field tooltips** (`InfoTip` + `ITooltipService`): an info icon beside connector fields
  opens a description + example in a layout-root portal (`position: fixed`) so it is never clipped by a
  parent's overflow; it flips above/below the icon based on viewport position.
- **Settings deep-links**: `/settings/connectors?expand=<ConnectorType>` opens that connector's form on
  load (used by the onboarding banner).
- **Visual polish primitives**: `section-enter` fade-in and `btn-success-flash` keyframes added for the
  settings surface.

A new **Apps** surface lets you point at a target repository by local path, build and run that repo's
application in its own **disposable container**, and link any saved workflow to **monitor** it —
mirroring the reference LangGraph app's repo → container build/run → workflow-monitors-it architecture
(see `specs/013-repo-app-monitoring/`).

- **App registry**: register a repo (name, local path, optional branch, optional build command,
  required run command); owner-scoped with per-owner unique names; persisted in SQLite
  (`MonitoredApps`). Lifecycle mirrors the reference: Registered → Building → (Ready | Build Failed);
  Ready → Running → Ready, with a single-in-flight guard so an app is never left stuck (FR-008/016).
- **Throwaway-container build/run**: an `IAppExecutor` seam with two implementations — a **simulated**
  executor (default; synthesizes outcomes, no engine required) and a real **Docker** executor
  (`Docker.DotNet`) that builds/runs in a fresh container removed by its specific id afterwards
  (Article II), with bind-mounted read-only repo, a per-app artifact volume, captured **secret-redacted**
  logs (Article IX), a hard timeout, and start-failure handling. The active executor is chosen at
  startup (Docker when reachable and not in demo mode, else simulated) and shown as an indicator.
- **Workflow monitoring**: link any saved workflow as an app's monitor. A hosted background loop builds
  a `MonitoringSnapshot` (status + latest run outcome/summary + redacted log tail, FR-018) and, on a
  detected problem, starts a bounded run via the existing `WorkflowExecutionOrchestrator` — the same
  path any run uses — de-duplicated by issue signature so a recurring problem is raised once
  (close-the-loop). Per-app monitoring health (last cycle, ok/fail, error) is surfaced.
- **UI**: an **Apps** nav tab, an `/apps` list with status badges + register form + Build/Run/Link/Remove,
  and an `/apps/{id}` detail page with build/run summaries, full redacted logs, the workflow link, and
  monitoring health.
- Reuses existing machinery (framework-first, Article VII): the workflow orchestrator, saved-workflow
  gallery, the connector-config/`ISecretProtector` pattern, `PipelineDbContext` idempotent DDL, and the
  in-process live-update pattern. The only new dependency is `Docker.DotNet`.

### Added — One-URL Azure Container demo deployment (foundation)

The app can now be packaged as a single public-URL Azure Container Apps demo that mirrors the
reference LangGraph app: scale-to-zero when idle, back-office connectors pre-seeded from the Forge
Vault, and the visitor supplying only their own LLM key (see `specs/012-azure-container-deploy/`).
This change set is the buildable foundation; the live cloud deploy + validation are operator steps.

- **Boot-time connector seeding** (`DemoConnectorSeeder`): on each (ephemeral) startup, the demo's
  ServiceNow, Azure DevOps, and Messaging connectors are seeded from `ConnectorSeed__*` environment
  variables (vault-injected at deploy time) through the existing connector repository, so secrets are
  encrypted at rest and seeded rows are indistinguishable from UI-configured ones. The **LLM
  connector is never seeded** — each visitor enters their own key (FR-004).
- **Design-time LLM hot-reload** (`HotReloadAnthropicService`): the Workflow Builder AI assistant and
  Node Realization now resolve the LLM key + model from the stored LLM connector on each call
  (config fallback), matching the per-run execution paths — so the single visitor-entered key powers
  every LLM feature without an app restart.
- **Configurable Data Protection key ring** via `DataProtection:KeyRingPath` — points at a writable,
  ephemeral container path so secrets encrypt/decrypt within a container lifetime and reset on cold
  start; falls back to `%APPDATA%` locally.
- **Container + deploy assets**: root `Dockerfile` (multi-stage, non-root, Kestrel on `:8080`,
  ephemeral SQLite) + `.dockerignore`; `deploy/aca/` local `az` deploy (`deploy.ps1`,
  `seed-secrets.ps1`, `team.env.example`) creating an ACA app with `--ingress external
  --min-replicas 0 --max-replicas 1` and vault-sourced ACA secrets. No GitHub Actions (Article VIII);
  no secret value committed (Article IX).

### Changed — Teams connector generalized to a multi-platform Messaging connector

The single-purpose "Teams" connector is now a **Messaging** connector that targets Microsoft
Teams, Slack, or Discord, with **MCP-first delivery and a webhook fallback** (see
`specs/010-messaging-connector/`).

- **MCP-first delivery**: when an MCP server endpoint is configured, messages are delivered by
  calling its send-message tool via the official MCP C# SDK (`ModelContextProtocol.Core`) over
  HTTP/SSE; tool arguments are built from an operator-supplied JSON template with `{{target}}` /
  `{{message}}` placeholders. With no MCP server configured, delivery falls back to the platform
  webhook. Selection is configuration-based — an unreachable MCP server reports a failure rather
  than silently using the webhook.
- **HITL + notify-node** delivery now flows through the same delivery seam, so pause notifications
  reach whichever platform is configured (Teams/Slack/Discord), not just Teams.

- **Platform dropdown** on the Connector Settings "Messaging" card (Teams / Slack / Discord),
  mirroring the LLM provider dropdown. Each platform uses its own webhook payload and success
  signal: Teams (Adaptive Card → `"1"`), Slack (`{"text"}` → `"ok"`), Discord (`{"content"}` → 204).
- New single `IMessageDelivery` seam selects MCP-first with webhook fallback and backs the
  Settings **Test Connection** / health check; the result names the platform and the path used.
- `ConnectorType.Teams` renamed to `ConnectorType.Messaging`; a legacy `"Teams"` row in the
  database is read defensively as Messaging (no migration required).
- **Removed the duplicate legacy connector modal** (`ConnectorConfigModal`/`ConnectorSection`/
  `ConnectorStatusBadge`); the home-page gear now opens the dedicated `/settings/connectors` page,
  eliminating a second, divergent connector UI.
- Secrets unchanged in handling: the webhook URL is stored encrypted with "leave blank to keep
  existing" semantics.

### Fixed — ServiceNow health check failed when Instance URL included a path

A ServiceNow Instance URL pasted from the browser address bar (e.g.
`https://acme.service-now.com/login.do`) caused the health check to build
`…/login.do/api/now/table/sys_properties`, which ServiceNow 302-redirects to its login
page — so even valid credentials never authenticated and the connector showed "Unhealthy".
`ServiceNowClient` now normalizes the configured URL to its origin (scheme + host)
before appending the Table API path, so stored credentials work regardless of how the URL
was entered. No re-entry is required for the existing stored value.

Additionally, the Connector Settings page now clears the in-memory health-check result after
a save, so a stale "Unhealthy / no credentials stored" message no longer lingers over freshly
entered credentials.

### Fixed — ServiceNow credentials lost on app restart

`AddDataProtection()` was called without key persistence. ASP.NET Core Data Protection
generates ephemeral keys by default — a restart produces new keys and all previously
encrypted connector secrets become unreadable ("Stored credentials could not be decrypted").
Keys are now persisted to `%APPDATA%\AzureWorkflowPOC\DataProtection-Keys` with a pinned
application name so they survive restarts and redeploys.
**Action required**: re-enter ServiceNow (and any other connector) credentials once after
this restart; they will persist from then on.

### Changed — LLM connector redesigned: provider dropdown + live model list

The LLM connector no longer asks for a raw URL.

- **Provider dropdown** — select Anthropic (Claude) or OpenAI
- **API Key** — password field; leave blank to keep the stored key
- **Fetch Models button** — calls the provider's live models API (`/v1/models`)
  using the entered key (or the stored one if left blank) and populates a dropdown.
  No model names are hardcoded; no fallback list exists.
- On opening Edit the model list auto-fetches if a provider and stored key are already set.
- Storage format: `NonSecretConfig` now stores `{"provider":"anthropic","modelName":"..."}`;
  the `providerEndpoint` URL field is removed.

### Fixed — ADO preflight fails for custom inherited process templates (v1.2.3)

`ResolveInheritedParentTypeAsync` was calling `_apis/process/processes/{id}` which returns the
basic `Process` object without `parentProcessTypeId`. The correct endpoint is
`_apis/work/processes/{id}` (Work namespace) which includes the `parentProcessTypeId` field
needed to walk up the inheritance chain.

### Fixed — ADO preflight fails for custom inherited process templates

`DetectProcessTypeAsync` only matched the two built-in Agile and Scrum GUIDs. Projects using a
custom inherited process (e.g. a process named "Agentic" that inherits from Agile) have a unique
GUID that doesn't match either built-in, causing a spurious "unsupported process type" error even
though the process is perfectly compatible.

Fix: when the project's `templateTypeId` is not a known built-in GUID, the service now calls
`_apis/process/processes/{typeId}` to read `parentProcessTypeId`. If the parent matches Agile or
Scrum, the project is treated accordingly. Covers the most common case — custom inherited processes
created in any ADO organisation.

### Fixed — ADO preflight process-type detection returns 404

`DetectProcessTypeAsync` was calling `_apis/work/process/configuration` to read the project's
process template GUID. That endpoint returns backlog/field configuration — it does not expose
`templateTypeId` and is not available at api-version 7.1, causing a 404 for all users.

Replaced with the documented projects capabilities endpoint:
`_apis/projects/{project}?includeCapabilities=true&api-version=7.1`
which returns `capabilities.processTemplate.templateTypeId` and works for all ADO organisations.
Updated all unit-test HTTP mocks to match the new URL and response shape.

### Added — ADO Telemetry Field Bootstrap: preflight service bootstraps custom fields before ticket creation (spec 009)

Before work items are created by the Spec Kit pipeline, a preflight step ensures the required ADO
custom telemetry fields exist. The feature operates in two modes chosen automatically at runtime:

- **Bootstrap mode (US1)** — admin access detected: creates 14 custom fields (`Custom.AISessionID`,
  `Custom.AIModelUsed`, token/cost/cache/rate counters, `Custom.SpeckitPhase` picklist) across
  `User Story` and `Task` work item types via the ADO Inherited Process API. Retries up to 3 times
  with exponential backoff on 429/503 HTTP errors. Writes a `.ado-bootstrap-manifest.json` to the
  active spec feature directory.
- **Adaptive mode (US2)** — admin probe returns 403: scans the org's existing fields and builds a
  fallback mapping (exact match → `FallbackReferenceName` → type-level fallback → log-only). Lets the
  pipeline continue without admin rights at the cost of telemetry fidelity.
- **Config override (US4)** — callers can pass a custom `AdoTelemetryFieldConfig` to `RunPreflightAsync`
  to swap the embedded default field schema without redeploying.
- **Startup auto-run** — `app.Lifetime.ApplicationStarted` fires a fire-and-forget preflight so fields
  are ready before the first ticket request.
- **"Test Connection" button (US3)** — the ADO connector card on `/settings/connectors` shows a
  dedicated "Test Connection" button. On click, `IAdoTelemetryPreflightService.RunPreflightAsync` runs
  and the result is surfaced via a `data-testid="ado-preflight-result"` badge.
- **SK Process step** — `AdoTelemetryPreflightStep : KernelProcessStep<AdoPreflightStepState>` wraps
  the service for integration into the Spec Kit SK process pipeline. Emits
  `AdoPreflightSucceeded` / `AdoPreflightFailed` events for downstream steps to react to.
- **Unit tests** — `AdoTelemetryPreflightServiceTests` and `AdoTelemetryPreflightStepTests` (408 tests
  total passing). `RetryDelayFactory` is publicly overridable so retry tests complete in milliseconds.
- **E2E tests** — two new Playwright tests in `ConnectorSettingsTests`:
  `ConnectorSettings_AdoPreflightButton_RendersOnAdoCard` (always runs) and
  `ConnectorSettings_AdoPreflightButton_WhenClicked_ShowsResultBadge` (live-credential path gated on
  `E2E_TEST_ADO_PAT`).

### Added — Production Platform Parity: run persistence, HITL close-loop, execution history, DoR validation (spec 008)

Implements the Azure-stack completeness feature set across US1–US7:

- **Run persistence (US1)** — `WorkflowBuilderRuns` and `WorkflowExecutionEvents` tables; `EfWorkflowRunRepository` writes every status transition via the existing `IDbContextFactory` singleton-safe pattern. `WorkflowRunRetentionService` (hosted service) purges terminal runs older than `RetentionDays` (default 30).
- **HITL close-loop via Teams (US2)** — `IWorkflowApprovalNotifier` / `TeamsWorkflowApprovalNotifier` sends Adaptive Cards via Graph API on run pause. `TeamsWebhookController` (`/api/teams/approval`) routes inbound decisions to `IWorkflowExecutionOrchestrator.SubmitApproval`. `ApprovalNodeConfig` extended with `ApproverChain`, `TimeoutMinutes`, `EscalationPolicy`.
- **Review Queue (US3)** — `/review-queue` Blazor page lists Paused runs with one-click Approve/Reject; updates live via orchestrator `RunUpdated` event subscription.
- **Execution History (US4)** — `/runs` list page and `/runs/{id}` detail page showing step timeline, LLM token costs, and failure reasons. `SqlWorkflowObserver`, `SignalRWorkflowObserver`, `AzureMonitorWorkflowObserver` fan-out to all registered observers on each event.
- **SignalR hub (US4)** — `WorkflowRunHub` at `/hubs/workflow-run` enables per-run group subscriptions and review-queue broadcast notifications.
- **DoR validation (US7)** — `IWorkflowReadinessRule` / `IWorkflowPreRunValidator` framework; four built-in rules: `TriggerNodePresentRule`, `AllNodesRealizedRule`, `ConnectorsHealthyRule`, `ApprovalNodesConfiguredRule`. Rules disabled via `DorRules:DisabledRuleNames` configuration.
- **Prompt audit filter** — `WorkflowPromptRenderFilter` logs SHA-256 hash of rendered prompts, never the text (Article IX).
- **Nav links** — Run History and Review Queue added to the main navigation bar.
- **Unit tests (T024–T077)** — 42 new passing tests: repository CRUD + purge, observer persistence + fan-out isolation, `WorkflowDesignSkillService` generation + clarifying-question path, and all four DoR rules plus validator skip/sort. `EfWorkflowRunRepository` updated to evaluate `DateTimeOffset` sorts and purge filters in-process for SQLite portability.

### Added — Node Realization: turn plain-language workflows into production-ready ones (spec 007)

A new **"Make it real"** flow converts a plain-language workflow into runnable, production-ready
configuration. The assistant proposes per-node configuration, the user reviews and accepts it, and the
workflow reports an honest production-readiness verdict and runs from the accepted configuration. This
is User Story 1 (the MVP) of spec `007-node-realization`.

- **Per-node realized config** — each node type has a typed configuration record (agent instruction +
  model + output shape; notify connector/recipient/message; route conditions + default; transform
  mappings; data read/write; approval prompt/options; trigger initial-data shape). All are stored in the
  existing `WorkflowNode.FunctionConfig` as a versioned envelope via `NodeConfigSerializer`, so no schema
  migration is needed.
- **`WorkflowRealizationService`** — proposes configuration for each node using schema-bound forced
  tool-use (`IStructuredCompletionService`), so the model returns structured config, never free text
  (Article VII). Proposing is read-only; `AcceptProposal` is a separate, deterministic, single-node
  mutation that records an intent hash for out-of-date detection.
- **`WorkflowReadinessService`** — evaluates production readiness: structural validity, per-node
  intrinsic validity, cross-node consistency, and live connector health. Validation of realized config
  lives here (the Run gate), keeping `WorkflowValidator` structural-only so plain-language drafts still
  save.
- **Builder UI** — a "Make it real" toolbar action, a streaming review panel with a single explicit
  "Accept all" confirmation, a production-readiness indicator, and a green "realized" badge on each node.
- **Review & adjust (US2)** — each proposal can be accepted, **edited in plain language** (no raw
  code/schema — the node type's primary field), rejected, or **regenerated** (re-proposed for that one
  node). Single-node acceptance is deterministic and touches only its own node.
- **Out-of-date detection (US2)** — when a realized node's plain-language intent (label, goal, or
  connected edges) changes, its accepted config no longer matches what was asked; the node is reported
  out-of-date and the workflow is no longer production-ready, via a content-based intent hash recorded
  at acceptance (not a timestamp, so unrelated re-saves never raise a false signal). Readiness re-checks
  on a meaningful edit only — pure position drags don't trigger a connector-health round-trip.
- **Runtime executes from realized config** — each step receives its node's configuration as Semantic
  Kernel step state (`AddStepFromType<TStep, TState>`). Agentic steps run the realized instruction;
  notify steps resolve the bound connector (secrets fetched at execution, never in config — Article IX);
  branch steps now route correctly (this fixed a pre-existing bug where the visual-workflow orchestrator
  never populated route port labels, so branch nodes always failed); transform steps apply the realized
  field mappings to structured (JSON) payloads; data steps resolve the bound connector and apply the
  configured operation; the trigger read-path reads `TriggerNodeConfig`, back-compatible with the legacy
  `{initialDataDescription}` blob.
- **Secrets discipline** — proposals, prompts, and `FunctionConfig` never carry secrets; only
  `ConnectorType` references (Article IX).
- **Per-node "Realize this node" (US3)** — right-clicking any canvas node opens a context menu with a
  "Realize this node" action that calls `ProposeNodeAsync` for exactly that node and opens the review
  panel scoped to one proposal. Other nodes are untouched. After acceptance, readiness is re-evaluated.
  Enables incremental realization: add a new node to an already-realized workflow and realize only it.
- **Honest Blocked gating when connectors are unconfigured (US4)** — `ProposeNotifyAsync` and
  `ProposeDataAsync` now check the connector repository before calling the LLM. When no connector of
  the required category (messaging or data) is configured, they return a `Blocked` proposal immediately
  with a plain-language reason naming the missing connector type — no LLM call is wasted. The `CanRun`
  gate was extended to require `IsProductionReady` from the readiness report, so a workflow whose nodes
  are configured but whose connectors are unhealthy cannot be run. The toolbar's disabled-Run label now
  shows the specific blocking reason from the readiness report (e.g. "This step needs a connector
  (Teams) that has not been configured yet") rather than the generic "Set up all steps first".
- Tests (TDD): unit coverage for config round-trip, proposal ordering/no-mutation, single-node accept +
  provenance, out-of-date detection, partially-realized single-node isolation (T041), readiness
  ready/blocked/needs-input/blocking-reason content (T044), and Blocked proposal when no messaging
  connector is configured (T044/T047); an end-to-end runtime test proving the realized instruction
  reaches the step through the real local process runtime; and Playwright Scenarios A (make-it-real →
  accept → readiness verdict → Run state mirrors verdict), B (edit-then-accept, proposal count
  decreases), C (per-node context-menu realize → exactly 1 proposal card → readiness re-evaluated),
  and D (Run button and readiness indicator are coherent; specific blocking reason shown when not ready)
  verified green against the live app with a real Anthropic key.

### Fixed — Saved edits silently reverted by stale auto-save (data loss)

A saved workflow edit could be overwritten seconds later and revert to an older state, so "Save"
appeared not to work. `WorkflowBuilderService` is scoped per Blazor circuit and runs a 60-second
`System.Threading.Timer` for auto-save, but Blazor Server retains disconnected circuits for ~3
minutes — during which the timer kept firing `SaveAsync` with that circuit's stale captured
`_workflow`, clobbering newer saves made from another circuit (e.g. after a reload or in a second
tab). Reproduced and fixed:

- **Content-signature change detection** — `WorkflowBuilderService` now fingerprints the content it
  last persisted (name, nodes, edges, settings, generated code, chat) and the auto-save timer
  **skips when nothing has changed**. A stale circuit can no longer re-save its old snapshot over a
  newer save. The baseline is seeded from the loaded workflow so an untouched workflow is never
  needlessly re-saved (which would also wrongly bump its `LastModifiedAt` for the "resume most
  recent" sort).
- The auto-save interval is now injectable (defaults to 60 s) so the behaviour is unit-testable.
- Regression tests: `AutoSave_DoesNotResave_WhenContentUnchanged` and
  `AutoSave_PersistsOnEdit_ThenStopsResaving`. The `DBAIAzure.Tests` project now references
  `DBAIAzure.Web` so the service is tested directly (previously only constants were asserted).
- Verified live: the clobber scenario that previously reverted a save now keeps it
  (`NEW survived? true`).

### Fixed — Node edits lost on navigation; builder now persists & resumes work

Node text (and any other canvas edit) was saved to the database but to a workflow id the URL
never pointed at, so navigating away and back showed a freshly-generated example and the edits
appeared to "revert". Root cause and fixes in `Pages/WorkflowBuilder.razor` (+ `WorkflowGallery.razor`):

- **Resume most recent on the bare URL** — opening `/workflow-builder` with no id now reopens the
  most-recently-edited saved workflow instead of regenerating a throwaway example. First-time users
  (no saved workflows) still get the entry-choice modal.
- **Bind the URL to the workflow after it is persisted** — on the first successful save (manual or
  auto-save), and when resuming a workflow reached via the bare URL, the page rewrites the address to
  `/workflow-builder/{id}` (history-replace, no reload) so a browser refresh reloads the same work.
- **`/workflow-builder/new`** is now the explicit "start a new workflow" entry point; the Gallery's
  "New Workflow" button targets it. Bare `/workflow-builder` is reserved for resuming.
- **Unsaved-changes dialog now completes the navigation** — its Save button previously persisted but
  silently kept the user on the page; it now resumes the originally-requested navigation on success
  and keeps the modal open if the save fails.
- **New-workflow name de-duplication** — example/scratch workflows get a unique " (n)" suffix when a
  name is already taken, preventing the `(OwnerId, Name)` uniqueness conflict that made the first save
  of a second new workflow fail silently. Manual save now also surfaces that conflict as a toast.
- E2E `Scenario9_RenamedLabel_PersistsAfterNavigatingAway` covers the regression; all 14
  label-editing E2E tests and 298 unit tests pass.

### Fixed — Undo label revert not updating DOM (spec 006 T018)

- `ApplyLabelChange` now calls `nodeModel.Refresh()` instead of `_diagram.Refresh()`.
  ZBD's per-node `Refresh()` sets the `_shouldRender` flag and calls `StateHasChanged()`
  directly on the node's widget, guaranteeing a re-render when a label changes outside of
  an active edit session (undo / redo). The diagram-level `Refresh()` lacked this guarantee
  under ZBD 3.0.4.1's `NodeRenderer` optimisation.
- Added `IsLabelEditing` flag on `WorkflowNodeModel`; ZBD keyboard shortcuts
  (`Delete`/`Backspace`) now check this flag so typing in the label input never
  accidentally deletes the node.
- E2E test `Scenario4_LabelUndo_CtrlZ_RestoresPreviousLabel` now uses the toolbar Undo
  button (more reliable than keyboard Ctrl+Z in Blazor Server SignalR tests) and waits for
  DOM re-renders rather than fixed timeouts. All 31 E2E tests pass.

### Fixed — Node text editing in Workflow Builder (spec 006)

Two-part fix for the bug where users could not type into workflow node fields because
text input was silently reset by Blazor re-renders.

**Part 1 — Config panel reset guard** (`WorkflowNodeConfigPanel.razor`):
- Added `_lastInitialisedNodeId` field that guards `OnParametersSet()` from resetting
  `_goalPrompt`, `_inputLabel`, and `_outputLabel` when the same node is re-rendered
  (e.g. while the 200 ms goal-preview debounce fires a parent `StateHasChanged()`).
- `OnCloseAsync()` clears the guard so re-opening the panel for the same node
  correctly reinitialises fields from the saved node record.

**Part 2 — Inline label editing** (`WorkflowNodeRenderer.razor`):
- Node label `<span>` is now a dual-state span/input: double-clicking the label text
  switches to an `<input>` field with the current label pre-filled.
- `_labelBuffer` is a local field never written by `OnParametersSet` — it is fully
  isolated from parent re-renders while the user is typing.
- Committing with Enter or blurring the input calls `Node.RaiseLabelCommitted()`;
  pressing Escape restores the pre-edit label without raising the committed event.
- `@ondblclick:stopPropagation="true"` on the label container prevents the node-body
  double-click handler (which opens the config panel) from firing when renaming.
- Keyboard accessibility: `tabindex="0"` on the outer node div; `Enter` activates
  inline editing when the node has keyboard focus.
- Empty committed labels display a type-appropriate fallback ("AI Agent", "Notify",
  etc.) rather than a blank header; the fallback is never stored or pre-filled.

**New types** (`WorkflowDiagramModels.cs`):
- `LabelCommitArgs readonly record struct` — carries `NodeId`, `PreviousLabel`,
  `NewLabel` from the renderer's committed event to the canvas handler.
- `LabelCommitted event Action<string, string>?` on `WorkflowNodeModel` (alongside
  the existing `DoubleClicked` event) — the signalling channel that avoids the
  EventCallback limitation when components are registered via `RegisterComponent`.
- `RenameLabelAction : ICanvasAction` in `WorkflowCanvas.razor` — undoable rename
  command that pairs with the existing `AddNodeAction`/`AddEdgeAction` undo stack.

**New tests** (`tests/DBAIAzure.Tests/`):
- `WorkflowNodeLabelEditTests.cs` — 8 pure domain tests covering rename Do/Undo,
  no-op guard, edit state machine transitions, double-fire guard, re-edit value, and
  empty label fallback.
- `WorkflowNodeConfigPanelResetGuardTests.cs` — 5 pure domain tests covering the
  same-node guard, different-node reset, close-then-reopen, null-node safety, and
  undo order.

**New E2E test stubs** (`tests/DBAIAzure.E2ETests/Tests/WorkflowNodeLabelEditTests.cs`):
- 5 Playwright stubs (Scenarios 1–5 from `specs/006-fix-node-text-editing/quickstart.md`)
  for config-panel reset guard, double-click rename, Escape cancel, Ctrl+Z undo, and
  empty-label placeholder.

### Changed — E2E tests upgraded to real user interactions

Replaced four "element presence" WorkflowBuilder tests with tests that physically click,
type, and interact the way a real user would, then assert on the resulting state change:

- `WorkflowName_ClickToRename_CommitsNewName` — clicks the name span, types "My Renamed
  Workflow", presses Enter, asserts the span shows the new name.
- `PaletteSearch_TypeKeyword_FiltersVisibleNodes` — types "trigger" in the palette search
  box (100 ms debounce), asserts "Start / Trigger" remains visible while "Notify" is hidden.
- `PaletteClickToPlace_AddsNewNodeToCanvas` — clicks "Add Reason & Decide node to canvas",
  waits for a new `.workflow-node` div to appear in the diagram.
- `RunButton_Click_OpensRunInputModal` — clicks the ▶ Run button, fills the scenario
  textarea, clicks Cancel, asserts the modal closes.

`ChatToggleButton_Click_OpensChatPanel` and `RootBuilderUrl_Loads_WithoutDatabaseError`
retained unchanged.

### Fixed — WorkflowDefinitions table name mismatch

- `PipelineDbContext`: added `.ToTable("WorkflowDefinitions")` to the `WorkflowDefinitionRecord`
  model configuration so EF Core queries the table name used by the raw-SQL idempotent migration,
  not the DbSet property name `Workflows`. Databases created before the Visual Workflow Builder
  feature landed had `WorkflowDefinitions` (from the raw SQL) but not `Workflows` (from EF Core
  convention), causing `SqliteException: no such table: Workflows` on `/workflow-builder`.
- Added E2E regression test `RootBuilderUrl_Loads_WithoutDatabaseError` that navigates to
  `/workflow-builder` (the user-facing path) and asserts no unhandled database error, closing
  the gap where all prior tests used `/workflow-builder/new` and bypassed `ListByOwnerAsync`.

### Fixed — Playwright E2E Test Suite (all 17 tests now pass)

- `WebAppFixture`: resolved user-local `.dotnet/dotnet.exe` instead of the system dotnet
  at `C:\Program Files\dotnet`, which lacks the ASP.NET Core 8 runtime, causing all tests
  to time out waiting for the app to start.
- `WorkflowBuilderTests`: navigate to `/workflow-builder/new` (non-GUID id bypasses the
  first-run entry choice modal and loads the example workflow directly); use
  `Page.WaitForFunctionAsync` to check `getBoundingClientRect().height > 0` instead of
  Playwright's static visibility heuristic, which races with Tailwind Play CDN's async CSS
  injection via MutationObserver; corrected all CSS selectors to match actual rendered HTML
  (`#workflow-canvas-drop-zone`, `.workflow-palette`, `.run-btn-ready`,
  `span[aria-label='Rename workflow — click to edit']`, `button[aria-label='Open chat panel']`).
- `NavigationTests`: same `/workflow-builder/new` + `WaitForFunctionAsync` canvas check.
- `ThreadsPageTests`: fix connector-modal selector to `h2:has-text('Connector Configuration')`
  (the modal renders a styled div without `role="dialog"`, not a `<dialog>` element).

### Added — Playwright E2E Test Suite

- New project `tests/DBAIAzure.E2ETests` with 17 Playwright tests covering every navigation
  tab (Threads, Graph, New Ticket, Workflow Builder, Workflow Gallery), the canvas, node
  palette, toolbar, chat toggle, connector gear icon, and run button.
- `WebAppFixture` starts the real Blazor Server app on port 5099 via a child process so
  Playwright connects through genuine HTTP/SignalR — no TestServer shortcuts.
- `PlaywrightFixture` manages a shared headless Chromium browser; each test gets an isolated
  `IBrowserContext`.
- `scripts/run-e2e.ps1` — one-command build + browser install + test run.
- Constitution Article V updated: Playwright replaces Cypress as the mandatory E2E framework.

### Added — Workflow Builder UX Master Review (`specs/005-workflow-ux-redesign`)

All 10 UX improvements shipped on `feature/visual-workflow-builder`:

1. **First-run entry choice** — users with no saved workflows see a "Start from scratch / Try the example" modal instead of a blank canvas; `WorkflowEntryChoiceModal.razor`; `OnScratchChosen` / `OnExampleChosen` callbacks.
2. **Welcome overlay & empty-canvas guide** — `WorkflowCanvas` shows a full welcome illustration until the very first node is placed; thereafter an empty canvas shows only a minimal "drag a step to continue" label; Triggers category pulses green when the canvas is empty.
3. **Node configuration affordance** — every unconfigured node shows an amber "!" badge and a plain-text "Set up →" label beneath it; single-click shows a 2-second "Double-click to configure" tooltip (60-second per-node cooldown); config panel opens with keyboard focus on the first field; "Save" button renamed to "Done".
4. **Live goal → label sync** — typing in the Goal field of the node config panel updates the canvas node label in real time via a 200 ms debounced `OnGoalPreview` EventCallback.
5. **Run button disabled reason** — an always-visible plain-language reason appears beside the Run button when it is disabled: "Needs a trigger to start" or "Set up all steps first"; text disappears and button fades to green (300 ms CSS transition) when all nodes are ready.
6. **Inline workflow name editing** — clicking the workflow name in the toolbar opens an inline input; Enter or blur commits; blank name reverts and flashes a 1-second tooltip; `<PageTitle>` updates reactively; name amber-coloured when "Untitled Workflow".
7. **Unsaved-changes navigation guard** — any topology change, name commit, or config Done sets a dirty flag; navigating away while dirty shows a three-button confirmation: "Save & Continue", "Discard Changes", "Cancel — keep editing"; guard active via `Nav.RegisterLocationChangingHandler`.
8. **Chat panel canvas-change indicator** — an orange dot badge appears on the Chat button whenever the canvas changes after code has been generated; dot clears when chat is opened or code is regenerated; "Update code" button in the workflow-changed banner triggers `RegenerateWithDiffAsync`, which computes a DiffPlex-backed compact diff (+ / - / context lines with "Show full code ↓" toggle).
9. **Post-run feedback pre-population** — clicking the feedback button on a node badge opens the chat panel with a pre-populated message naming the step, its status, and its output excerpt.
10. **Gallery improvements** — search input above the card grid filters by workflow name and node type; node-type summary chips (▶ Trigger, 🧠 AI Step, 👤 Human, etc.) replace the raw step count on each card; SVG thumbnails generated on load for any workflow that lacks one; zero-result state with plain-language message.
- **Keyboard shortcuts panel** — "?" button at the far right of the toolbar opens a floating `WorkflowKeyboardShortcutsPanel`; lists Ctrl+Z / Ctrl+Y / Delete / Ctrl+S shortcuts; closes on Escape or outside click.
- **New types** — `DiffResult`, `DiffLine`, `DiffLineType` in `DBAIAzure.Core.Models.DiffModels`; `IWorkflowThumbnailGenerator`, `IWorkflowCodeDiffService` interfaces in `DBAIAzure.Core.Interfaces`.
- **New services** — `WorkflowThumbnailGenerator` (SVG, 200×100 viewBox, colour-coded `<rect>` per node) and `WorkflowCodeDiffService` (DiffPlex-backed, ±3 context lines) in `DBAIAzure.Core.Services`.
- **15 new unit tests** — `WorkflowThumbnailGeneratorTests` (6), `WorkflowCodeDiffServiceTests` (7), `WorkflowEntryChoiceModalTests` (5), `WorkflowNodeRendererAffordanceTests` (5), `WorkflowUnsavedChangesModalTests` (5), `WorkflowToolbarNameEditTests` (5); all 283 passing.

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
