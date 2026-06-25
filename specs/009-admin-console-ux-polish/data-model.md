# Data Model: Admin Console UX Polish

## New Entities

---

### ConnectorFieldDescriptor

**Location**: `src/DBAIAzure.Core/Models/ConnectorFieldDescriptor.cs`  
**Nature**: Immutable value object (C# `record`). Never persisted — defined as static tables.

| Property | Type | Description |
|----------|------|-------------|
| `Key` | `string` | JSON property key used in `NonSecretConfig` or secrets blob (e.g., `"InstanceUrl"`) |
| `Label` | `string` | Human-readable field label shown in the UI (e.g., `"ServiceNow URL"`) |
| `FieldType` | `ConnectorFieldType` | Rendering hint: `Url`, `Text`, or `Secret` |
| `Placeholder` | `string` | Input placeholder (e.g., `"https://acme.service-now.com"`) |
| `TooltipContent` | `string` | Plain-English description for the InfoTip |
| `TooltipExample` | `string` | Concrete example value for the InfoTip (maps to `InfoTip.Example` parameter) |
| `IsRequired` | `bool` | Whether the field must have a value before save is allowed |

**Enum ConnectorFieldType**: `Url` | `Text` | `Secret`

**Static factory**: `ConnectorFieldSchema.For(ConnectorType)` returns `IReadOnlyList<ConnectorFieldDescriptor>`.

---

### ConnectorFormState

**Location**: `src/DBAIAzure.Core/Models/ConnectorFormState.cs`  
**Nature**: Transient, in-memory UI state per connector. Not persisted; reconstructed from `ConnectorConfig` on page load.

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `ConnectorType` | Which connector this state represents |
| `FieldValues` | `Dictionary<string, string>` | Draft values keyed by `ConnectorFieldDescriptor.Key`. Secret fields pre-populated with `SecretSentinel.Value` when `ConnectorConfig.HasSecrets == true`. |
| `ValidationErrors` | `Dictionary<string, string>` | Per-field validation error message. Empty when valid. |
| `TestResult` | `ConnectorTestResult?` | Result of the most recent Test Connection call; `null` when untested or invalidated by a field edit |
| `IsTesting` | `bool` | True while a Test Connection request is in flight |
| `SaveStatus` | `ConnectorSaveStatus` | Current save state: `Idle`, `Saving`, `SavedOk`, or `SavedError` |
| `SaveError` | `string?` | Error message populated when `SaveStatus == SavedError` |
| `IsExpanded` | `bool` | Whether the accordion section is open |

**Enum ConnectorSaveStatus**: `Idle` | `Saving` | `SavedOk` | `SavedError`

**Invariants**:
- When a field value changes, `TestResult` is set to `null` (invalidated).
- A secret field value equal to `SecretSentinel.Value` maps to `null` on save (preserve existing secret).
- A secret field value that is blank maps to `null` on save (preserve existing secret).
- Only a non-blank, non-sentinel secret field value triggers a secrets-blob update.

---

### SecretSentinel

**Location**: `src/DBAIAzure.Core/Models/SecretSentinel.cs`  
**Nature**: String constant class (single `public const string Value = "__KEY_STORED__"`).

**Rules**:
- The server sets a secret field's draft value to `SecretSentinel.Value` when `ConnectorConfig.HasSecrets == true`.
- The UI renders a "Key saved" badge and leaves the input blank when the draft value equals `SecretSentinel.Value`.
- On save, a draft value equal to `SecretSentinel.Value` or `""` passes `null` for `plaintextSecretsJson` to `IConnectorConfigRepository.SaveAsync`.
- A "Remove stored key" action sets the draft value to `""` with a distinct `isKeyRemoveRequested` flag, which passes an empty JSON object `{}` to signal explicit secret deletion (distinct from null/preserve).

---

### OnboardingState

**Location**: Managed by `OnboardingStateService.cs` in `src/DBAIAzure.Web/Services/`.  
**Persistence**: Browser `localStorage` key `"onboarding_dismissed"` (string `"true"` / absent).  
**Nature**: Transient server-side state enriched with browser-side persistence.

| Property | Type | Description |
|----------|------|-------------|
| `IsLlmHealthy` | `bool` | True when `IConnectorHealthChecker` returns a successful result for `ConnectorType.LLM`; false when result is failed, partial, or when the health check itself throws |
| `IsDismissed` | `bool` | True when the user has manually dismissed the banner; persisted in localStorage |
| `ShouldShow` | `bool` (derived) | `!IsLlmHealthy && !IsDismissed` |

**Lifecycle**:
1. On page load: `OnboardingStateService.InitialiseAsync()` reads `localStorage` for dismissed flag + calls `IConnectorHealthChecker.TestAsync(ConnectorType.LLM)`.
2. If health check throws → `IsLlmHealthy = false` (banner shows per clarification Q5).
3. User clicks "Dismiss" → `IsDismissed = true` written to localStorage; banner hides.
4. On next page load with LLM healthy → `ShouldShow == false`; banner does not render.

---

## Unchanged Entities (consumed, not modified)

| Entity | Location | Notes |
|--------|----------|-------|
| `ConnectorConfig` | `Core/Models/ConnectorConfig.cs` | Domain projection; `HasSecrets` bool drives sentinel display |
| `ConnectorTestResult` | `Core/Models/ConnectorTestResult.cs` | Consumed to drive success/failure/partial UI states (FR-008–010) |
| `ConnectorType` | `Core/Models/ConnectorType.cs` | Enum: `ServiceNow`, `AzureDevOps`, `LLM`, `Teams` |
| `ServiceNowConnectorConfig` | `Core/Models/ServiceNowConnectorConfig.cs` | Deserialised from `NonSecretConfig` JSON on load |
| `AzureDevOpsConnectorConfig` | `Core/Models/AzureDevOpsConnectorConfig.cs` | Deserialised from `NonSecretConfig` JSON on load |
| `LlmConnectorConfig` | `Core/Models/LlmConnectorConfig.cs` | Deserialised from `NonSecretConfig` JSON on load |

---

## Modified Files (no schema change — interface/behavior only)

| File | Change |
|------|--------|
| `_Host.cshtml` | Add `window.localStorageGet` and `window.localStorageSet` JS functions |
| `MainLayout.razor` | Add `<TooltipPortal />` render slot and `<OnboardingBanner />` below nav |
| `Program.cs` | Register `TooltipService` (scoped) and `OnboardingStateService` (scoped) |
| `ConnectorSettings.razor` | Full replacement: typed form fields, InfoTip, accordion layout, save/test UX |
| `workflow-canvas-animations.css` | Add `fade-in`, `success-flash` keyframes and `.section-enter`, `.btn-success-flash` classes |

## Deleted Files

| File | Replacement |
|------|-------------|
| `Shared/ConnectorConfigModal.razor` | Settings page is now the canonical configuration surface |
| `Shared/ConnectorSection.razor` | Superseded by `Components/Settings/ConnectorFieldEditor.razor` |
