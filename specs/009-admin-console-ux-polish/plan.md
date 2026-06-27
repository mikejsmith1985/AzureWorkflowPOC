# Implementation Plan: Admin Console UX Polish — Configuration & Visual Parity

**Branch**: `feature/admin-console-ux-polish` | **Date**: 2026-06-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/009-admin-console-ux-polish/spec.md`

---

## Summary

Replace the raw-JSON connector configuration surface with a typed, field-per-property form UI. Retire `ConnectorConfigModal.razor` and `ConnectorSection.razor` in the same PR. Add a reusable `InfoTip` tooltip component (portal-rendered via `TooltipService`), a first-time onboarding banner (`OnboardingBanner`) driven by `IConnectorHealthChecker` + browser localStorage, and a full visual-polish layer (fade-in animations, save/test loading states, inline validation, success flash) across the Settings page.

---

## Technical Context

**Language/Version**: C# 12 / .NET 8

**Primary Dependencies**: Blazor Server, Tailwind CSS (CDN — no build step), ASP.NET Core Data Protection, Entity Framework Core (SQLite), Semantic Kernel 1.77.0

**Storage**: SQLite via EF Core (existing `PipelineDbContext`). No schema changes — connector config persistence is unchanged.

**Testing**: xUnit 2.x + Moq (unit), Playwright / `Microsoft.Playwright` (E2E via `scripts/run-e2e.ps1`)

**Target Platform**: ASP.NET Core Kestrel, Windows / Linux server

**Performance Goals**: Test Connection must return within 10 seconds (FR-007). All animations complete within 200ms (SC-08).

**Constraints**: `IConnectorConfigRepository`, `IConnectorHealthChecker`, and `ISecretProtector` interfaces are read-only — not modified by this feature. Last-write-wins concurrency; no optimistic locking. Any authenticated user may access `/settings/connectors`; no new auth gate.

**Scale/Scope**: 4 fixed connector types, single-admin-at-a-time assumed; no multi-tenant concerns.

---

## Constitution Check

| Article | Gate | Status |
|---------|------|--------|
| I — Prime Directive | Best route: typed form, portal tooltip, TDD | ✓ Pass |
| II — Process Protection | No wildcard process kills in scripts | ✓ Pass |
| III — Branching | Work on `feature/admin-console-ux-polish` | ✓ Pass |
| IV — Code Quality | All new types follow PascalCase/camelCase rules; XML doc on all public members; no magic numbers; guard clauses over nesting | ✓ Pass |
| V — Testing (TDD) | Failing unit tests written first for `ConnectorFormState`, `ConnectorFieldSchema`, `OnboardingState`, `TooltipService`; E2E tests cover all 10 quickstart scenarios | ✓ Pass |
| VI — Documentation | CHANGELOG.md updated in PR | ✓ Pass |
| VII — Framework-First | No SK Process Framework primitives are bypassed — this is pure Blazor UI, not an orchestration/state/HITL/LLM concern | ✓ Pass |
| VIII — Release Discipline | No direct-to-main commit; PR required | ✓ Pass |
| IX — Secrets | Sentinel pattern + null-save preserves stored secrets; no plaintext ever in UI or logs | ✓ Pass |
| X — Verification & Proof | E2E Playwright tests validate each acceptance scenario before feature is shippable | ✓ Pass |
| XI — Output Restraint | No ad-hoc status documents generated | ✓ Pass |

---

## Project Structure

### Documentation (this feature)

```text
specs/009-admin-console-ux-polish/
├── plan.md              ← this file
├── research.md          ← Phase 0 decisions
├── data-model.md        ← entity definitions
├── contracts/
│   └── service-interfaces.md   ← ITooltipService, IOnboardingStateService, ConnectorFieldSchema
├── quickstart.md        ← validation guide
└── tasks.md             ← Phase 2 output (/speckit-tasks command)
```

### Source Code

```text
src/DBAIAzure.Core/
└── Models/
    ├── ConnectorFieldDescriptor.cs    [NEW] field metadata record + ConnectorFieldType enum
    ├── ConnectorFieldSchema.cs        [NEW] static factory, hardcoded per-connector field tables
    ├── ConnectorFormState.cs          [NEW] transient UI state record + ConnectorSaveStatus enum
    └── SecretSentinel.cs              [NEW] string constant "__KEY_STORED__"

src/DBAIAzure.Web/
├── Components/
│   └── Settings/                      [NEW directory]
│       ├── InfoTip.razor              [NEW] info icon + tooltip trigger
│       ├── ConnectorFieldEditor.razor [NEW] single-field renderer (label, input, badge, validation)
│       └── OnboardingBanner.razor     [NEW] first-time setup banner
├── Services/
│   ├── TooltipService.cs              [NEW] scoped tooltip portal service + TooltipContext record
│   └── OnboardingStateService.cs      [NEW] scoped onboarding state + localStorage persistence
├── Pages/
│   └── ConnectorSettings.razor        [REPLACE] full rewrite — accordion layout, typed fields
├── Shared/
│   ├── ConnectorConfigModal.razor     [DELETE]
│   ├── ConnectorSection.razor         [DELETE]
│   └── MainLayout.razor               [MODIFY] add <TooltipPortal /> + <OnboardingBanner />
├── Pages/
│   └── _Host.cshtml                   [MODIFY] add window.localStorageGet / localStorageSet
└── wwwroot/css/
    └── workflow-canvas-animations.css [MODIFY] add fade-in, success-flash keyframes + classes

tests/DBAIAzure.Tests/
├── ConnectorFieldSchemaTests.cs       [NEW] validates all four field tables
├── ConnectorFormStateTests.cs         [NEW] sentinel, invalidation, save-path logic
├── SecretSentinelTests.cs             [NEW] constant value, remove-key path
├── OnboardingStateTests.cs            [NEW] ShouldShow, dismiss, exception path
├── TooltipServiceTests.cs             [NEW] show/hide/replace contract
└── ConnectorSettingsPanelTests.cs     [MODIFY] update for new form-state API

tests/DBAIAzure.E2ETests/
└── ConnectorSettingsTests.cs          [MODIFY] replace JSON-textarea tests with 10 new scenarios
```

---

## Complexity Tracking

No constitution violations requiring justification. All custom work is against documented gaps:
- `TooltipService` — Blazor Server has no native portal API; documented gap.
- `OnboardingStateService` + localStorage interop — no built-in Blazor Server onboarding primitive; documented gap.
- `ConnectorFieldSchema` static tables — hardcoded per spec assumption; not a premature abstraction.
