# Quickstart Validation Guide: Admin Console UX Polish

## Prerequisites

- .NET 8 SDK (version specified in `global.json` if present, otherwise 8.0.422+)
- Vault secrets injected (see `memory/reference_live-verify-secrets.md`) **or** `appsettings.Development.json` with stub values for offline validation
- For E2E tests: Playwright browsers installed (`pwsh scripts/install-playwright.ps1`)

---

## Build Verification

```powershell
# From repo root — must produce zero errors, zero warnings
dotnet build DBAIAzure.sln --no-incremental
```

**Expected**: All 8 projects compile. Any reference to the deleted `ConnectorConfigModal.razor` or `ConnectorSection.razor` surfaces as a compile error here — this is intentional and must be resolved before the build passes.

---

## Unit Test Suite

```powershell
dotnet test tests/DBAIAzure.Tests --no-build
```

**Key new/updated test classes**:

| Test Class | What it validates |
|---|---|
| `ConnectorFieldSchemaTests` | `ConnectorFieldSchema.For()` returns non-empty lists for all four `ConnectorType` values; every field has non-empty `Label`, `TooltipContent`, and `TooltipExample` |
| `ConnectorFormStateTests` | Sentinel detection: setting a secret field to `__KEY_STORED__` maps to `null` on save; field edit clears `TestResult`; save with blank secret preserves existing |
| `SecretSentinelTests` | `Value` constant equals `"__KEY_STORED__"`; remove-key path maps to empty JSON object `{}` |
| `OnboardingStateTests` | `ShouldShow` is `true` when LLM unhealthy + not dismissed; `false` when LLM healthy; `false` after dismiss; health check exception → `IsLlmHealthy = false` |
| `TooltipServiceTests` | `Show` fires `OnChange`; `Hide` clears `ActiveTooltip`; second `Show` replaces first without stacking |
| `ConnectorSettingsPanelTests` | All existing tests continue to pass after ConnectorSettings.razor replacement |

---

## E2E Validation Scenarios

Start the app:
```powershell
# From repo root
scripts/run-e2e.ps1
# OR for manual run:
dotnet run --project src/DBAIAzure.Web --launch-profile Development
# Navigate to https://localhost:5001/settings/connectors
```

### Scenario 1 — No JSON textarea (FR-006, SC-02)

1. Navigate to `/settings/connectors`
2. Expand any connector section
3. **Assert**: No `<textarea>` element is visible anywhere on the page
4. **Assert**: Each connector section shows distinct labelled form fields

### Scenario 2 — ServiceNow field layout (US1 / FR-001–FR-003)

1. Expand the ServiceNow section
2. **Assert**: Three fields visible: "ServiceNow URL" (type=url), "Username" (type=text), "Password" (type=password with reveal toggle)
3. **Assert**: Reveal toggle has `aria-label` attribute
4. Click reveal toggle — **Assert**: input type changes from `password` to `text`

### Scenario 3 — Key saved badge (FR-004, FR-011, SC-05)

*Prerequisite: ServiceNow connector has a password stored.*
1. Expand ServiceNow section
2. **Assert**: Password field is blank
3. **Assert**: "Key saved" badge is visible next to the password field
4. **Assert**: No `__KEY_STORED__` text visible anywhere in the DOM

### Scenario 4 — Tooltip appears and disappears (US2 / FR-005, FR-025)

1. Hover the info icon next to "Organisation URL" on the Azure DevOps section
2. **Assert**: Tooltip panel appears containing the description text
3. **Assert**: Tooltip contains a concrete example value
4. Move mouse away
5. **Assert**: Tooltip disappears within 300ms

### Scenario 5 — Test Connection states (FR-007–FR-010)

*Success path*:
1. Fill all Azure DevOps fields with valid values
2. Click "Test Connection"
3. **Assert**: Button shows spinner and is non-interactive during the test
4. **Assert**: On success, green indicator appears with "Connected" text

*Failure path*:
1. Set Organisation URL to `https://invalid.example.com`
2. Click "Test Connection"
3. **Assert**: Red indicator appears with human-readable message (no stack trace, no HTTP status code)

### Scenario 6 — Save lifecycle (FR-019, FR-020, SC-07)

1. Make a change to any non-secret field
2. Click "Save"
3. **Assert**: Save button shows loading state and is non-interactive during save
4. **Assert**: After save completes, button briefly shows green success state
5. **Assert**: Button returns to normal after ~1.5 seconds

### Scenario 7 — Onboarding banner (US3 / FR-014–FR-017)

*LLM not configured*:
1. Navigate to `/` (home)
2. **Assert**: Onboarding banner is visible with LLM as required step
3. **Assert**: Other connectors shown as optional secondary steps
4. Click the LLM step link
5. **Assert**: Navigated to `/settings/connectors` with LLM section expanded/focused

*LLM configured and healthy*:
1. Configure LLM connector with valid credentials
2. Reload the page
3. **Assert**: Onboarding banner is no longer shown

*Manual dismiss*:
1. Ensure LLM is not configured
2. Click the "Dismiss" button on the onboarding banner
3. **Assert**: Banner hides immediately
4. Reload the page
5. **Assert**: Banner remains hidden

### Scenario 8 — Fade-in animation (FR-018, SC-08)

1. Navigate to `/settings/connectors`
2. **Assert**: Connector sections fade in on first render (inspect CSS transition)
3. **Assert**: No layout shift during fade-in

### Scenario 9 — Remove stored key (FR-013)

1. Open a connector that has a stored secret
2. Click "Remove stored key" next to the "Key saved" badge
3. **Assert**: Badge disappears; field is blank and editable
4. Click "Save" without entering a new value
5. **Assert**: Save completes; connector `HasSecrets` is now `false` (badge no longer shown on re-open)

### Scenario 10 — ConnectorConfigModal.razor removed (US5 / SC-02)

1. Navigate to the Threads page (or wherever the gear icon previously appeared)
2. **Assert**: No gear icon opens a connector config modal
3. **Assert**: Any navigation to Settings works via the main nav item only

---

## Regression Guard

Run the full E2E suite to confirm no existing functionality was broken:

```powershell
scripts/run-e2e.ps1
```

**Expected**: All pre-existing tests in `NavigationTests`, `ReviewQueueTests`, `RunHistoryTests`, `ThreadsPageTests`, and `WorkflowBuilderTests` continue to pass. `ConnectorSettingsTests` should have new passing tests for the scenarios above.

---

## Acceptance Mapping

| SC | Scenario above | Pass criteria |
|---|---|---|
| SC-01 | Scenarios 2–5 combined | Configuring all 4 connectors requires no JSON knowledge |
| SC-02 | Scenario 1, 10 | Zero raw JSON textareas visible |
| SC-03 | Scenario 4 | Every field has a tooltip with example |
| SC-04 | Scenario 5 | Three visually distinct test result states |
| SC-05 | Scenario 3 | No secret echoed back to browser |
| SC-06 | Scenario 7 | Onboarding banner guides first-time user |
| SC-07 | Scenario 6 | All buttons show loading states |
| SC-08 | Scenario 8 | All animations ≤ 200ms, no layout shift |
