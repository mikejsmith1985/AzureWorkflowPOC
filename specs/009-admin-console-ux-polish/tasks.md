# Tasks: Admin Console UX Polish — Configuration & Visual Parity

> **Implementation status (2026-06-26):** This spec predated work that already shipped. Re-baselined
> against current `main` before implementing:
> - **Already shipped (no-op):** US1 typed connector forms (no JSON textarea; `ConnectorSettings.razor`
>   already field-per-property), the modal/`ConnectorSection` retirement (those files are already gone),
>   and US5 header health badges (Configured / Healthy / Unhealthy already render).
> - **Implemented now (net-new):** US3 onboarding banner (`OnboardingStateService` + `OnboardingBanner`
>   + localStorage JS + `?expand=` deep-links) and US2 field tooltips (`TooltipService` + `InfoTip` +
>   layout-root portal), plus the `section-enter` / `btn-success-flash` polish keyframes. Unit tests:
>   `OnboardingStateTests`, `TooltipServiceTests` (14 green); solution builds 0 errors.
> - **Deferred:** Playwright E2E tasks (T012/T019/T023/T028/T034) and the full US4 button spinner/
>   success-flash wiring (T035–T037) — the CSS primitives exist but the button bindings weren't added.

**Input**: Design documents from `specs/009-admin-console-ux-polish/`

**Prerequisites**: plan.md ✓ | spec.md ✓ | research.md ✓ | data-model.md ✓ | contracts/ ✓ | quickstart.md ✓

**Tests**: Included per Article V of the project constitution (TDD mandatory — write failing tests before implementation).

**Organization**: Tasks grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no inter-task dependency)
- **[Story]**: Which user story this task belongs to (US1–US5, maps to spec.md)
- Exact file paths are included in every task description

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: JavaScript and CSS infrastructure shared by multiple user stories. No user story depends on another story's completion to start here.

- [ ] T001 Add `window.localStorageGet` and `window.localStorageSet` functions to the inline script block in `src/DBAIAzure.Web/Pages/_Host.cshtml`
- [ ] T002 [P] Add `@keyframes fade-in`, `@keyframes success-flash`, `.section-enter`, and `.btn-success-flash` CSS rules to `src/DBAIAzure.Web/wwwroot/css/workflow-canvas-animations.css`

**Checkpoint**: JS interop and animation keyframes available for all component phases.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models and unit tests that every user story depends on. Must be complete and green before any user story phase begins.

**⚠️ CRITICAL**: All unit tests in this phase must be written first and confirmed failing before the matching implementation task.

- [ ] T003 Write failing unit tests for `SecretSentinel` (value equals `"__KEY_STORED__"`, remove-key path maps to empty JSON) in `tests/DBAIAzure.Tests/SecretSentinelTests.cs`
- [ ] T004 Create `SecretSentinel` constant class in `src/DBAIAzure.Core/Models/SecretSentinel.cs` — make T003 pass
- [ ] T005 [P] Write failing unit tests for `ConnectorFieldSchema` (non-empty lists for all four `ConnectorType` values; every field has non-empty `Label`, `TooltipContent`, `TooltipExample`) in `tests/DBAIAzure.Tests/ConnectorFieldSchemaTests.cs`
- [ ] T006 [P] Create `ConnectorFieldType` enum (`Url`, `Text`, `Secret`) and `ConnectorFieldDescriptor` record in `src/DBAIAzure.Core/Models/ConnectorFieldDescriptor.cs`
- [ ] T007 Create `ConnectorFieldSchema` static factory with hardcoded field tables for all four connectors (ServiceNow: 3 fields, AzureDevOps: 3 fields, LLM: 3 fields, Teams: 1 field) in `src/DBAIAzure.Core/Models/ConnectorFieldSchema.cs` — make T005 pass
- [ ] T008 [P] Write failing unit tests for `ConnectorFormState` (sentinel detection, field-edit clears `TestResult`, blank-secret maps to null on save) in `tests/DBAIAzure.Tests/ConnectorFormStateTests.cs`
- [ ] T009 Create `ConnectorSaveStatus` enum (`Idle`, `Saving`, `SavedOk`, `SavedError`) and `ConnectorFormState` record in `src/DBAIAzure.Core/Models/ConnectorFormState.cs` — make T008 pass
- [ ] T010 [P] Write failing unit tests for `TooltipService` (`Show` fires `OnChange`, `Hide` clears `ActiveTooltip`, second `Show` replaces first) in `tests/DBAIAzure.Tests/TooltipServiceTests.cs`
- [ ] T011 Create `ITooltipService`, `TooltipContext` record, and `TooltipService` implementation in `src/DBAIAzure.Web/Services/TooltipService.cs` — make T010 pass

**Checkpoint**: All foundational tests green. `dotnet test tests/DBAIAzure.Tests` passes. User story implementation can begin.

---

## Phase 3: User Story 1 — Non-Technical Connector Configuration (Priority: P0) 🎯 MVP

**Goal**: Replace the raw JSON textarea with labelled, typed form fields for all four connectors. Include secret sentinel ("Key saved" badge), reveal/hide toggle, remove-key action, and save path.

**Independent Test**: Navigate to `/settings/connectors`, expand any connector section — no `<textarea>` visible; three distinct labelled fields present; password field has reveal toggle; existing secret shows "Key saved" badge.

### Tests for User Story 1 (write first — must fail before T014)

- [ ] T012 [P] [US1] Write failing E2E tests covering US1 acceptance scenarios 1–5 (no JSON textarea, typed fields, green/red test result, "Key saved" badge, blank re-save preserves secret) in `tests/DBAIAzure.E2ETests/ConnectorSettingsTests.cs`

### Implementation for User Story 1

- [ ] T013 [US1] Create `ConnectorFieldEditor.razor` — renders label + `InfoTip` placeholder (no-op until US2) + input (type from `ConnectorFieldType`) + password reveal toggle + "Key saved" badge + "Remove stored key" link + inline validation error in `src/DBAIAzure.Web/Components/Settings/ConnectorFieldEditor.razor`
- [ ] T014 [US1] Replace `ConnectorSettings.razor` with accordion layout: load all four `ConnectorConfig` records on `OnInitializedAsync`, build `ConnectorFormState` per connector (set secret fields to sentinel when `HasSecrets == true`), render one accordion section per connector using `ConnectorFieldEditor` for each field in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`
- [ ] T015 [US1] Implement save path in `ConnectorSettings.razor`: deserialise non-secret fields to typed records, detect sentinel/blank → `null` for `plaintextSecretsJson`, detect remove-key flag → `"{}"` for explicit clear, call `IConnectorConfigRepository.SaveAsync()`, update `ConnectorFormState.SaveStatus` in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`
- [ ] T016 [US1] Implement test-connection path in `ConnectorSettings.razor`: call `IConnectorHealthChecker.TestAsync(connectorType)`, set `ConnectorFormState.TestResult`, map result to green/amber/red display with human-readable message, set `IsTesting` spinner flag, disable Test button while in-flight in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`
- [ ] T017 [US1] Delete `src/DBAIAzure.Web/Shared/ConnectorConfigModal.razor`, delete `src/DBAIAzure.Web/Shared/ConnectorSection.razor`, and remove all call sites (gear-icon triggers and `Open()` references) in `src/DBAIAzure.Web/Pages/` and `src/DBAIAzure.Web/Components/`
- [ ] T018 [US1] Update `tests/DBAIAzure.Tests/ConnectorSettingsPanelTests.cs` for the new `ConnectorFormState`-based API — remove any tests that relied on the old JSON-textarea model

**Checkpoint**: Build compiles (modal deleted, all references removed), US1 E2E tests pass. A non-technical user can configure all four connectors without seeing JSON.

---

## Phase 4: User Story 5 — Clean Consolidated Settings Page Layout (Priority: P1)

**Goal**: Section headers display connector icon + name + health badge (Healthy / Check Required / Not Configured). "Re-test" shortcut on amber badge. Single page, no modal required.

**Independent Test**: Load `/settings/connectors` with a configured healthy connector — green "Healthy" badge visible in section header without expanding the section. Load with a connector whose last test failed — amber "Check required" badge with "Re-test" button visible.

### Tests for User Story 5 (write first — must fail before T020)

- [ ] T019 [P] [US5] Write failing E2E tests covering US5 acceptance scenarios 1–4 (all 4 connectors on one page, Not Configured CTA, Healthy badge, Check Required with Re-test) in `tests/DBAIAzure.E2ETests/ConnectorSettingsTests.cs`

### Implementation for User Story 5

- [ ] T020 [US5] Implement accordion section header in `ConnectorSettings.razor`: connector icon + display name + health badge (`ConnectorStatusBadge` component or equivalent), derive badge from `ConnectorConfig.LastTestResult` and `IsConfigured` in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`
- [ ] T021 [US5] Add "Re-test" shortcut button to the amber "Check required" badge state — clicking it sets `IsExpanded = true` on that section and immediately triggers `TestAsync` without requiring the user to open the form in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`
- [ ] T022 [P] [US5] Confirm `src/DBAIAzure.Web/Shared/MainLayout.razor` navigation link points to `/settings/connectors` and no residual gear-icon or modal-trigger references remain in any navigation or layout file

**Checkpoint**: US5 E2E tests pass. Settings page is the single canonical configuration surface.

---

## Phase 5: User Story 2 — Contextual Field Guidance via Tooltips (Priority: P1)

**Goal**: Every connector field shows an info icon. Hovering opens a portal-rendered tooltip with description + example. Tooltip flips when near viewport edge. Disappears within 200ms of mouse leaving.

**Independent Test**: Hover the info icon next to any field — tooltip panel appears above or below the icon, contains description and example text, disappears when mouse moves away.

### Tests for User Story 2 (write first — must fail before T024)

- [ ] T023 [P] [US2] Write failing E2E tests covering US2 acceptance scenarios 1–3 (tooltip appears with description + example, disappears on mouseleave, flips near viewport edge) in `tests/DBAIAzure.E2ETests/ConnectorSettingsTests.cs`

### Implementation for User Story 2

- [ ] T024 [US2] Create `InfoTip.razor`: renders `ℹ` icon button (`aria-label="More information"`), on `@onmouseenter` calls `getBoundingClientRect` via `IJSRuntime` then `ITooltipService.Show(Content, Example, rect)`, on `@onmouseleave` calls `ITooltipService.Hide()`, accepts `Content` (string, required) and `Example` (string?, optional) parameters in `src/DBAIAzure.Web/Components/Settings/InfoTip.razor`
- [ ] T025 [US2] Register `ITooltipService → TooltipService` (scoped) in `src/DBAIAzure.Web/Program.cs` and update `ConnectorFieldEditor.razor` to pass `Descriptor.TooltipContent` and `Descriptor.TooltipExample` to the `<InfoTip>` placeholder wired in T013 in `src/DBAIAzure.Web/Components/Settings/ConnectorFieldEditor.razor`
- [ ] T026 [US2] Add tooltip portal to `src/DBAIAzure.Web/Shared/MainLayout.razor`: inject `ITooltipService`, subscribe to `OnChange` → call `StateHasChanged()`, render active tooltip panel at layout root with `position: fixed`, `z-index: 9999`; compute flip (top vs bottom) based on anchor top vs `window.innerHeight / 2`

**Checkpoint**: US2 E2E tests pass. All 10 connector fields show tooltips; no clipping by parent overflow containers.

---

## Phase 6: User Story 3 — First-Time Onboarding Banner (Priority: P1)

**Goal**: When LLM is unconfigured or unhealthy (including health-check exceptions), show guided setup banner on home page. LLM is the required primary step; other three connectors shown as optional. Banner hides when LLM becomes healthy. User can dismiss permanently (localStorage).

**Independent Test**: Load the app with no LLM connector configured — onboarding banner visible with LLM as required step and three optional steps. Click the LLM step link — navigates to `/settings/connectors?expand=LLM` with that section pre-expanded. Dismiss the banner — reloading keeps it hidden.

### Tests for User Story 3 (write first — must fail before T029)

- [ ] T027 [P] [US3] Write failing unit tests for `OnboardingStateService` (`ShouldShow` true when LLM unhealthy + not dismissed; false when LLM healthy; false after dismiss; health check exception → `IsLlmHealthy = false`) in `tests/DBAIAzure.Tests/OnboardingStateTests.cs`
- [ ] T028 [P] [US3] Write failing E2E tests covering US3 acceptance scenarios 1–4 (banner shown/hidden based on LLM health, step link navigates and expands correct section, dismiss persists) in `tests/DBAIAzure.E2ETests/ConnectorSettingsTests.cs`

### Implementation for User Story 3

- [ ] T029 [US3] Create `IOnboardingStateService` and `OnboardingStateService` (scoped): `InitialiseAsync` reads `localStorageGet("onboarding_dismissed")` via `IJSRuntime` and calls `IConnectorHealthChecker.TestAsync(LLM)` — any exception sets `IsLlmHealthy = false`; `DismissAsync` writes `localStorageSet("onboarding_dismissed","true")` in `src/DBAIAzure.Web/Services/OnboardingStateService.cs` — make T027 pass
- [ ] T030 [US3] Register `IOnboardingStateService → OnboardingStateService` (scoped) in `src/DBAIAzure.Web/Program.cs`
- [ ] T031 [US3] Create `OnboardingBanner.razor`: renders only when `IOnboardingStateService.State.ShouldShow == true`, shows LLM as required primary step linking to `/settings/connectors?expand=LLM` and ServiceNow / AzureDevOps / Teams as optional secondary steps, includes dismiss button that calls `DismissAsync()` in `src/DBAIAzure.Web/Components/Settings/OnboardingBanner.razor`
- [ ] T032 [US3] Add `<OnboardingBanner />` below the navigation bar in `src/DBAIAzure.Web/Shared/MainLayout.razor`; call `OnboardingStateService.InitialiseAsync()` from `OnAfterRenderAsync(firstRender: true)` in the layout
- [ ] T033 [US3] Handle `?expand=LLM` (and other connector type names) query parameter in `ConnectorSettings.razor` — on `OnParametersSetAsync`, if the param is present set `IsExpanded = true` on the matching `ConnectorFormState` and scroll/focus that section in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`

**Checkpoint**: US3 E2E tests pass. A first-time user landing on the app is immediately guided to configure the LLM connector.

---

## Phase 7: User Story 4 — Visual Feedback and Animation (Priority: P2)

**Goal**: Save/test buttons show spinner while in-flight and are non-interactive. Save success shows 1.5-second green flash. Page sections fade in on first render (150ms). Error messages are plain English; no stack traces shown.

**Independent Test**: Click Save on a connector section — Save button shows spinner and is disabled during the request; after success the button briefly turns green then reverts. Navigate to Settings — connector sections fade in.

### Tests for User Story 4 (write first — must fail before T035)

- [ ] T034 [P] [US4] Write failing E2E tests covering US4 acceptance scenarios 1–4 (save spinner, 1.5s green flash, 150ms fade-in, error banner with plain English) in `tests/DBAIAzure.E2ETests/ConnectorSettingsTests.cs`

### Implementation for User Story 4

- [ ] T035 [US4] Implement save/test spinner and success flash in `ConnectorSettings.razor`: bind Save button `disabled` to `SaveStatus == Saving`; after `SavedOk`, apply `btn-success-flash` CSS class and start a `Task.Delay(1500)` timer to reset to `Idle`; bind Test button `disabled` to `IsTesting` in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`
- [ ] T036 [P] [US4] Apply `.section-enter` CSS class to each connector accordion `<div>` so it fades in on first render via the `@keyframes fade-in` animation added in T002 in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`
- [ ] T037 [US4] Implement dismissible plain-English error banner in `ConnectorSettings.razor`: catch exceptions from save and test paths, map to user-facing messages (no stack traces, no HTTP status codes), render as dismissible red banner above the action row in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`

**Checkpoint**: US4 E2E tests pass. All buttons show loading states; no frozen UI. Animations play within 200ms.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, documentation, build gate.

- [ ] T038 [P] Run the full E2E suite to confirm all pre-existing tests still pass: `scripts/run-e2e.ps1` — `NavigationTests`, `ReviewQueueTests`, `RunHistoryTests`, `ThreadsPageTests`, `WorkflowBuilderTests` must be green
- [ ] T039 [P] Update `CHANGELOG.md` with all behavior changes introduced by spec-009 (typed connector forms, retired modal, tooltip system, onboarding banner, animation layer)
- [ ] T040 Run `dotnet build DBAIAzure.sln --no-incremental` — zero errors, zero warnings

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 — **BLOCKS** all user story phases
- **US1 (Phase 3)**: Depends on Phase 2 completion — no US dependency
- **US5 (Phase 4)**: Depends on Phase 3 completion (builds on ConnectorSettings.razor accordion)
- **US2 (Phase 5)**: Depends on Phase 2 completion (TooltipService registered) and T013 (ConnectorFieldEditor has InfoTip slot)
- **US3 (Phase 6)**: Depends on Phase 1 (localStorage JS) and Phase 2 completion; independent of US1/US2/US5
- **US4 (Phase 7)**: Depends on Phase 3 completion (buttons and sections exist in ConnectorSettings.razor)
- **Polish (Phase 8)**: Depends on all user story phases complete

### User Story Dependencies

- **US1 (P0)**: Start after Phase 2 — no other story dependency
- **US5 (P1)**: Start after US1 — extends the ConnectorSettings.razor built in US1
- **US2 (P1)**: Start after Phase 2 and T013 — InfoTip slot is a no-op until T024 wires it; can overlap with US5
- **US3 (P1)**: Start after Phase 1 and Phase 2 — fully independent of US1/US2/US5
- **US4 (P2)**: Start after US1 — applies polish to ConnectorSettings.razor sections and buttons

### Within Each User Story

1. Write failing tests → confirm they fail → implement → confirm tests pass

### Parallel Opportunities Within Phases

**Phase 2** — T003/T005/T008/T010 (unit test stubs) can all be written in parallel before any implementations run

**Phase 3** — T012 (E2E tests) and T013 (ConnectorFieldEditor.razor) can be started in parallel

**Phase 5** — T023 (E2E tests) and T024 (InfoTip.razor) can be started in parallel

**Phase 6** — T027 (unit tests) and T028 (E2E tests) can be written in parallel before T029

---

## Parallel Example: Phase 2 (Foundational Tests First)

```
Write in parallel (all in different test files):
  T003 — SecretSentinelTests.cs
  T005 — ConnectorFieldSchemaTests.cs
  T008 — ConnectorFormStateTests.cs
  T010 — TooltipServiceTests.cs

Then implement in dependency order:
  T004 — SecretSentinel.cs             (unblocks nothing else, small)
  T006 — ConnectorFieldDescriptor.cs   (unblocks T007)
  T007 — ConnectorFieldSchema.cs       (unblocks Phase 3)
  T009 — ConnectorFormState.cs         (unblocks Phase 3)
  T011 — TooltipService.cs             (unblocks Phase 5)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only — P0)

1. Complete Phase 1: Setup (T001–T002)
2. Complete Phase 2: Foundational (T003–T011)
3. Complete Phase 3: US1 (T012–T018)
4. **STOP and VALIDATE**: Navigate to `/settings/connectors`, confirm no JSON textarea, confirm typed fields work, confirm "Key saved" badge
5. Deploy or demo — the product is now self-serviceable by non-technical users

### Incremental Delivery

1. Phase 1 + 2 → Foundation ready
2. Phase 3 → US1 complete (P0 MVP)
3. Phase 4 → US5 complete (clean layout, health badges)
4. Phase 5 → US2 complete (tooltips — reduces support tickets)
5. Phase 6 → US3 complete (onboarding — first-time UX)
6. Phase 7 → US4 complete (animations — production-grade feel)
7. Phase 8 → Full validation

---

## Notes

- Constitution Article V mandates TDD: every implementation task has a corresponding failing test written immediately before it
- `[P]` tasks operate on different files — safe to parallelize
- Each user story checkpoint must be verified before the next priority story begins
- The modal deletion (T017) is an atomic commit: file deleted + all call sites removed in one step to avoid a broken build window
- `ConnectorSettings.razor` is modified across multiple phases (3, 4, 5, 7) — each phase adds to the same file; tasks within each phase are sequential for that file
