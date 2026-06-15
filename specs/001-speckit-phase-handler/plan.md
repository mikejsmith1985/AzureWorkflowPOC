# Implementation Plan: Spec Kit Phase Handler

**Branch**: `feature/speckit-phase-handler` | **Date**: 2026-06-15 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/001-speckit-phase-handler/spec.md`

## Summary

Add a second Semantic Kernel Process Framework pipeline that turns Spec-Driven Development phase
completions into reviewed, human-approved Azure DevOps Boards work items. Forge Terminal POSTs a
phase-complete signal; the process reads the feature's `specs/NNN-feature-name/` artifacts, produces a
schema-bound validation summary + flagged gaps via Claude, pushes that summary + gaps (with a portal
link) to the Forge decision card, pauses for an explicit approve/reject decision delivered back from
the card, and only then creates/updates a work item
(Specify→Epic, Plan→Task-per-unit, Implement→Bug), linked under the feature's Epic. Repeat signals
upsert non-destructively (fields refreshed + summary appended as a discussion comment). The existing
support-ticket pipeline is untouched.

The technical approach reuses SK PF primitives end-to-end (typed events, `IExternalKernelProcessMessageChannel`
HITL, kernel DI) and the existing webhook/orchestrator/persistence patterns; the only net-new
infrastructure is the Azure DevOps Boards client (a genuine external-system gap) and artifact file I/O.

## Technical Context

**Language/Version**: C# / .NET 8 (`net8.0`, SDK pinned via `global.json`)

**Primary Dependencies**: Semantic Kernel + SK Process Framework; existing `AnthropicChatCompletionService`
(raw Anthropic Messages API over `HttpClient`); **new:** `Microsoft.TeamFoundationServer.Client`
`20.256.2` (Azure DevOps Work Item Tracking, `netstandard2.0` asset consumed by net8.0)

**Storage**: SQLite via EF Core 8 (`PipelineDbContext`); new `PhaseRunRecord` entity (+ unique index on
`(FeatureKey, Phase)`)

**Testing**: xUnit via `dotnet test`; unit (mocked `IBoardsClient`, fake chat service), integration
(real `WorkItemTrackingHttpClient` against a test project — gated on PAT presence)

**Target Platform**: ASP.NET Core web host (Blazor Server + controllers), Windows/Linux

**Project Type**: Web service + background SK process (single solution, multiple class libraries)

**Performance Goals**: Work item appears within 30s of approval (SC-006); validation is a single
non-streaming structured LLM call

**Constraints**: No autonomous board writes (FR-006); non-destructive upsert (FR-013/FR-018); existing
ticket pipeline behavior preserved (FR-017); secrets resolved from configuration only (Article IX);
artifact read bounded by configurable limits (defaults: `SpecKit:MaxArtifactBytes` = 65536 per file,
`SpecKit:MaxArtifactFiles` = 12 per phase) to protect the LLM call

**Scale/Scope**: POC — one Azure DevOps org/project; three supported phases; single-host in-process
orchestration

## Constitution Check

*GATE: evaluated before Phase 0 and re-checked after Phase 1 design.*

| Article | Gate | Status |
|---|---|---|
| I — Prime Directive (best route) | Reuse framework, isolate the one real integration, full TDD | ✅ |
| II — Process Protection | No wildcard `dotnet` kills; target PIDs only | ✅ N/A at design time |
| III — Branching | On `feature/speckit-phase-handler` (not main) | ✅ |
| IV — Code Quality | Records immutable, `Async`+`CancellationToken`, nullable on, doc comments, <40-line methods | ✅ planned |
| V — Testing (3-layer) | Unit (mocked `IBoardsClient`/chat), integration (real Boards/SK), Red→Green→Refactor | ✅ planned |
| VI — Documentation | CHANGELOG updated at implementation; specs/ artifacts exempt | ✅ planned |
| **VII — Framework-First** | SK PF for orchestration/HITL/DI; Anthropic native tool-use for structured output; **custom only** for Azure DevOps Boards (documented gap) + file I/O | ✅ **PASS** (see research.md §1) |
| VIII — Release | No release in scope | ✅ N/A |
| IX — Secrets | PAT, API key, webhook secret all from `IConfiguration`; never logged | ✅ planned |
| X — Verification | quickstart.md defines evidence-based end-to-end proof, not "200 OK" | ✅ planned |
| XI — Output Restraint | No scratch docs; specs/ tree only | ✅ |

**Initial gate: PASS.** **Post-design re-check: PASS** — the design introduces no bespoke state
machine, event bus, pause/resume loop, serialization layer, or DI registry; the two custom pieces
(Boards client, artifact reader) are recorded drift justifications in research.md §1. No entries
required in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/001-speckit-phase-handler/
├── plan.md              # This file
├── spec.md              # Feature specification (+ Clarifications)
├── research.md          # Phase 0: framework-first analysis + verified API facts
├── data-model.md        # Phase 1: entities, state machine, persistence
├── quickstart.md        # Phase 1: runnable end-to-end validation
├── contracts/           # Phase 1: HTTP endpoints, IBoardsClient, validation tool schema
│   ├── http-endpoints.md
│   ├── iboards-client.md
│   └── validation-tool-schema.json
└── checklists/
    └── requirements.md
```

### Source Code (repository root)

```text
src/
├── DBAIAzure.Core/
│   ├── Models/
│   │   ├── SpecKitPhase.cs              # enum + PhaseWorkItemMap
│   │   ├── PhaseHandlerState.cs         # process state record (+ PhaseArtifact, PlannedWorkItem,
│   │   │                                #   ApprovalDecision, CreatedWorkItemRef, PhaseRunStatus)
│   │   └── PhaseValidationResult.cs     # structured LLM output (+ PhaseValidationGap)
│   └── Interfaces/
│       ├── IBoardsClient.cs             # Azure DevOps Boards seam
│       ├── IArtifactReader.cs           # reads specs/NNN dir → PhaseArtifact[]
│       ├── IPhaseRunRepository.cs       # phase-run persistence seam
│       └── IPhaseApprovalNotifier.cs    # outbound: push summary+gaps+link to the decision card
│
├── DBAIAzure.Connectors/
│   └── AnthropicChatCompletionService.cs  # + GetStructuredAsync<T>(...) non-streaming tool-use
│
├── DBAIAzure.Processes/
│   ├── PhaseHandlerEvents.cs            # typed event name constants
│   ├── PhaseHandlerPipelineBuilder.cs   # ProcessBuilder graph + approval proxy step
│   ├── ApprovalExternalChannel.cs       # IExternalKernelProcessMessageChannel (approve/reject)
│   ├── Steps/
│   │   ├── ReadArtifactsStep.cs
│   │   ├── PhaseValidationStep.cs        # structured LLM call
│   │   ├── ApprovalPauseStep.cs          # emits AwaitApproval (external)
│   │   └── CreateWorkItemStep.cs         # create/upsert via IBoardsClient (post-approval only)
│   └── Pipeline/
│       └── PhaseHandlerOrchestrator.cs   # background run loop + event stream + approval gate
│
├── DBAIAzure.Storage/
│   ├── Entities/PhaseRunRecord.cs
│   ├── PipelineDbContext.cs              # + DbSet<PhaseRunRecord>, unique (FeatureKey, Phase)
│   └── Repositories/                     # phase-run upsert/lookup (IPhaseRunRepository)
│
└── DBAIAzure.Web/
    ├── Controllers/SpecKitWebhookController.cs   # /speckit-phase + /speckit-approval
    ├── Integrations/SpecKit/                     # PhaseSignalPayload, ApprovalDecisionPayload, mapper,
    │                                             #   ForgeApprovalNotifier : IPhaseApprovalNotifier
    ├── Integrations/AzureDevOps/                 # AzureDevOpsBoardsClient : IBoardsClient, WorkItemMapper
    ├── Services/FileSystemArtifactReader.cs      # IArtifactReader impl (repo specs/ root)
    └── Program.cs                                # DI: orchestrator, IBoardsClient, IArtifactReader, options

tests/DBAIAzure.Tests/
├── PhaseWorkItemMapTests.cs            # phase → work item type mapping
├── PhaseValidationResultTests.cs       # structured-output binding (tool_use input → record)
├── ReadArtifactsStepTests.cs           # missing/empty dir → Failed
├── PhaseValidationStepTests.cs         # fake chat service → typed result
├── ApprovalPauseStepTests.cs           # emits AwaitApproval; no write before decision
├── CreateWorkItemStepTests.cs          # fake IBoardsClient: create vs upsert, parent link, reject
├── PhaseHandlerOrchestratorTests.cs    # state transitions, idempotent upsert, gate enforcement
├── WorkItemMapperTests.cs              # phase → fields/title/description
├── SpecKitWebhookControllerTests.cs    # auth, 202/400/401/404/409
└── AzureDevOpsBoardsClientTests.cs     # [integration] real Boards round-trip (skipped without PAT)
```

**Structure Decision**: Single solution, existing layered libraries
(`Core` → `Connectors`/`Storage` → `Processes` → `Web`). The phase handler is a **parallel track**:
new files alongside the ticket pipeline, no modification to `IntakePipelineBuilder`,
`PipelineOrchestrator`, or `TicketState` (FR-017). Domain types and the two seams live in `Core`; SK
steps/process/orchestrator in `Processes`; the Azure DevOps SDK and HTTP surface stay in `Web`.

## Complexity Tracking

No constitution violations to justify — the Framework-First gate passed with only documented,
external-system drift (Azure DevOps Boards client, artifact reader). Table intentionally empty.
