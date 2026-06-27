# Data Model: ADO Telemetry Field Bootstrap

**Feature**: specs/008-ado-telemetry-bootstrap  
**Date**: 2026-06-23

---

## Enumerations

### `AdoProcessType`
**Namespace**: `DBAIAzure.Core.Models.AdoTelemetry`  
**File**: `src/DBAIAzure.Core/Models/AdoTelemetry/AdoProcessType.cs`

| Value | Meaning |
|---|---|
| `Agile` | ADO inherited Agile process — story-level WIT is "User Story" |
| `Scrum` | ADO inherited Scrum process — story-level WIT is "Product Backlog Item" |
| `Unsupported` | Any other process type (CMMI, hosted XML, etc.) — preflight halts |

### `AdoFieldType`
**Namespace**: `DBAIAzure.Core.Models.AdoTelemetry`  
**File**: `src/DBAIAzure.Core/Models/AdoTelemetry/AdoFieldType.cs`

| Value | ADO type string | Notes |
|---|---|---|
| `String` | `"string"` | |
| `Integer` | `"integer"` | |
| `Double` | `"double"` | Maps to ADO `Decimal` |
| `PicklistString` | `"picklistString"` | Requires picklist creation step |

### `PreflightMode`
**Namespace**: `DBAIAzure.Core.Models.AdoTelemetry`  
**File**: `src/DBAIAzure.Core/Models/AdoTelemetry/PreflightMode.cs`

| Value | Meaning |
|---|---|
| `Bootstrap` | Admin rights confirmed — custom fields created/verified |
| `Adaptive` | No admin rights — native field fallback mapping built |

---

## Core Config Entities

### `AdoTelemetryFieldDefinition`
**Namespace**: `DBAIAzure.Core.Models.AdoTelemetry`  
**File**: `src/DBAIAzure.Core/Models/AdoTelemetry/AdoTelemetryFieldDefinition.cs`

| Property | Type | Nullable | Validation |
|---|---|---|---|
| `Name` | `string` | No | Non-empty |
| `ReferenceName` | `string` | No | Non-empty; convention `Custom.*` |
| `FieldType` | `AdoFieldType` | No | Valid enum value |
| `PicklistValues` | `IReadOnlyList<string>` | Yes | Non-null only when `FieldType == PicklistString` |
| `Required` | `bool` | No | Always present |
| `FallbackReferenceName` | `string` | Yes | Native ADO reference name or null (log-only) |
| `FallbackDisplayName` | `string` | Yes | Human-readable name of fallback field |

**Invariants**:
- `PicklistValues` must be non-null and non-empty when `FieldType == PicklistString`.
- `FallbackReferenceName` null means "log only" — not a missing-data error.

### `AdoTelemetryWorkItemTypeConfig`
**Namespace**: `DBAIAzure.Core.Models.AdoTelemetry`  
**File**: `src/DBAIAzure.Core/Models/AdoTelemetry/AdoTelemetryWorkItemTypeConfig.cs`

| Property | Type | Notes |
|---|---|---|
| `WorkItemTypeName` | `string` | e.g. `"User Story"`, `"Product Backlog Item"`, `"Task"` |
| `Fields` | `IReadOnlyList<AdoTelemetryFieldDefinition>` | At least one entry |

### `AdoTelemetryFieldConfig`
**Namespace**: `DBAIAzure.Core.Models.AdoTelemetry`  
**File**: `src/DBAIAzure.Core/Models/AdoTelemetry/AdoTelemetryFieldConfig.cs`

| Property | Type | Notes |
|---|---|---|
| `Version` | `string` | Semver string (e.g. `"1.0"`) |
| `WorkItemTypes` | `IReadOnlyDictionary<string, AdoTelemetryWorkItemTypeConfig>` | Key is logical WIT name (e.g. `"UserStory"`, `"Task"`) — resolved to actual WIT name based on `AdoProcessType` at runtime |
| `FallbackStrategy` | `IReadOnlyDictionary<AdoFieldType, string?>` | Maps field type to fallback reference name (`null` = log-only) |
| `TagsEncoding` | `string` | Always `"pipe_separated_kv"` in default config |

**Logical WIT key to actual ADO WIT name mapping** (resolved at runtime per `AdoProcessType`):

| Config key | Agile WIT | Scrum WIT |
|---|---|---|
| `"UserStory"` | `"User Story"` | `"Product Backlog Item"` |
| `"Task"` | `"Task"` | `"Task"` |

---

## Manifest Entities

### `PreflightManifestBase` (abstract record)
**Namespace**: `DBAIAzure.Core.Models.AdoTelemetry`  
**File**: `src/DBAIAzure.Core/Models/AdoTelemetry/PreflightManifest.cs`

| Property | Type | Notes |
|---|---|---|
| `Mode` | `PreflightMode` | Abstract — overridden in derived types |
| `Timestamp` | `DateTimeOffset` | UTC time preflight completed |
| `OrgUrl` | `string` | The org URL that was probed |
| `Project` | `string` | The project name that was targeted |
| `ProcessType` | `AdoProcessType` | Detected process type |

### `BootstrapManifest : PreflightManifestBase`
**Namespace**: `DBAIAzure.Core.Models.AdoTelemetry`  
**File**: `src/DBAIAzure.Core/Models/AdoTelemetry/PreflightManifest.cs`

| Property | Type | Notes |
|---|---|---|
| `Mode` | `PreflightMode` | Always `Bootstrap` |
| `FieldsCreated` | `IReadOnlyList<string>` | Reference names of newly created fields |
| `FieldsExisting` | `IReadOnlyList<string>` | Reference names of fields that already existed |
| `FieldsFailed` | `IReadOnlyList<FieldBootstrapFailure>` | Fields that failed after all retries |
| `MappingStrategy` | `string` | Always `"preferred"` — custom fields |

**Invariant**: `FieldsCreated + FieldsExisting + FieldsFailed` = full set of configured fields.

### `AdaptiveManifest : PreflightManifestBase`
**Namespace**: `DBAIAzure.Core.Models.AdoTelemetry`  
**File**: `src/DBAIAzure.Core/Models/AdoTelemetry/PreflightManifest.cs`

| Property | Type | Notes |
|---|---|---|
| `Mode` | `PreflightMode` | Always `Adaptive` |
| `Mapping` | `IReadOnlyDictionary<string, string>` | Key = desired reference name, value = actual ADO field reference name |
| `UnmatchedFields` | `IReadOnlyList<string>` | Desired fields with no suitable native match (fell through to log-only) |
| `LogOnlyFields` | `IReadOnlyList<string>` | Reference names captured in log only, not written to ADO |

### `FieldBootstrapFailure`
**Namespace**: `DBAIAzure.Core.Models.AdoTelemetry`  
**File**: `src/DBAIAzure.Core/Models/AdoTelemetry/PreflightManifest.cs`

| Property | Type | Notes |
|---|---|---|
| `ReferenceName` | `string` | The field that failed |
| `Error` | `string` | Last error message after exhausting retries |
| `AttemptsExhausted` | `int` | Always 3 (matches FR-014) |

---

## Service Interface

### `IAdoTelemetryPreflightService`
**Namespace**: `DBAIAzure.Core.Interfaces`  
**File**: `src/DBAIAzure.Core/Interfaces/IAdoTelemetryPreflightService.cs`

```
RunPreflightAsync(
    config: AdoTelemetryFieldConfig,
    ct: CancellationToken
) → Task<PreflightResult>
```

### `PreflightResult`
**Namespace**: `DBAIAzure.Core.Models.AdoTelemetry`

| Property | Type | Notes |
|---|---|---|
| `IsSuccess` | `bool` | False only when preflight halts (FR-004, FR-005, FR-013) |
| `ErrorMessage` | `string?` | Populated when `IsSuccess = false` |
| `Manifest` | `PreflightManifestBase?` | Null when `IsSuccess = false` |

**State transitions**:

```
Config missing / unreachable ADO / unsupported process
    → IsSuccess=false, Manifest=null, ErrorMessage=<diagnostic>

Admin rights detected
    → Bootstrap Mode → BootstrapManifest → IsSuccess=true

No admin rights
    → Adaptive Mode → AdaptiveManifest → IsSuccess=true

Partial bootstrap (some fields failed after retries)
    → BootstrapManifest.FieldsFailed non-empty → IsSuccess=true (run continues)
```

---

## SK Step State

### `AdoPreflightStepState`
**Namespace**: `DBAIAzure.Processes.Steps`  
**File**: `src/DBAIAzure.Processes/Steps/AdoTelemetryPreflightStep.cs`

| Property | Type | Notes |
|---|---|---|
| `ManifestPath` | `string?` | Absolute path where manifest was written; null until step completes |
| `Mode` | `PreflightMode?` | Set after successful run |
| `IsComplete` | `bool` | Set to true on both Bootstrap and Adaptive completion |

---

## Default Field Set

The embedded default config (`default-telemetry-config.json`) defines:

### User Story / Product Backlog Item fields (12)

| Display Name | Reference Name | Type | Fallback |
|---|---|---|---|
| AI Session ID | `Custom.AISessionID` | String | `System.Tags` |
| AI Model Used | `Custom.AIModelUsed` | String | `System.Tags` |
| AI Input Tokens | `Custom.AIInputTokens` | Integer | `Microsoft.VSTS.Scheduling.StoryPoints` |
| AI Output Tokens | `Custom.AIOutputTokens` | Integer | log-only |
| AI Cache Tokens | `Custom.AICacheTokens` | Integer | log-only |
| AI Estimated Cost USD | `Custom.AIEstimatedCostUSD` | Double | log-only |
| AI Session Duration Sec | `Custom.AISessionDurationSec` | Integer | log-only |
| AI Tool Calls | `Custom.AIToolCalls` | Integer | log-only |
| AI Tool Accept Rate Pct | `Custom.AIToolAcceptRatePct` | Double | log-only |
| AI API Errors | `Custom.AIAPIErrors` | Integer | log-only |
| AI Cache Hit Rate Pct | `Custom.AICacheHitRatePct` | Double | log-only |
| Speckit Phase | `Custom.SpeckitPhase` | PicklistString (`Spec`,`Plan`,`Tasks`,`Analyze`,`Implement`) | `System.Tags` |

### Task fields (2)

| Display Name | Reference Name | Type | Fallback |
|---|---|---|---|
| AI Session ID | `Custom.AISessionID` | String | `System.Tags` |
| AI Model Used | `Custom.AIModelUsed` | String | `System.Tags` |
