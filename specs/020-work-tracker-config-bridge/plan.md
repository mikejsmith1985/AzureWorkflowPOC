# Implementation Plan: Work-Tracker Config Bridge — Select & Configure Any Tracker (incl. Jira) from the UI

**Branch**: `feature/generic-work-tracker-config-labels` | **Date**: 2026-07-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/020-work-tracker-config-bridge/spec.md`

## Summary

Bridge the operator-facing **connector-settings UI** to the spec-018 **work-tracker adapter layer** so an
operator can select the active work tracker and enter its credentials entirely in the UI — proving it
end-to-end against a real **Jira** instance. Today the two systems are disconnected: the UI edits a
hardcoded Azure DevOps connector stored in the DB, while the pipeline selects its tracker and reads Jira
credentials only from environment variables that the UI never surfaces. This feature introduces a single
generic **Work Tracking System** connector (`ConnectorType.WorkTracker`) with a `provider` discriminator
(Azure DevOps / Jira), makes the stored connector the **per-run source of truth** for both selection and
credentials, converts the Jira adapter from startup-baked auth to per-run resolution (the specific fix that
makes UI-entered Jira credentials work without restart), adds a Jira **Test Connection**, and auto-migrates
existing ADO connectors in place. The spec-018 adapters are **reused unchanged** — this is a UI-and-wiring
feature, not new tracker logic. Technical decisions are fixed in [research.md](./research.md).

## Technical Context

**Language/Version**: C# / .NET 8 (`global.json`-pinned SDK) — unchanged.

**Primary Dependencies**: existing only — `IWorkTrackerAdapter` + ADO/Jira adapters (spec-018),
`IConnectorConfigRepository` + `ISecretProtector` + `PipelineDbContext` (spec-002), ASP.NET Core Data
Protection, Blazor Server. No new packages.

**Storage**: EF Core over the shared SQLite `pipeline.db` — **no schema migration** (rows are string-keyed;
the new `WorkTracker` connector is a new row value). Migration-less DB init via `EnsureCreated` + hand-rolled
`CREATE TABLE IF NOT EXISTS` in `Program.cs`.

**Testing**: xUnit (unit — resolver dispatch, migration idempotency, Jira config parse, tester probes),
bUnit (the generic connector card / provider-conditional form), Playwright (`scripts/run-e2e.ps1` — Connectors
tab). TDD Red→Green per Article V.

**Target Platform**: Blazor Server web app (`DBAIAzure.Web`, Kestrel) + console runner; Windows dev + Linux
container (ACA).

**Project Type**: Web application (Blazor Server) + class libraries. No new projects.

**Performance Goals**: Test Connection returns within a few seconds (SC-004); per-run config resolution adds
no meaningful overhead (rebuild-on-change caching, same discipline as the ADO client and LLM hot-reload).

**Constraints**: Secrets never in plaintext / never redisplayed (Article IX, FR-006); single active tracker
per instance (spec-018 FR-005, unchanged); live-apply without restart (FR-005); zero manual reconfiguration
for existing ADO deployments (FR-015/SC-003).

**Scale/Scope**: Concentrated change. New: `ConnectorType.WorkTracker`, `JiraConnectorConfig`,
`IWorkTrackerConfigResolver`, `IJiraConnectionFactory`, `JiraConnectorTester`, the startup migration. Modified:
`WorkTrackerAdapterProvider` (per-run), `JiraWorkTrackerAdapter` (factory-based), `AzureDevOpsBoardsClient`
config source (WorkTracker row), `ConnectorSettings.razor` (generic card + provider selector),
`OnboardingBanner.razor` / `UserGuide.razor` (generic copy), `Program.cs` DI + migration,
`SqliteConnectorConfigRepository.AllConnectorTypes`.

## Constitution Check

*GATE: must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Article | Gate | Status |
|---|---|---|
| I — Prime Directive (BEST) | Finish the spec-018 seam properly (per-run resolution, migration), not a cosmetic rename | ✅ Feature's purpose |
| II — Process Protection | No wildcard `dotnet` kills; target PID from `stop-web.ps1` | ✅ In quickstart |
| III — Branching | Work on `feature/generic-work-tracker-config-labels`; PR to main | ✅ On branch |
| IV — Code Quality | Naming/doc/40-line/guard-clause rules on new resolver, factory, tester, migration | ✅ Enforced Phase 3/4 |
| **V — Testing (TDD, 3-layer)** | Failing-first: migration idempotency, resolver dispatch, Jira tester, bUnit card, Playwright | ✅ Plan mandates Red→Green |
| VI — Documentation | CHANGELOG updated; artifacts confined to `specs/020-*` | ✅ |
| **VII — Framework-First** | Extend existing seams (spec-018 adapter, spec-002 connector store, existing hot-reload + `IConnectorHealthChecker`), do not rebuild | ✅ See note |
| VIII — Release | Deliberate; one-time idempotent migration bundled, gated on tests | ✅ D6 |
| IX — Secrets | Reuse `ISecretProtector`; migration copies ciphertext, never decrypts; secret never redisplayed | ✅ FR-006 |
| X — Verification & Proof | Real Jira round-trip + live switch + migration replay in quickstart | ✅ |
| XI — Output Restraint | No scratch dashboards; no ad-hoc summary docs | ✅ |

**Framework-First note (Article VII)**: Every new type is minimal app-specific plumbing over an existing seam,
not a reinvention: `IWorkTrackerConfigResolver` centralizes reads of the existing repository;
`IJiraConnectionFactory` ports the existing ADO `GetClientAsync` connection-cache pattern; `JiraConnectorTester`
implements the existing `IConnectorHealthChecker.TestAsync`; the migration reuses `IConnectorConfigRepository`.
No parallel state machine, event bus, or persistence layer is introduced. **No Complexity Tracking entries
required.**

## Project Structure

### Documentation (this feature)

```text
specs/020-work-tracker-config-bridge/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions D1–D8 (complete)
├── data-model.md        # Phase 1 — connector type, discriminated config, resolver, migration
├── quickstart.md        # Phase 1 — 6 validation scenarios + regression gate
├── contracts/           # Phase 1 — internal seams
│   ├── work-tracker-config-resolver.md
│   ├── jira-per-run-resolution.md
│   ├── jira-connection-test.md
│   └── ado-connector-migration.md
├── checklists/requirements.md   # Spec quality checklist (from /speckit-specify)
└── tasks.md             # Phase 2 — created by /speckit-tasks (NOT here)
```

### Source Code (repository root — existing projects, mapped to changes)

```text
src/
├── DBAIAzure.Core/
│   ├── Models/ConnectorType.cs                 # + WorkTracker member (AzureDevOps kept for legacy parse) [D1]
│   ├── Models/JiraConnectorConfig.cs           # NEW non-secret Jira record [D2]
│   ├── Models/ResolvedWorkTrackerConfig.cs     # NEW in-memory resolved config [D3]
│   └── Interfaces/IWorkTrackerConfigResolver.cs # NEW seam [D3]
├── DBAIAzure.Web/
│   ├── Services/WorkTrackerConfigResolver.cs   # NEW impl — reads WorkTracker row, dispatch on provider [D3]
│   ├── Services/WorkTrackerAdapterProvider.cs  # per-run GetAdapter() via resolver (was WorkTracker:Active) [D3]
│   ├── Integrations/Jira/JiraConnectionFactory.cs   # NEW per-run authed client, cache-on-change [D4]
│   ├── Integrations/Jira/JiraWorkTrackerAdapter.cs   # take factory, resolve per call (was baked client) [D4]
│   ├── Integrations/Jira/JiraConnectorTester.cs      # NEW IConnectorHealthChecker impl [D5]
│   ├── Integrations/AzureDevOps/AzureDevOpsBoardsClient.cs # read WorkTracker row (provider=ADO) [D1/D3]
│   ├── Pages/ConnectorSettings.razor           # generic card + provider selector + Jira form + test [D7]
│   ├── Components/Settings/OnboardingBanner.razor   # generic chip copy [D7]
│   ├── Pages/UserGuide.razor                    # generic help copy [D7]
│   └── Program.cs                               # DI (resolver/factory/tester), per-run registrations,
│                                                #   one-time ADO→WorkTracker migration [D3/D4/D6]
├── DBAIAzure.Storage/
│   └── Repositories/SqliteConnectorConfigRepository.cs # AllConnectorTypes: ADO→WorkTracker [D1]
tests/
├── DBAIAzure.Tests/     # unit: resolver dispatch, migration idempotency, Jira config parse, tester probes;
│                        #   bUnit: provider-conditional card
└── DBAIAzure.E2ETests/  # Playwright: Connectors tab provider select + Jira form + test + round-trip
```

**Structure Decision**: No new projects. The bridge is re-homed within existing library boundaries — the
generic connector identity + records in `DBAIAzure.Core`, the resolver/factory/tester/UI/DI/migration in
`DBAIAzure.Web`, the connector-type set in `DBAIAzure.Storage`. This keeps the diff aligned with the current
architecture (Article VII — extend the seams, don't add parallel structure).

## Phased approach (development sequencing)

1. **Generic identity + resolver (no behavior change yet)**: add `ConnectorType.WorkTracker`,
   `JiraConnectorConfig`, `ResolvedWorkTrackerConfig`, `IWorkTrackerConfigResolver` + impl; swap
   `AllConnectorTypes`. Repoint `WorkTrackerAdapterProvider` and `AzureDevOpsBoardsClient` at the resolver.
   Prove ADO regression stays green reading the `WorkTracker` row.
2. **Migration**: one-time idempotent ADO→WorkTracker startup migration (D6); prove replay is a no-op and ADO
   behavior is unchanged.
3. **Jira per-run resolution**: `JiraConnectionFactory` + adapter change (D4); remove startup-baked Jira
   client auth. Prove UI-saved Jira credentials take effect without restart.
4. **Jira test**: `JiraConnectorTester` on the existing seam (D5).
5. **UI**: generic Work Tracking System card + provider selector + Jira form (D7); generify onboarding/help
   copy; wire Test Connection to the selected provider. bUnit + Playwright green.
6. **End-to-end**: run a ticket onto real Jira; live-switch ADO↔Jira; full suite + quickstart scenarios as
   gates. Update CHANGELOG.

## Complexity Tracking

*No Constitution violations requiring justification.* Every new type is minimal plumbing over an existing
seam (see Framework-First note); no added architectural complexity.
