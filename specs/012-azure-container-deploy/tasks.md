---
description: "Task list for One-URL Azure Container Demo Deployment"
---

# Tasks: One-URL Azure Container Demo Deployment

**Input**: Design documents from `specs/012-azure-container-deploy/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: INCLUDED for the one unit-testable component (`DemoConnectorSeeder`) per Constitution
Article V (Red → Green → Refactor). Container and cloud behaviors are verified by a local Docker
smoke test and the `quickstart.md` cloud round-trip (the justified Article V boundary in plan.md) —
there is no xUnit primitive for ACA scale-to-zero / wake / public FQDN.

**Organization**: The container + boot seeder + deploy script are a shared **Foundational** build
(blocking). Each user story is then an independently testable behavior validated against the live
deployment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no incomplete-task dependency)
- **[Story]**: US1–US5 (Setup, Foundational, Polish carry no story label)

## Path Conventions

Blazor Server app `src/DBAIAzure.Web/`, domain `src/DBAIAzure.Core/`, storage `src/DBAIAzure.Storage/`,
xUnit tests `tests/DBAIAzure.Tests/`. Deployment artifacts at repo root (`Dockerfile`, `.dockerignore`)
and under `deploy/aca/`. Behavioral target: the reference app at `C:\ProjectsWin\DBAI`.

---

## Phase 1: Setup

**Purpose**: Branch, baseline, and ignore-file hygiene before adding container artifacts.

- [X] T001 Ensure work is on `feature/012-azure-container-deploy` (Constitution Article III) and confirm a clean baseline with `dotnet build`
- [X] T002 Read `global.json` and note the pinned .NET 8 SDK version so the Dockerfile build stage matches it (reproducibility, FR-014)
- [X] T003 [P] Create `.dockerignore` at repo root excluding `bin/`, `obj/`, `.git/`, `tests/`, `specs/`, `**/*.db`, and any `team.env`/`.env` (hygiene + Article IX)
- [X] T004 [P] Add `deploy/aca/team.env` and `**/*.env` (except `*.example`) to `.gitignore` so no secret values are ever committed (FR-014, Article IX)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The deployable container, the boot connector seeder, and the deploy scripts. Nothing
can be validated until this exists — it is the MVP-enabling build.

**⚠️ CRITICAL**: No user story can be validated until this phase is complete.

### Tests for the boot seeder (write first — must FAIL before implementation) ⚠️

- [X] T005 [P] Unit tests in tests/DBAIAzure.Tests/DemoConnectorSeederTests.cs — seeds ServiceNow/ADO/Messaging from a controlled env source; **never** creates an LLM row even if an LLM key is present in env; tolerates each connector's missing values independently (leaves it unconfigured, no crash); asserts no secret value is passed to the logger (per contracts/DemoConnectorSeeder.md)
- [X] T006 [P] Integration tests in tests/DBAIAzure.Tests/DemoConnectorSeederIntegrationTests.cs — against the real `SqliteConnectorConfigRepository` + Data Protection: after `SeedAsync`, the three connector rows exist and secrets round-trip via `GetDecryptedSecretsAsync`; the LLM connector is absent
- [X] T006A [P] Integration test in tests/DBAIAzure.Tests/DesignTimeLlmKeyResolutionTests.cs — a design-time LLM service (Workflow Builder chat / Node Realization) resolves the LLM key+model from the `ConnectorType.LLM` DB row when present and falls back to config when absent; proves the visitor's entered key reaches design-time features without restart (research Decision 7). Implemented by T009A.

### Implementation

- [X] T007 Implement `DemoConnectorSeeder` in src/DBAIAzure.Web/Services/DemoConnectorSeeder.cs — read vault-injected env, write ServiceNow/ADO/Messaging connector rows via the existing `IConnectorConfigRepository` (whose `SaveAsync` encrypts secrets at rest via `ISecretProtector` internally, so the seeder needs no direct crypto dependency); never seed LLM; idempotent; tolerant of missing values (per contracts/DemoConnectorSeeder.md)
- [X] T008 Register `DemoConnectorSeeder` in DI and invoke `SeedAsync` from the post-`Build()` startup scope in src/DBAIAzure.Web/Program.cs, after `EnsureCreatedAsync` and before serving traffic
- [X] T009 In src/DBAIAzure.Web/Program.cs, pin Data Protection `SetApplicationName` for in-lifetime stability and confirm the key ring location stays ephemeral (no volume/persistence — research Decision 3)
- [X] T009A In src/DBAIAzure.Web/Program.cs, route the two **design-time** LLM singletons — the Workflow Builder `IChatCompletionService` (~line 266) and the Node Realization `IStructuredCompletionService` (~line 296) — through the same DB-first→config-fallback key resolution the per-run kernel factories use (read `ConnectorType.LLM` via `IConnectorConfigRepository` + the decrypt seam per call), so the visitor's entered LLM key powers the AI builder assistant and node realization with no restart (research Decision 7; FR-003/FR-004/SC-006); implements T006A. Sequential with T008/T009 (same file).
- [X] T010 [P] Create `Dockerfile` at repo root — multi-stage (`sdk:8.0` build via `dotnet publish -c Release`, `aspnet:8.0` runtime), `ASPNETCORE_URLS=http://+:8080`, `EXPOSE 8080`, entrypoint `dotnet DBAIAzure.Web.dll`, no secrets baked, SQLite path left container-local/ephemeral (per contracts/ContainerRuntime.md)
- [X] T011 [P] Create `deploy/aca/team.env.example` listing only the env **names** the demo seeds (ServiceNow/ADO/Messaging) — no values (per contracts/AcaDeployment.md)
- [X] T012 [P] Create `deploy/aca/seed-secrets.ps1` — inject back-office secret values from the Forge Vault into the deploy shell (zero-knowledge; values never written to a committed file or log; LLM key excluded)
- [X] T013 Create `deploy/aca/deploy.ps1` — `az acr` build/push, then `az containerapp create/update` with `--ingress external --target-port 8080 --min-replicas 0 --max-replicas 1 --cpu 0.25 --memory 0.5Gi --secrets <from shell> --env-vars KEY=secretref:name`; re-assert `--min-replicas 0 --max-replicas 1`; print the FQDN; **no LLM secret** included (per contracts/AcaDeployment.md)
- [X] T014 Local container smoke test — `docker build` then `docker run` with throwaway env vars; confirm the app serves on :8080, the back-office connectors show configured, the LLM connector prompts for a key, and no secret value appears in container logs (Article X)

**Checkpoint**: A deployable container that seeds the demo on boot exists and passes a local smoke test.

---

## Phase 3: User Story 1 — Hand someone a URL and they run a live test in minutes (Priority: P1) 🎯 MVP

**Goal**: A first-time visitor opens one public URL, supplies only their LLM key, and runs a ticket
end-to-end against the pre-seeded connectors.

**Independent Test**: From a machine that never touched the project, open the shared URL, enter only
an LLM API key, start a run, and observe it execute against the pre-configured connectors.

- [ ] T015 [US1] Deploy to Azure Container Apps via `deploy/aca/seed-secrets.ps1` + `deploy/aca/deploy.ps1`; capture the public HTTPS FQDN
- [ ] T016 [US1] Validate (quickstart §3): open the FQDN from a clean browser — no install, no login, only an LLM-key field; enter a key; run a demo ticket/workflow end-to-end against the seeded connectors with live progress; **also confirm a design-time LLM feature (Workflow Builder AI assistant or Node Realization) works with the same entered key** (proves T009A — research Decision 7)
- [X] T016A [US1] [P] Playwright E2E in tests/DBAIAzure.E2ETests/Tests/LlmKeyEntryTests.cs — the LLM connector card exposes a masked API Key field + provider dropdown on Edit, and a supplied key saves without error (covers the FR-003 key-entry interactive element per Constitution Article V; no live inference). Run via scripts/run-e2e.ps1.
- [ ] T017 [US1] Confirm a missing/invalid LLM key yields a clear, recoverable in-app prompt (not an opaque failure); adjust the run/settings path in src/DBAIAzure.Web if the message is unclear (FR-015)

**Checkpoint**: The core sharing flow works — a URL + an LLM key = a live demo (MVP delivered).

---

## Phase 4: User Story 2 — Two people use it at once without stepping on each other (Priority: P1)

**Goal**: Two simultaneous visitors share one stable workspace and watch runs live.

**Independent Test**: Open the URL in two sessions at once; both trigger/observe activity; neither
errors, hangs, or shows corrupted state because of the other.

- [X] T018 [US2] Verify `deploy/aca/deploy.ps1` pins `--max-replicas 1` (single shared instance required by the in-memory run state + SignalR with no backplane — research Decision 6)
- [ ] T019 [US2] Validate (quickstart §4): two concurrent sessions watch the same live run; updates arrive to the right session(s); the shared environment stays stable with no crash/corruption

**Checkpoint**: Concurrent two-visitor use is stable.

---

## Phase 5: User Story 3 — Repoint any connector to your own systems (Priority: P2)

**Goal**: A visitor overrides any connector at runtime without redeploy; untouched connectors keep
their seeded defaults.

**Independent Test**: Repoint one connector to a different target, run an action that uses it, and
confirm it hits the new target while others keep defaults.

- [ ] T020 [US3] Validate (quickstart §5): repoint one connector in-app → a subsequent action uses the new target without redeploy; its health check reflects the new target; untouched connectors still use the seeded defaults (FR-007/FR-008). Also validate FR-015(b): repointing to an **unreachable** target fails the health check gracefully with a clear message and does NOT crash the shared environment for the other visitor.

**Checkpoint**: Runtime repointing works and is isolated to the changed connector.

---

## Phase 6: User Story 4 — Idle to zero, wake on demand (Priority: P2)

**Goal**: The deployment costs nothing while idle and wakes automatically on the next request.

**Independent Test**: Leave the URL idle until it scales down (0 replicas), then reopen and confirm it
wakes and becomes usable.

- [ ] T021 [US4] Verify `deploy/aca/deploy.ps1` sets `--min-replicas 0` and re-asserts it; validate (quickstart §6): after the platform idle window `az containerapp replica list` shows 0 replicas, then reopening the URL wakes it and serves without a broken page (FR-012/FR-013)

**Checkpoint**: Scale-to-zero + wake-on-request behave like the reference.

---

## Phase 7: User Story 5 — Pre-seeded credentials delivered safely from the vault (Priority: P2)

**Goal**: Back-office secrets come from the vault and are never exposed; demo state is ephemeral.

**Independent Test**: Deploy with vault-sourced creds; confirm connectors work yet no secret appears
in repo/logs/UI; confirm a cold start resets state to defaults.

- [ ] T022 [US5] Audit (quickstart §7.1–7.2): no plaintext back-office secret in the repository, deployment logs, or UI responses; connectors nonetheless function (proves vault delivery); the UI masks stored secrets and cannot read seeded secrets back (FR-006/FR-009; `az containerapp secret list` shows them masked)
- [ ] T023 [US5] Validate ephemeral reset (quickstart §7.3): after an idle scale-down, run history is cleared, workflows revert to the seeded set, the entered LLM key is gone, and any repointed connector reverted to the seeded default — a fresh, usable demo (FR-016)

**Checkpoint**: Vault-safe secrets and reference-exact ephemeral reset confirmed.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T024 [P] Update CHANGELOG.md (single-URL ACA demo deployment, boot connector seeder, scale-to-zero, vault-sourced secrets)
- [X] T025 [P] Code-quality pass on src/DBAIAzure.Web/Services/DemoConnectorSeeder.cs and the Program.cs changes (Article IV — XML docs, nullable, guard clauses, no magic numbers)
- [X] T026 [P] Document the demo env keys (names only) in appsettings.json comments / a deploy README so the deployment is reproducible from committed config (FR-014, SC-007) — added deploy/aca/README.md
- [ ] T027 Run the full quickstart.md validation across US1–US5 and capture evidence (test output, smoke output, `az` excerpts, concurrent-session and idle→wake captures) per Constitution Article X

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup. BLOCKS all user stories (it builds the container,
  seeder, and deploy scripts).
- **US1 (Phase 3)**: Depends on Foundational — performs the first cloud deployment.
- **US2–US5 (Phases 4–7)**: Depend on the US1 deployment existing (they validate behaviors of the
  live app), but each is an **independently testable** behavior — concurrency (US2), repointing (US3),
  idle/wake (US4), secret-safety + ephemeral reset (US5) can be validated in any order.
- **Polish (Phase 8)**: After the desired stories are validated.

### Within Foundational

- Seeder tests (T005, T006) are written and FAIL before the seeder implementation (T007).
- The design-time LLM key-resolution test (T006A) is written and FAILS before T009A implements it.
- T008, T009, and T009A all edit `Program.cs` — sequential.
- T013 (`deploy.ps1`) consumes T012 (`seed-secrets.ps1`) and T010 (`Dockerfile`) — author T010–T012
  first, then T013, then the T014 smoke test.

### File-contention notes

- T008, T009, and T009A all edit `src/DBAIAzure.Web/Program.cs` — do not run in parallel.
- T010, T011, T012 are separate files → [P]; T013 depends on them.

### Parallel Opportunities

- Setup T003/T004 are [P].
- Foundational tests T005/T006 are [P]; artifact files T010/T011/T012 are [P].
- Polish T024/T025/T026 are [P].
- The user-story **validations** (US2–US5) can be executed in parallel by different people against the
  single deployed app, since they only observe/exercise behavior.

---

## Parallel Example: Foundational seeder tests

```bash
# Write both seeder tests together (they must fail first):
Task: "Unit tests for DemoConnectorSeeder in tests/DBAIAzure.Tests/DemoConnectorSeederTests.cs"
Task: "Integration tests for DemoConnectorSeeder in tests/DBAIAzure.Tests/DemoConnectorSeederIntegrationTests.cs"
```

---

## Implementation Strategy

### MVP First (Foundational + User Story 1)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational) — container, boot seeder, deploy scripts,
   local smoke test green.
2. Complete Phase 3 (US1) — deploy and prove a clean visitor runs with only an LLM key.
3. **STOP and VALIDATE**: a shared URL that works out of the box is the entire point of the request.

### Incremental Delivery

1. Foundational → a deployable, self-seeding container.
2. US1 → the public URL demo (MVP).
3. US2 → prove concurrent two-visitor stability.
4. US3 → prove runtime repointing.
5. US4 → prove idle→zero→wake.
6. US5 → prove vault-secret safety + ephemeral reset.

### Parallel Team Strategy

One developer builds Foundational + US1. Once deployed, US2–US5 validations can be split across people
against the single live deployment.

---

## Notes

- [P] = different files, no incomplete-task dependency.
- New C# files: committing with `--no-verify` is acceptable per the project's known pre-commit hook
  gate bug (tests are still written and run) — do not skip writing the tests.
- **Article IX**: never let a secret value enter a committed file, a log, or the conversation; the
  vault is the only secret source; the LLM key is never seeded.
- Do not mount a volume for SQLite or the Data Protection key ring — ephemerality is required (FR-016).
- Do not add GitHub Actions for deployment — local script only (Article VIII / reference parity).
