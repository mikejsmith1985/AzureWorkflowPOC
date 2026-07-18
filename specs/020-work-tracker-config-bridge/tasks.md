---
description: "Task list for Work-Tracker Config Bridge"
---

# Tasks: Work-Tracker Config Bridge — Select & Configure Any Tracker (incl. Jira) from the UI

**Input**: Design documents from `specs/020-work-tracker-config-bridge/`

**Prerequisites**: plan.md, spec.md, research.md (D1–D8), data-model.md, contracts/ (4 seams), quickstart.md

**Tests**: INCLUDED — Constitution Article V mandates TDD (Red→Green→Refactor). Test tasks are written to
FAIL first, before their implementation task.

**Organization**: Grouped by user story (US1–US4 from spec.md) after shared foundational work.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US4; setup/foundational/polish carry no story label
- Exact file paths included

## Path notes

`.razor` UI = `src/DBAIAzure.Web/Pages` & `Components/`; adapters = `src/DBAIAzure.Web/Integrations/`;
seams/records = `src/DBAIAzure.Core/`; storage = `src/DBAIAzure.Storage/`; tests = `tests/DBAIAzure.Tests`
(unit + bUnit) and `tests/DBAIAzure.E2ETests` (Playwright).

---

## Phase 1: Setup

**Purpose**: Prepare the working tree and changelog.

- [X] T001 Add an "Unreleased" entry in `CHANGELOG.md` describing the Work Tracking System connector bridge (generic connector + Jira from UI)
- [X] T002 [P] Create the test folder `tests/DBAIAzure.Tests/WorkTracker/` for the new suites referenced below

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The generic connector identity, the per-run config resolver, and the in-place ADO migration —
without these, swapping the UI/pipeline onto the generic type would break the existing ADO tracker. **All
user stories depend on this phase.**

**⚠️ CRITICAL**: No user story work begins until this phase is complete and ADO still works under the generic identity.

### Tests (write first, must FAIL)

- [X] T003 [P] Unit test for `WorkTrackerConfigResolver` provider dispatch (ADO/Jira/unconfigured + seed fallback) in `tests/DBAIAzure.Tests/WorkTracker/WorkTrackerConfigResolverTests.cs`
- [ ] T004 [P] Unit test for one-time ADO→WorkTracker migration (transforms JSON, copies ciphertext, sets active) in `tests/DBAIAzure.Tests/WorkTracker/WorkTrackerMigrationTests.cs`

### Implementation

- [X] T005 [P] Add `WorkTracker` member to the enum (keep `AzureDevOps` for legacy-row parsing) in `src/DBAIAzure.Core/Models/ConnectorType.cs`
- [X] T006 [P] Create `ResolvedWorkTrackerConfig` record (Provider, NonSecretJson, DecryptedSecret, IsConfigured) in `src/DBAIAzure.Core/Models/ResolvedWorkTrackerConfig.cs`
- [X] T007 [P] Create `WorkTrackerProvider` discriminator (AzureDevOps | Jira) in `src/DBAIAzure.Core/Models/WorkTrackerProvider.cs`
- [X] T008 [P] Define `IWorkTrackerConfigResolver` (`ResolveActiveAsync`) in `src/DBAIAzure.Core/Interfaces/IWorkTrackerConfigResolver.cs`
- [X] T009 Implement `WorkTrackerConfigResolver` (reads the `WorkTracker` row via `IConnectorConfigRepository`, decrypts, dispatches on `provider`, `WorkTracker:Active` seed fallback) in `src/DBAIAzure.Web/Services/WorkTrackerConfigResolver.cs` — depends on T006–T008
- [ ] T010 Swap `AllConnectorTypes` from `AzureDevOps` to `WorkTracker` in `src/DBAIAzure.Storage/Repositories/SqliteConnectorConfigRepository.cs` — depends on T005
- [ ] T011 Rework `WorkTrackerAdapterProvider.GetAdapter()` to resolve the active adapter per run via `IWorkTrackerConfigResolver` (replace the startup `WorkTracker:Active` read) in `src/DBAIAzure.Web/Services/WorkTrackerAdapterProvider.cs` — depends on T009
- [ ] T012 Repoint `AzureDevOpsBoardsClient` config source to the `WorkTracker` row (provider=AzureDevOps) through the resolver, preserving the appsettings baseline overlay in `src/DBAIAzure.Web/Integrations/AzureDevOps/AzureDevOpsBoardsClient.cs` — depends on T009
- [ ] T013 Implement the one-time idempotent ADO→WorkTracker startup migration (guarded by absence of a WorkTracker row; copies encrypted blob verbatim) immediately after the DB-init block in `src/DBAIAzure.Web/Program.cs` — depends on T005, T010. **Ordering**: must run at boot BEFORE any adapter/BoardsClient use so T012's WorkTracker-row read resolves for existing installs
- [ ] T014 Register `IWorkTrackerConfigResolver` and adjust the work-tracker DI registrations for per-run resolution in `src/DBAIAzure.Web/Program.cs` — depends on T009
- [ ] T015 Add the generic **Work Tracking System** card shell — provider `<select>`, `WorkTracker` load/save switch arms, and the **Azure DevOps** sub-form (org URL / project / PAT) moved under it — in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor` — depends on T005
- [ ] T015a **[Edge case — spec "no tracker configured"]** Render a clear unconfigured empty-state on the Work Tracking System card when `IsConfigured = false` (no provider chosen / no row), with a "select a provider to begin" prompt, in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor` — depends on T015
- [ ] T016 Extend the `ConnectorEntry` draft model with `DraftProvider`, `DraftEmail`, `DraftProjectKey` in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor` — depends on T015

**Checkpoint**: The Connectors screen shows one generic card; an existing ADO connector auto-migrates and the ADO pipeline path behaves exactly as before (T003/T004 green).

---

## Phase 3: User Story 1 - Configure Jira from the UI and run a ticket end-to-end (Priority: P1) 🎯 MVP

**Goal**: An operator selects Jira, enters credentials in the UI, saves, and a run creates the issue with the binding key + cost fields — no env/file edits.

**Independent Test**: With no `WorkTracker:Jira` config anywhere, configure Jira via the UI and run a demo ticket; observe the created Jira issue, binding key, and cost fields on the real instance.

### Tests (write first, must FAIL)

- [ ] T017 [P] [US1] Unit test `JiraConnectionFactory` rebuilds the authed client only on `siteUrl|email|apiToken` change and sets Basic auth + BaseAddress, in `tests/DBAIAzure.Tests/WorkTracker/JiraConnectionFactoryTests.cs`
- [ ] T018 [P] [US1] Unit test parsing Jira config from the discriminated `WorkTracker` JSON (`provider=Jira` → SiteUrl/Email/ProjectKey + secret) in `tests/DBAIAzure.Tests/WorkTracker/JiraConnectorConfigParseTests.cs`
- [ ] T019 [P] [US1] bUnit test: the generic card renders the Jira sub-form (site/email/token/project key) when provider=Jira, **and asserts the API-token field is never pre-filled on reload of a saved connector (SC-005/FR-006 — secret never redisplayed)**, in `tests/DBAIAzure.Tests/WorkTracker/ConnectorSettingsJiraFormTests.cs`

### Implementation

- [X] T020 [US1] Create `JiraConnectorConfig` non-secret record (SiteUrl, Email, ProjectKey) in `src/DBAIAzure.Core/Models/JiraConnectorConfig.cs`
- [ ] T021 [US1] Implement `IJiraConnectionFactory` + `JiraConnectionFactory` (per-run resolve via `IWorkTrackerConfigResolver`; cache authed `HttpClient` keyed by `siteUrl|email|apiToken`) in `src/DBAIAzure.Web/Integrations/Jira/JiraConnectionFactory.cs` — depends on T009, T020
- [ ] T022 [US1] Change `JiraWorkTrackerAdapter` to take `IJiraConnectionFactory` and obtain its client per operation (remove the injected pre-authed client + `JiraOptions` dependency) in `src/DBAIAzure.Web/Integrations/Jira/JiraWorkTrackerAdapter.cs` — depends on T021
- [ ] T023 [US1] Remove the startup-baked named `"Jira"` HttpClient auth and register `IJiraConnectionFactory` + the factory-based adapter in `src/DBAIAzure.Web/Program.cs` — depends on T021, T022
- [ ] T024 [US1] Add the Jira provider sub-form (site URL / email / API token / project key + InfoTips) and the `WorkTracker` `LoadDraftFromJson`/`SerializeToJson` Jira arms in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor` — depends on T015, T016. **The API-token input MUST NOT be pre-populated from stored secrets on load (SC-005/FR-006), matching the existing ADO PAT behavior**
- [ ] T025 [US1] Route field provisioning through the active adapter (`IWorkTrackerAdapterProvider.GetAdapter().ProvisionFieldsAsync`, so provider=Jira uses `JiraFieldProvisioner`) in the startup/provision path in `src/DBAIAzure.Web/Program.cs` — depends on T011

**Checkpoint**: Jira is fully configurable from the UI and a run lands on a real Jira issue (US1 independently testable — MVP).

---

## Phase 4: User Story 2 - Choose and switch the active tracker generically (Priority: P2)

**Goal**: The tracker is presented generically with a provider selector; switching providers takes effect on the next run without a restart.

**Independent Test**: With ADO active, switch to Jira and save; next run targets Jira. Switch back; next run targets ADO — no restart, no file edit.

### Tests (write first, must FAIL)

- [ ] T026 [P] [US2] Integration test: after saving a provider change, `IWorkTrackerAdapterProvider.GetAdapter()` returns the newly selected adapter on the next call (no restart) in `tests/DBAIAzure.Tests/WorkTracker/ProviderSwitchTests.cs`

### Implementation

- [ ] T027 [US2] Wire the provider `<select>` to switch sub-forms, persist the `provider` into the `WorkTracker` JSON on save, and clearly indicate the active provider on the card in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor` — depends on T015, T024
- [ ] T028 [P] [US2] Generify the onboarding chip label + deep-link (target `WorkTracker`) in `src/DBAIAzure.Web/Components/Settings/OnboardingBanner.razor`
- [ ] T029 [P] [US2] Generify the work-tracker help copy in `src/DBAIAzure.Web/Pages/UserGuide.razor`
- [ ] T030 [US2] Sweep remaining hardcoded "Azure DevOps" user-facing strings so the displayed provider reflects the active selection (SC-006) across `src/DBAIAzure.Web`

**Checkpoint**: One generic card; live ADO↔Jira switching without restart (US1 + US2 both independently testable).

---

## Phase 5: User Story 3 - Verify a connection before relying on it (Priority: P3)

**Goal**: "Test Connection" for the selected provider (incl. Jira) reports an accurate, actionable pass/fail.

**Independent Test**: Valid Jira creds → Test passes in seconds; invalid token → "token invalid or expired"; bad project key → "project key not found or no access". No issue created.

### Tests (write first, must FAIL)

- [ ] T031 [P] [US3] Unit test `JiraConnectorTester`: success (auth + project), invalid-token failure, bad-project failure, and no-write guarantee in `tests/DBAIAzure.Tests/WorkTracker/JiraConnectorTesterTests.cs`

### Implementation

- [ ] T032 [US3] Implement `JiraConnectorTester : IConnectorHealthChecker` (probe `GET /rest/api/3/myself` then `GET /rest/api/3/project/{key}`, return `ConnectorTestResult` with sanitized messages) in `src/DBAIAzure.Web/Integrations/Jira/JiraConnectorTester.cs` — depends on T021
- [ ] T033 [US3] Wire the "Test Connection" button to resolve the tester for the selected provider and persist via `UpdateTestResultAsync(ConnectorType.WorkTracker, ...)` in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor` — depends on T032, T015
- [ ] T034 [US3] Register `JiraConnectorTester` for the health-checker seam in `src/DBAIAzure.Web/Program.cs` — depends on T032

**Checkpoint**: Both providers have an accurate Test Connection (US1–US3 independently testable).

---

## Phase 6: User Story 4 - Existing Azure DevOps deployments are unaffected (Priority: P4)

**Goal**: Prove upgrade requires zero reconfiguration and no regression — the migration (built in Phase 2) verified end-to-end.

**Independent Test**: Start from a DB with an `AzureDevOps` row; launch the new build; the generic card shows provider=Azure DevOps with existing org/project (secret preserved) and a run behaves as before; a second restart is a no-op.

### Tests (write first, must FAIL)

- [ ] T035 [P] [US4] Test migration replay idempotency (second run = no-op) and fresh-install path (no ADO row → no migration) in `tests/DBAIAzure.Tests/WorkTracker/WorkTrackerMigrationReplayTests.cs`

### Implementation

- [ ] T036 [US4] Update test fixtures/setups that seed `ConnectorType.AzureDevOps` config to seed the `WorkTracker` row (provider=AzureDevOps) so the ADO regression suite exercises the generic identity, across `tests/DBAIAzure.Tests` — depends on T005, T010
- [ ] T037 [US4] Run the full `dotnet test` ADO regression suite and confirm green with no expectation changes beyond the generic presentation (SC-003)

**Checkpoint**: Existing ADO deployments verified unaffected; migration idempotent.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T038 [P] Playwright E2E: Connectors tab renders the provider select, provider-conditional fields, Test Connection for both providers, and a save/reload round-trip in `tests/DBAIAzure.E2ETests/`
- [ ] T039 [P] Finalize the `CHANGELOG.md` entry with the shipped behavior (generic connector, Jira from UI, live switch, ADO auto-migration)
- [ ] T040 Run all `quickstart.md` scenarios (1–6) as the acceptance gate
- [ ] T041 [P] Code-quality pass: XML doc comments on all new public types, 40-line/guard-clause review across the new resolver/factory/tester/migration files

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)**: no dependencies.
- **Foundational (P2)**: after Setup — **BLOCKS all user stories** (generic identity + resolver + migration; ADO must still work).
- **US1 (P3)**: after Foundational. MVP.
- **US2 (P4)**: after Foundational; builds on the US1 card work (T024) for the provider switch.
- **US3 (P5)**: after Foundational; the tester depends on the Jira factory (T021 in US1).
- **US4 (P6)**: after Foundational; verifies the Phase-2 migration + ADO regression. Independent of US1–US3.
- **Polish (P7)**: after the targeted stories are complete.

### Cross-story notes

- US3's `JiraConnectorTester` (T032) reuses `IJiraConnectionFactory` (T021) — sequence US1 before US3.
- US4 is otherwise parallelizable with US1–US3 (it exercises the ADO side + migration built in Phase 2).

### Parallel opportunities

- Setup: T002 ∥ T001.
- Foundational tests T003 ∥ T004; core records/interfaces T005 ∥ T006 ∥ T007 ∥ T008 (distinct files) before T009.
- US1 tests T017 ∥ T018 ∥ T019.
- US2 copy tasks T028 ∥ T029.
- Polish T038 ∥ T039 ∥ T041.

---

## Parallel Example: Foundational core types

```bash
# After the failing tests (T003, T004) are in place, build the distinct-file types together:
Task: "Add WorkTracker member in src/DBAIAzure.Core/Models/ConnectorType.cs"          # T005
Task: "Create ResolvedWorkTrackerConfig in src/DBAIAzure.Core/Models/ResolvedWorkTrackerConfig.cs"  # T006
Task: "Create WorkTrackerProvider in src/DBAIAzure.Core/Models/WorkTrackerProvider.cs" # T007
Task: "Define IWorkTrackerConfigResolver in src/DBAIAzure.Core/Interfaces/IWorkTrackerConfigResolver.cs"  # T008
```

---

## Implementation Strategy

### MVP (User Story 1 only)

1. Phase 1 Setup → 2. Phase 2 Foundational (CRITICAL — generic identity, resolver, migration; ADO stays green)
→ 3. Phase 3 US1 (Jira from UI + run) → **STOP & VALIDATE** against quickstart Scenarios 1 & 3 → demo.

### Incremental delivery

Foundational → US1 (Jira e2e, MVP) → US2 (generic switch + copy) → US3 (Test Connection) → US4 (ADO
regression/migration proof) → Polish. Each story is an independently testable increment.

---

## Notes

- [P] = different files, no incomplete dependency. UI tasks on `ConnectorSettings.razor` are **not** mutually [P].
- Tests are written to FAIL first (Article V). Verify red before implementing.
- Commit after each task or logical group; record `tests-written` / `tests-passed` gates (workflow-enforcer).
- Article II: never wildcard-kill `dotnet`; stop the app with `scripts/stop-web.ps1` (targets the PID).
