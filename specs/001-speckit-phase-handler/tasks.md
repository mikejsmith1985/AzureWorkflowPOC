---
description: "Task list for Spec Kit Phase Handler implementation"
---

# Tasks: Spec Kit Phase Handler

## Status (reconciled 2026-08-31)

**Shipped.** The only open item is **T048**, a `quickstart.md` Scenarios A–C run — verification, not
development. Nothing here is waiting on code.

---

**Input**: Design documents from `specs/001-speckit-phase-handler/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: INCLUDED — the constitution (Article V) mandates TDD (Red → Green → Refactor). Test tasks
are written before their implementation and must fail first.

**Organization**: Tasks are grouped by user story. Each story is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1 / US2 / US3 (setup, foundational, polish carry no story label)
- All paths are repo-relative.

---

## Phase 1: Setup (Shared Infrastructure)

- [X] T001 Add `Microsoft.TeamFoundationServer.Client` `20.256.2` PackageReference to `src/DBAIAzure.Web/DBAIAzure.Web.csproj`
- [X] T002 [P] Add configuration keys to `src/DBAIAzure.Web/appsettings.json`: `AzureDevOps:OrganizationUrl`, `AzureDevOps:Project`, `AzureDevOps:AreaPath`, `AzureDevOps:IterationPath`, `SpecKit:SpecsRoot`, `SpecKit:MaxArtifactBytes` (65536), `SpecKit:MaxArtifactFiles` (12), `SpecKit:DecisionCardUrl`. Secrets (`AzureDevOps:Pat`, `WebhookSecrets:SpecKit`) get a `REPLACE_*` placeholder here and the real values live only in gitignored `appsettings.Development.json` (Article IX)
- [X] T003 [P] Verify solution builds with the new package on the user-local SDK: `dotnet build DBAIAzure.sln`

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: No user story work begins until this phase is complete. These are the shared domain
types, seams, persistence, and SK plumbing every story depends on.

- [X] T004 [P] Create `SpecKitPhase` enum + `PhaseWorkItemMap` (phase → "Epic"/"Task"/"Bug"/unsupported) in `src/DBAIAzure.Core/Models/SpecKitPhase.cs`
- [X] T005 [P] Create `PhaseValidationResult` + `PhaseValidationGap` records (snake_case `[JsonPropertyName]`, `required`) in `src/DBAIAzure.Core/Models/PhaseValidationResult.cs`
- [X] T006 [P] Create `PhaseHandlerState` plus `PhaseArtifact`, `PlannedWorkItem`, `ApprovalDecision`, `CreatedWorkItemRef`, `PhaseRunStatus` in `src/DBAIAzure.Core/Models/PhaseHandlerState.cs`
- [X] T007 [P] Create `IBoardsClient` interface (per contracts/iboards-client.md) in `src/DBAIAzure.Core/Interfaces/IBoardsClient.cs`
- [X] T008 [P] Create `IArtifactReader` interface (resolve feature dir → `PhaseArtifact[]`) in `src/DBAIAzure.Core/Interfaces/IArtifactReader.cs`
- [X] T009 [P] Create `IPhaseRunRepository` interface (upsert/lookup by RunId and by (FeatureKey, Phase)) in `src/DBAIAzure.Core/Interfaces/IPhaseRunRepository.cs`
- [X] T010 Create `PhaseRunRecord` entity in `src/DBAIAzure.Storage/Entities/PhaseRunRecord.cs`
- [X] T011 Add `DbSet<PhaseRunRecord>` + unique index on `(FeatureKey, Phase)` + indexes in `src/DBAIAzure.Storage/PipelineDbContext.cs` (depends on T010)
- [X] T012 Implement `SqlitePhaseRunRepository` (uses `IDbContextFactory`, singleton-safe like `SqliteRunRepository`) in `src/DBAIAzure.Storage/Repositories/SqlitePhaseRunRepository.cs` (depends on T009, T011)
- [X] T013 [P] Create `PhaseHandlerEvents` typed event-name constants (ArtifactsRead, Validated, AwaitApproval, ApprovalDecided, WorkItemWritten, Unsupported, Failed) in `src/DBAIAzure.Processes/PhaseHandlerEvents.cs`
- [X] T014 [P] Create `ApprovalExternalChannel : IExternalKernelProcessMessageChannel` (mirrors `HitlExternalChannel`; pauses on AwaitApproval) in `src/DBAIAzure.Processes/ApprovalExternalChannel.cs`
- [X] T015 [P] Write FAILING unit test for tool-use structured parsing (tool_use block `input` → typed record) in `tests/DBAIAzure.Tests/AnthropicStructuredOutputTests.cs`
- [X] T016 Add `GetStructuredAsync<T>(ChatHistory, toolName, inputSchema, ct)` (non-streaming, `tools` + `tool_choice`) and extend wire records with `id`/`name`/`JsonElement? input` in `src/DBAIAzure.Connectors/AnthropicChatCompletionService.cs` (makes T015 pass)
- [X] T050 [P] Create `IPhaseApprovalNotifier` interface (push approval card: run id, feature, phase, summary, gaps, portal link) in `src/DBAIAzure.Core/Interfaces/IPhaseApprovalNotifier.cs`

**Checkpoint**: Shared foundation ready — user stories can begin.

---

## Phase 3: User Story 1 — Review a completed phase and approve its work item (Priority: P1) 🎯 MVP

**Goal**: End-to-end loop for the Specify phase: signal → read artifacts → structured validation →
HITL pause → approve/reject → on approval create an **Epic**; nothing written before approval.

**Independent Test**: POST `phase: "specify"` → see summary + gaps, board empty → approve → Epic
appears; reject → no work item (quickstart.md Scenario A).

### Tests for User Story 1 (write first, ensure they FAIL) ⚠️

- [X] T017 [P] [US1] Failing unit tests for `ReadArtifactsStep` (reads files; missing/empty dir → `Failed` with reason) in `tests/DBAIAzure.Tests/ReadArtifactsStepTests.cs`
- [X] T018 [P] [US1] Failing unit tests for `PhaseValidationStep` (fake chat service → typed `PhaseValidationResult`) in `tests/DBAIAzure.Tests/PhaseValidationStepTests.cs`
- [X] T019 [P] [US1] Failing unit tests for `CreateWorkItemStep` Epic creation + **no write unless approved** (fake `IBoardsClient`) in `tests/DBAIAzure.Tests/CreateWorkItemStepTests.cs`
- [X] T020 [P] [US1] Failing unit tests for `PhaseHandlerOrchestrator` gate + reject path (no work item before approval; reject → `Rejected`; **board-write failure after approval → `Failed` with the approval preserved and the failure recorded, FR-015**) in `tests/DBAIAzure.Tests/PhaseHandlerOrchestratorTests.cs`
- [X] T021 [P] [US1] Failing unit tests for `SpecKitWebhookController` (secret 401, missing field 400, signal 202; approval 200/404/409) in `tests/DBAIAzure.Tests/SpecKitWebhookControllerTests.cs`
- [X] T052 [P] [US1] Failing unit test: orchestrator invokes `IPhaseApprovalNotifier` with summary + gaps + portal link when the run pauses (fake notifier) in `tests/DBAIAzure.Tests/PhaseApprovalNotifierTests.cs`

### Implementation for User Story 1

- [X] T022 [P] [US1] Implement `FileSystemArtifactReader : IArtifactReader` (resolve `SpecKit:SpecsRoot` + feature dir; bounded read honoring `SpecKit:MaxArtifactBytes`/`SpecKit:MaxArtifactFiles`) in `src/DBAIAzure.Web/Services/FileSystemArtifactReader.cs`
- [X] T023 [P] [US1] Implement `WorkItemMapper` for Specify→Epic title/description from artifacts in `src/DBAIAzure.Web/Integrations/AzureDevOps/WorkItemMapper.cs`
- [X] T024 [US1] Implement `AzureDevOpsBoardsClient : IBoardsClient` — `VssConnection`/PAT, `CreateWorkItemAsync(Epic)`, `AppendDiscussionCommentAsync` (System.History) in `src/DBAIAzure.Web/Integrations/AzureDevOps/AzureDevOpsBoardsClient.cs`
- [X] T025 [US1] Implement `ReadArtifactsStep` (emits ArtifactsRead / Failed) in `src/DBAIAzure.Processes/Steps/ReadArtifactsStep.cs`
- [X] T026 [US1] Implement `PhaseValidationStep` using `GetStructuredAsync<PhaseValidationResult>` + the contracts/validation-tool-schema.json in `src/DBAIAzure.Processes/Steps/PhaseValidationStep.cs`
- [X] T027 [US1] Implement `ApprovalPauseStep` (emits AwaitApproval external event) in `src/DBAIAzure.Processes/Steps/ApprovalPauseStep.cs`
- [X] T028 [US1] Implement `CreateWorkItemStep` — Epic create via `IBoardsClient`, only on approved decision; on board-write failure emit Failed with the reason (approval preserved, FR-015) in `src/DBAIAzure.Processes/Steps/CreateWorkItemStep.cs`
- [X] T029 [US1] Implement `PhaseHandlerPipelineBuilder` (wire steps + approval proxy step) in `src/DBAIAzure.Processes/PhaseHandlerPipelineBuilder.cs`
- [X] T030 [US1] Implement `PhaseHandlerOrchestrator` (start run, pause on AwaitApproval, push summary + gaps + portal link via `IPhaseApprovalNotifier` on pause, resume on ApprovalDecided, persist via `IPhaseRunRepository`, expose update event) in `src/DBAIAzure.Processes/Pipeline/PhaseHandlerOrchestrator.cs`
- [X] T031 [P] [US1] Create `PhaseSignalPayload`, `ApprovalDecisionPayload`, and signal→state mapper in `src/DBAIAzure.Web/Integrations/SpecKit/`
- [X] T051 [US1] Implement `ForgeApprovalNotifier : IPhaseApprovalNotifier` (POST compact summary + gaps + portal link to `SpecKit:DecisionCardUrl`; fire-and-forget, failure-tolerant like `TeamsHitlNotifier`) in `src/DBAIAzure.Web/Integrations/SpecKit/ForgeApprovalNotifier.cs`
- [X] T032 [US1] Implement `SpecKitWebhookController` (`POST /api/webhook/speckit-phase`, `POST /api/webhook/speckit-approval`; shared-secret guard) in `src/DBAIAzure.Web/Controllers/SpecKitWebhookController.cs`
- [X] T033 [US1] Wire DI + options in `src/DBAIAzure.Web/Program.cs` (`IArtifactReader`, `IBoardsClient`, `IPhaseRunRepository`, `IPhaseApprovalNotifier` + its named `HttpClient`, `PhaseHandlerOrchestrator`, `AzureDevOps`/`SpecKit` options) without altering existing ticket-pipeline registrations

**Checkpoint**: MVP — Specify→Epic works end-to-end with HITL gate; reject path verified.

---

## Phase 4: User Story 2 — Correct work item type and content per phase (Priority: P2)

**Goal**: Plan → one Task per planned unit; Implement → Bug; unsupported phase → recorded, no write.

**Independent Test**: POST each phase, approve, verify Epic / Task-set / Bug created (quickstart.md Scenario B).

### Tests for User Story 2 (write first, ensure they FAIL) ⚠️

- [X] T034 [P] [US2] Failing tests for `PhaseWorkItemMap` + `WorkItemMapper` (Plan→Task, Implement→Bug, fields) in `tests/DBAIAzure.Tests/WorkItemMapperTests.cs`
- [X] T035 [P] [US2] Failing tests for planned-item parsing (plan/tasks artifact → `PlannedWorkItem[]`) in `tests/DBAIAzure.Tests/PlanArtifactParserTests.cs`
- [X] T036 [P] [US2] Failing tests extending `CreateWorkItemStepTests`: Plan creates one Task per item; Implement creates a Bug; unsupported phase creates nothing

### Implementation for User Story 2

- [X] T037 [US2] Implement `PlanArtifactParser` — parse `tasks.md` into `PlannedWorkItem[]` when present, else derive from `plan.md` structural sections in `src/DBAIAzure.Processes/PlanArtifactParser.cs`
- [X] T038 [US2] Extend `WorkItemMapper` with Plan and Implement field/title/description mapping in `src/DBAIAzure.Web/Integrations/AzureDevOps/WorkItemMapper.cs`
- [X] T039 [US2] Extend `CreateWorkItemStep`: Plan → loop create one Task per `PlannedWorkItem`; Implement → create Bug in `src/DBAIAzure.Processes/Steps/CreateWorkItemStep.cs`
- [X] T040 [US2] Handle `Unsupported` phase (record `Unsupported`, no write) in `PhaseHandlerOrchestrator` + controller in `src/DBAIAzure.Processes/Pipeline/PhaseHandlerOrchestrator.cs`

**Checkpoint**: All three supported phases produce the correct work item type.

---

## Phase 5: User Story 3 — Traceability and safe re-signaling (Priority: P3)

**Goal**: Plan/Implement items linked under the Epic (auto-create Epic if missing — no orphans);
repeat signal upserts non-destructively (fields refreshed + summary appended as a comment).

**Independent Test**: Build Epic, then Plan/Implement link to it; re-send a signal → no duplicate,
existing item updated, prior content intact (quickstart.md Scenario C).

### Tests for User Story 3 (write first, ensure they FAIL) ⚠️

- [X] T041 [P] [US3] Failing tests: Plan/Implement linked under Epic; Epic auto-created when missing (fake `IBoardsClient` + `IPhaseRunRepository`) in `tests/DBAIAzure.Tests/HierarchyLinkingTests.cs`
- [X] T042 [P] [US3] Failing tests: repeat (feature, phase) signal → upsert (fields refreshed + comment appended), zero duplicates in `tests/DBAIAzure.Tests/IdempotentUpsertTests.cs`

### Implementation for User Story 3

- [X] T043 [US3] Extend `AzureDevOpsBoardsClient`: `UpsertWorkItemAsync` (System.History append) + parent link via `System.LinkTypes.Hierarchy-Reverse` in `src/DBAIAzure.Web/Integrations/AzureDevOps/AzureDevOpsBoardsClient.cs` (already present from the MVP; verified correct per research.md §2)
- [X] T044 [US3] Auto-create-Epic-if-missing + link children in `CreateWorkItemStep` (look up Epic id via `IPhaseRunRepository`) in `src/DBAIAzure.Processes/Steps/CreateWorkItemStep.cs`
- [X] T045 [US3] Idempotent upsert path in `CreateWorkItemStep`/orchestrator using stored `CreatedWorkItemRef` keyed by (FeatureKey, Phase) in `src/DBAIAzure.Processes/Steps/CreateWorkItemStep.cs` + `src/DBAIAzure.Storage/Repositories/SqlitePhaseRunRepository.cs`

**Checkpoint**: Hierarchy linking + non-destructive upsert complete.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T046 [P] Integration test `AzureDevOpsBoardsClientTests` — real Boards round-trip, skipped via `[Fact(Skip)]`/trait when no PAT in env, in `tests/DBAIAzure.Tests/AzureDevOpsBoardsClientTests.cs`
- [X] T047 [P] Update `CHANGELOG.md` `[Unreleased]` with the Spec Kit phase handler feature
- [ ] T048 Run `quickstart.md` Scenarios A–C + edge cases; confirm existing ticket-pipeline tests stay green (FR-017 / SC-007): `dotnet test DBAIAzure.sln` — **requires real Azure DevOps PAT + Anthropic key in `appsettings.Development.json`; deferred to the user**
- [X] T049 [P] Article IV audit pass over new files (XML doc comments, naming, <40-line methods, nullable, `Async`+CancellationToken) — reviewed; no violations found

---

## Dependencies & Execution Order

### Phase dependencies
- **Setup (P1)** → no deps.
- **Foundational (P2)** → after Setup; **blocks all stories**.
- **US1 (P3)** → after Foundational. **US2 (P4)** and **US3 (P5)** → after US1 (they extend
  `CreateWorkItemStep` / `AzureDevOpsBoardsClient` / orchestrator created in US1).
- **Polish (P6)** → after the stories you intend to ship.

### Within foundational
- T010 → T011 → T012 (entity → context → repository).
- T015 (failing test) → T016 (implementation).

### Within each story
- Tests (T017–T021, T052, T034–T036, T041–T042) are written and failing **before** their implementation.
- Foundational `T050` (`IPhaseApprovalNotifier`) must exist before `T030`/`T051` (orchestrator push + notifier impl).
- Models/readers/mappers before steps; steps before pipeline builder; builder before orchestrator
  completion; orchestrator + controller before DI wiring.

### Cross-story file note
US2 and US3 both edit `CreateWorkItemStep.cs`, `AzureDevOpsBoardsClient.cs`, and
`PhaseHandlerOrchestrator.cs` — do **not** parallelize US2 and US3 on those files; complete US2 first.

## Parallel Opportunities

- Setup: T002, T003 in parallel.
- Foundational: T004–T009 (all [P], distinct Core files) and T013, T014, T015 in parallel; then the
  T010→T011→T012 chain and T016.
- US1 tests T017–T021 in parallel; then [P] impl files T022, T023, T031 in parallel before the
  sequential step/builder/orchestrator chain (T024–T030, T032, T033).

```bash
# Foundational domain types — all parallel (different files):
Task: "Create SpecKitPhase enum + PhaseWorkItemMap in src/DBAIAzure.Core/Models/SpecKitPhase.cs"
Task: "Create PhaseValidationResult in src/DBAIAzure.Core/Models/PhaseValidationResult.cs"
Task: "Create PhaseHandlerState in src/DBAIAzure.Core/Models/PhaseHandlerState.cs"
Task: "Create IBoardsClient in src/DBAIAzure.Core/Interfaces/IBoardsClient.cs"
Task: "Create IArtifactReader in src/DBAIAzure.Core/Interfaces/IArtifactReader.cs"
Task: "Create IPhaseRunRepository in src/DBAIAzure.Core/Interfaces/IPhaseRunRepository.cs"
```

## Implementation Strategy

- **MVP = Phase 1 + Phase 2 + Phase 3 (US1).** Stop and validate the Specify→Epic loop end-to-end
  (quickstart Scenario A) before continuing. This is a demonstrable increment on its own.
- **Incremental**: add US2 (all phase types) → validate; add US3 (linking + upsert) → validate.
- Each story keeps the existing ticket pipeline untouched (FR-017); run the regression filter at each
  checkpoint.

## Notes

- TDD is mandatory (Article V): confirm each test fails before implementing.
- Record the `tests-written` and `tests-passed` workflow gates (Phase 3 of workflow-enforcer) before
  committing.
- Commit after each task or logical group; keep secrets out of source (Article IX).
