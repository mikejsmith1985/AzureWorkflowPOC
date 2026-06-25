# Implementation Plan: One-URL Azure Container Demo Deployment

**Branch**: `feature/012-azure-container-deploy` | **Date**: 2026-06-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/012-azure-container-deploy/spec.md`

## Summary

Package the existing .NET 8 / Blazor Server app as a single public-URL **Azure Container Apps (ACA)**
deployment that behaves exactly like the reference LangGraph app at `C:\ProjectsWin\DBAI`: one
shareable HTTPS URL, scale-to-zero when idle with wake-on-request, back-office connectors pre-wired
from Forge Vault-sourced credentials, the visitor supplying **only** their own LLM key, runtime
connector repointing, a shared single-instance workspace for concurrent visitors, and ephemeral
state that resets to deploy-time defaults on every cold start.

The reference deploys via a **local `az` CLI script** (`deploy/aca/deploy.sh`) building a single
container into **Azure Container Registry** and creating an ACA app with `--ingress external
--min-replicas 0 --max-replicas 1`; back-office secrets become **ACA secrets + env-var secretrefs**
from a gitignored `team.env`; the LLM key is never seeded; SQLite lives on the container's ephemeral
filesystem (no mounted volume) so every cold start is a fresh demo. We mirror that model.

**Three pieces of new work, plus deployment artifacts:**
1. **Boot-time demo seeding (the core code gap).** Our app only writes `ConnectorConfigs` via the
   UI; the reference re-seeds from env on every cold start. Add a startup `DemoConnectorSeeder` that
   reads vault-injected env vars and populates the ServiceNow (ticketing), Azure DevOps (work-items),
   and Messaging connectors on each boot — **never** the LLM connector — so the demo works out of the
   box while the visitor still supplies their own LLM key. Reuse the existing connector repository and
   encryption seam.
2. **Route the visitor's LLM key to every LLM consumer.** The per-run kernel factories already
   re-read the LLM key from the `ConnectorType.LLM` DB row, but two design-time singletons — the
   Workflow Builder AI assistant (`IChatCompletionService`) and Node Realization
   (`IStructuredCompletionService`) — capture the (empty, unseeded) config key at startup. Route both
   through the same DB-first→config-fallback resolution so the visitor's entered key powers the AI
   builder and node realization with no restart (research Decision 7). Reuses the existing per-call
   key-resolution pattern — no new component.
3. **Container + deploy artifacts.** A `Dockerfile` (multi-stage `dotnet publish`), `.dockerignore`,
   and a `deploy/aca/` local deploy script (PowerShell, matching this Windows/pwsh environment and
   the reference's local-pipeline model) that builds to ACR and creates/updates the ACA app with
   scale-to-zero and vault-sourced secrets — reproducible from committed config, no secrets committed.

No change to workflow/pipeline behavior, the data model, or how the app runs once started.

## Technical Context

**Language/Version**: C# 12 / .NET 8 (pinned via `global.json`).

**Primary Dependencies**: ASP.NET Core / Blazor Server (+ SignalR: Blazor circuit hub and
`WorkflowRunHub`), EF Core (SQLite), ASP.NET Core Data Protection (connector-secret encryption),
Semantic Kernel (execution — untouched). Container/runtime images:
`mcr.microsoft.com/dotnet/sdk:8.0` (build) and `mcr.microsoft.com/dotnet/aspnet:8.0` (runtime).

**Storage**: SQLite on the **container's ephemeral filesystem** (default `pipeline.db` under
ContentRoot — intentionally NOT a mounted volume), so all demo state resets to deploy-time defaults
on each cold start (FR-016). No persistent database.

**Target Platform**: Azure Container Apps (Linux container), images in Azure Container Registry,
deployed by a local `az` CLI script. Public external ingress, HTTPS FQDN.

**Project Type**: Web application (single Blazor Server app + Core/Storage libraries) plus
deployment artifacts at repo root (`Dockerfile`, `deploy/aca/`).

**Performance Goals**: Cold-start to usable within an acceptable wait comparable to the reference
(~10–30 s first hit after idle, per the reference's own note). A first-time visitor reaches a working
run supplying only an LLM key in under 5 minutes (SC-001).

**Constraints**:
- **Single replica is mandatory** (`--max-replicas 1`): the app holds run state in in-memory
  singletons (`WorkflowExecutionOrchestrator`) and uses SignalR with no backplane; scaling out would
  fragment shared state. This conveniently also delivers the "shared workspace" model (FR-018).
- **Ephemeral by design**: no volume mount for SQLite or the Data Protection key ring; cold start =
  clean slate (FR-016).
- **Zero-knowledge secrets** (Constitution Article IX): connector credentials are injected from the
  Forge Vault into ACA secrets at deploy time; no secret value enters source control, logs, or the
  conversation; the UI already masks stored secrets.
- **LLM key is never pre-seeded** (FR-004/SC-006): the seeder explicitly excludes it.
- **No GitHub Actions** (Constitution Article VIII / reference parity): deployment is a local,
  reproducible script.

**Scale/Scope**: Demo/evaluation posture (not hardened multi-tenant prod). One small running
instance when active (~0.25 vCPU / 0.5 GiB, matching the reference), scale-to-zero when idle. A
handful of connectors (ServiceNow, Azure DevOps, Messaging) + the visitor's LLM key.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Article I (BEST not fastest)**: PASS. We mirror the proven reference topology and reuse the app's
  existing connector/secret/real-time machinery rather than rebuilding; the only new code is the
  documented seeding gap.
- **Article II (Process Protection)**: PASS. No wildcard process kills introduced; deploy script
  targets named Azure resources, not local PIDs.
- **Article III (Branching)**: PASS. Work on `feature/012-azure-container-deploy`.
- **Article IV (Code Quality)**: PASS (impl gate). The new seeder carries XML docs, nullable honored,
  guard clauses, no magic numbers; the Dockerfile/script are commented with a one-line purpose.
- **Article V (Testing — three-layer)**: PASS with a justified boundary. Unit: seeder logic
  (LLM-excluded, idempotent, secret values never logged, missing-env handled). Integration: seeder
  writes/decrypts `ConnectorConfigs` via the real repository + Data Protection seam; an unconfigured
  LLM still prompts the visitor; and a design-time LLM service resolves the DB-stored key (falling
  back to config) so the builder assistant / node realization use the visitor's key (Decision 7).
  **Container smoke test** (local `docker build` + `docker run` with
  fake env): the app boots, the URL serves, seeded connectors appear configured, the LLM prompt
  appears. *Boundary:* cloud-only behaviors (scale-to-zero idle, wake-on-request, the public FQDN)
  cannot be unit-tested and are validated manually via `quickstart.md` against a real ACA deployment
  (Article X evidence) — there is no framework primitive to assert them in xUnit.
- **Article VI (Documentation)**: PASS. `CHANGELOG.md` updated; deploy steps live in `quickstart.md`
  (a pipeline artifact), not an ad-hoc status doc.
- **Article VII (Framework-First Gate)**: PASS. Idle/scale-to-zero/wake → **ACA built-in HTTP scaler**
  (no bespoke idle timer). Secret delivery → **ACA secrets + env**, secret encryption → **existing
  ASP.NET Data Protection seam**, connector storage → **existing `ConnectorConfigs` repository**,
  config binding → **existing `IConfiguration` env convention**, real-time/concurrency → **existing
  Blazor Server + SignalR** on a single replica. **Documented gap (custom, justified):** nothing
  seeds connectors from environment at boot — `DemoConnectorSeeder` fills exactly that gap and is the
  only new runtime *component*; the Decision-7 change *modifies* the two existing LLM-singleton
  registrations to reuse the per-call key-resolution pattern already proven three times in this file
  (no new abstraction). The Dockerfile and deploy script are infrastructure, not framework
  reimplementation.
- **Article VIII (Release: local pipeline)**: PASS. A local `az` CLI deploy script, no Actions
  runner; build via `dotnet publish` inside the image; reproducible from committed config (FR-014).
- **Article IX (Secrets & Configuration)**: PASS. Vault-sourced injection, gitignored env file, ACA
  secrets, masked UI; no secret in source/logs/conversation; LLM key never seeded.
- **Article X (Verification & Proof)**: PASS by plan — seeder proven by tests, container proven by a
  local smoke run, cloud behavior proven by the quickstart round-trip (open URL, run with only an LLM
  key, second concurrent session, idle→wake).
- **Article XI (Output Restraint)**: PASS. No new dashboards; generated/scratch output kept out of the
  committed tree; secrets never echoed.

**Result: PASS — no violations to track in Complexity Tracking.**

## Project Structure

### Documentation (this feature)

```text
specs/012-azure-container-deploy/
├── plan.md              # This file
├── research.md          # Phase 0 output — reference-parity decisions
├── data-model.md        # Phase 1 output — deployment/runtime entities (no schema change)
├── quickstart.md        # Phase 1 output — build, deploy, and validate guide
├── contracts/           # Phase 1 output
│   ├── DemoConnectorSeeder.md      # boot-time env→connector seeding (LLM excluded)
│   ├── ContainerRuntime.md         # Dockerfile / port / ephemerality contract
│   └── AcaDeployment.md            # ACA app config + vault secret injection contract
└── checklists/
    └── requirements.md  # (from /speckit-specify)
```

### Source Code (repository root)

```text
Dockerfile                                   # NEW — multi-stage dotnet publish; ASPNETCORE_URLS :8080; ephemeral SQLite
.dockerignore                                # NEW — exclude bin/obj/.git/secrets
deploy/
└── aca/
    ├── deploy.ps1                           # NEW — local az CLI deploy (ACR build/push + ACA create/update, scale-to-zero)
    ├── seed-secrets.ps1                     # NEW — pull Forge Vault values → ACA secrets/env (zero-knowledge), gitignored output
    └── team.env.example                     # NEW — names (not values) of the env vars the demo seeds; real team.env gitignored

src/
├── DBAIAzure.Web/
│   ├── Services/
│   │   └── DemoConnectorSeeder.cs           # NEW — reads vault-injected env → seeds ServiceNow/ADO/Messaging connectors (NOT LLM)
│   ├── Program.cs                           # CHANGED — run DemoConnectorSeeder at startup; route design-time LLM singletons through DB-first key resolution (D7)
│   └── appsettings.json                     # CHANGED — document demo env keys; ensure no secrets committed
├── DBAIAzure.Core/
│   └── Configuration/
│       └── ConnectorSeedOptions.cs          # NEW — strongly-typed seed values bound from ConnectorSeed__* (no LLM member)
├── DBAIAzure.Connectors/
│   └── HotReloadAnthropicService.cs         # NEW — design-time LLM hot-reload (T009A, research Decision 7)
└── DBAIAzure.Storage/                       # UNCHANGED (reuse ConnectorConfigs repo + ISecretProtector)

tests/
├── DBAIAzure.Tests/
│   ├── DemoConnectorSeederTests.cs          # NEW — unit: LLM excluded, idempotent, secrets not logged, missing env tolerated
│   ├── DemoConnectorSeederIntegrationTests.cs # NEW — integration: seeds + decrypts via real repo/Data Protection
│   └── DesignTimeLlmKeyResolutionTests.cs   # NEW — unit: design-time LLM key resolves DB-first, config fallback (T006A)
└── DBAIAzure.E2ETests/
    └── Tests/LlmKeyEntryTests.cs            # NEW — Playwright: LLM key-entry interactive element (D1, Article V)
```

**Structure Decision**: Keep the single Blazor Server app unchanged in how it runs; add one startup
service (`DemoConnectorSeeder`) in `DBAIAzure.Web/Services` (alongside the existing connector
services) wired from the existing `Program.cs` startup scope. Deployment artifacts live at the repo
root (`Dockerfile`, `.dockerignore`) and under `deploy/aca/` mirroring the reference's
`deploy/aca/` layout, deployed by a local PowerShell `az` CLI script (this environment is
Windows/pwsh; the reference uses bash — same model, host-appropriate shell).

## Complexity Tracking

> No Constitution Check violations — table intentionally empty.
