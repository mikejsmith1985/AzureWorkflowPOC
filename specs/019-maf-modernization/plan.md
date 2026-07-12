# Implementation Plan: Modernize the Agent Stack onto Microsoft Agent Framework (MAF)

**Branch**: `feature/019-maf-modernization` | **Date**: 2026-07-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/019-maf-modernization/spec.md`

## Summary

Replace the experimental SK Process Framework (`1.77.0-alpha`, `SKEXP0080`) that today runs all three
pipelines (ticket intake, phase-handler, visual workflow builder) with **Microsoft Agent Framework 1.0
Workflows** (GA), and move the model layer from SK `IChatCompletionService` to the provider-neutral
`Microsoft.Extensions.AI` `IChatClient`. Behavior is preserved end-to-end (no regression); the migration
also delivers **bring-your-own-AI** (Claude default, provider selectable by configuration) and re-homes
AI cost/telemetry capture, MCP tool delivery, and Azure Monitor tracing onto MAF/M.E.AI seams. Production
cutover is **atomic**; SK-paused runs are **auto-migrated** in place. Technical approach and all package
choices are fixed in [research.md](./research.md) (execution path stays on GA/stable packages only).

## Technical Context

**Language/Version**: C# / .NET 8 (retained; `global.json`-pinned SDK).

**Primary Dependencies (target)**: `Microsoft.Agents.AI` + `Microsoft.Agents.AI.Workflows` (GA),
`Microsoft.Extensions.AI` / `.Abstractions`, official `Anthropic` SDK (`.AsIChatClient`),
`Azure.Monitor.OpenTelemetry.Exporter` (unchanged), `ModelContextProtocol.Core` (MCP, retained).
**Removed**: `Microsoft.SemanticKernel*` (core + Process.Core + Process.LocalRuntime).

**Storage**: EF Core (SQLite dev / SQL Server) — unchanged, plus a new EF-backed
`ICheckpointStore<JsonElement>` implementation for MAF workflow checkpoints.

**Testing**: xUnit (unit), bUnit (component), Playwright (E2E) — all retained; `scripts/run-e2e.ps1`.

**Target Platform**: Blazor Server web app (`DBAIAzure.Web`, Kestrel) + console runner
(`DBAIAzure.Runner`); Linux container (ACA) and Windows dev.

**Project Type**: Web application (Blazor Server) + background/console host + class libraries.

**Performance Goals**: No regression >10% in end-to-end run latency or per-model-call overhead vs. the
pre-migration build for equivalent runs (SC-010).

**Constraints**: Zero experimental/pre-release packages and zero experimental-API pragmas in the
execution path (FR-003/SC-002); single active AI provider per instance (Clarifications Q4); atomic
production cutover, no dual-runtime (FR-016); durable pause/resume preserved across restart
(FR-006/FR-006a).

**Scale/Scope**: ~57 files reference `Microsoft.SemanticKernel*`; source footprint = 17 executor/step
classes, 3 graph builders, 3 orchestrators, 2 external channels, 2 kernel filters, 2 LLM connector
impls, 4 kernel factories. Concentrated in `DBAIAzure.Processes` + `DBAIAzure.Web`.

## Constitution Check

*GATE: must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Article | Gate | Status |
|---|---|---|
| I — Prime Directive (BEST, production-ready) | Move off experimental alpha onto GA foundation; parity-tested | ✅ This feature's entire purpose |
| II — Process Protection | No wildcard kills; target PIDs during dev/verify | ✅ Honored in quickstart |
| III — Branching | Work on `feature/019-maf-modernization`; PR to main | ✅ On branch |
| IV — Code Quality | Naming/doc/40-line rules apply to new executors/clients | ✅ Enforced during Phase 3/4 |
| **V — Testing (TDD, 3-layer)** | Parity tests written **failing-first**; unit mocked, integration real, E2E Playwright | ✅ Plan mandates Red→Green parity per pipeline |
| VI — Documentation | CHANGELOG updated; specs tree is the artifact home | ✅ FR-017 |
| **VII — Framework-First** | **Governing framework changes SK → MAF.** Use MAF Workflows/`RequestPort`/checkpointing/`IChatClient` natively rather than re-hand-rolling; **Article VII text itself must be updated to name MAF** (FR-017) | ⚠️ Governance update required — see note |
| VIII — Release | Deliberate, reproducible; atomic cutover release + one-time paused-run migration | ✅ D10/D4 |
| IX — Secrets | Provider keys by reference from config/vault, per provider (FR-009c) | ✅ |
| X — Verification & Proof | Parity evidence: real runs, restart-resume, token-cost equality (quickstart) | ✅ |
| XI — Output Restraint | No scratch dashboards; artifacts confined to specs tree | ✅ |

**Governance note (Article VII)**: The constitution currently *mandates* SK Process Framework primitives
(`KernelProcess`, `ProcessStepBuilder`, `IExternalKernelProcessMessageChannel`, SK structured output).
This plan intentionally supersedes those with MAF equivalents. That is not a violation of Framework-First
— it is Framework-First applied to the newly-governing framework — but it **requires amending Article VII
text** to name MAF (tracked by FR-017). No unjustified complexity is introduced; no Complexity Tracking
entries required.

## Project Structure

### Documentation (this feature)

```text
specs/019-maf-modernization/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D10 (complete)
├── data-model.md        # Phase 1 — entities & state
├── quickstart.md        # Phase 1 — parity validation guide
├── contracts/           # Phase 1 — internal seams (provider, checkpoint store, HITL, telemetry)
│   ├── ichatclient-provider.md
│   ├── workflow-executor-mapping.md
│   ├── hitl-request-response.md
│   ├── checkpoint-store.md
│   └── cost-telemetry-capture.md
└── tasks.md             # Phase 2 — created by /speckit-tasks (NOT here)
```

### Source Code (repository root — existing projects, mapped to changes)

```text
src/
├── DBAIAzure.Core/            # IStructuredCompletionService, RouteDecision, NodeConfig models
│   └── (re-express IStructuredCompletionService atop IChatClient; add AI provider config records)
├── DBAIAzure.Connectors/      # LLM layer
│   ├── (retire AnthropicChatCompletionService / HotReloadAnthropicService — SK IChatCompletionService)
│   ├── AnthropicChatClientProvider   # official Anthropic SDK → IChatClient  (D5)
│   ├── HotReloadChatClient            # DelegatingChatClient: per-call provider/model reselect (D6)
│   └── IChatClientProvider registry   # BYO-AI factories keyed by config (D6)
├── DBAIAzure.Processes/       # ← heaviest change: SK Process Framework → MAF Workflows
│   ├── Steps/  → Executors/            # 17 KernelProcessStep → MAF Executor (D1)
│   ├── IntakePipelineBuilder / PhaseHandlerPipelineBuilder / WorkflowRuntimeBuilder → WorkflowBuilder (D1)
│   ├── HitlExternalChannel/ApprovalExternalChannel → removed; RequestPort nodes (D2)
│   └── Pipeline/*Orchestrator          # drive InProcessExecution.Run/ResumeStreamingAsync + RequestInfoEvent
├── DBAIAzure.Storage/         # persistence
│   └── EfCheckpointStore : ICheckpointStore<JsonElement>   # DB-backed MAF checkpoints (D3)
│       + one-time SK-paused-run → checkpoint migration (D4)
├── DBAIAzure.Web/             # DI/composition, filters, orchestrator factories, Run Detail UI
│   ├── (remove SKEXP0080 pragma + 4 kernel factories; build IChatClient pipelines)
│   ├── CostCapturingChatClient : DelegatingChatClient   # replaces the 2 SK filters (D8)
│   ├── OpenTelemetry .UseOpenTelemetry(SourceName) + Azure Monitor source repoint (D9)
│   └── Run Detail Stream tab (preserve token streaming — FR-011a)
└── DBAIAzure.Runner/         # console host: build its workflow via WorkflowBuilder; RequestPort console loop

tests/
├── DBAIAzure.Tests/          # unit + bUnit — update SK-typed assertions to MAF equivalents; add parity tests
└── DBAIAzure.E2ETests/       # Playwright — Review Queue, Run Detail stream, per-page flows unchanged
```

**Structure Decision**: No new projects. The migration is re-homed within the existing library
boundaries — orchestration in `DBAIAzure.Processes`, the LLM/provider seam in `DBAIAzure.Connectors`
+ `DBAIAzure.Core`, durable checkpoints in `DBAIAzure.Storage`, and DI/telemetry/UI in `DBAIAzure.Web`.
This keeps the diff aligned with the current architecture (constitution Article VII — extend the seams,
don't add parallel structure) and makes the atomic cutover a coherent single release.

## Phased approach (development sequencing — production switch stays atomic, D10)

1. **Foundation (no behavior change)**: add MAF/M.E.AI packages; build the `IChatClient` provider seam
   (D5/D6), `CostCapturingChatClient` (D8), and OTel repoint (D9) behind the existing SK pipelines;
   prove token/cost parity at the model layer first.
2. **Executor + workflow port**: convert the 17 steps to executors and the 3 builders to `WorkflowBuilder`
   graphs (D1), including `AddSwitch` port-label routing; keep parity tests green per pipeline.
3. **HITL + checkpoints**: `RequestPort` for all three surfaces (D2) + EF `ICheckpointStore` (D3) +
   rehydration; prove restart-resume.
4. **Paused-run migration**: implement + verify the one-time SK→MAF checkpoint converter (D4).
5. **Cutover**: remove all `Microsoft.SemanticKernel*` packages and the `SKEXP0080` pragma; single
   release; run the full suite + performance budget + paused-run migration as gates.

## Complexity Tracking

*No Constitution violations requiring justification.* The only governance action is the required
Article VII text update (FR-017), which is a documentation change, not added implementation complexity.
