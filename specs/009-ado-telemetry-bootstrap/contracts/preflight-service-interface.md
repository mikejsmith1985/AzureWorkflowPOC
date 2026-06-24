# Contract: IAdoTelemetryPreflightService

**Namespace**: `DBAIAzure.Core.Interfaces`  
**File**: `src/DBAIAzure.Core/Interfaces/IAdoTelemetryPreflightService.cs`

This is the primary seam between the SK step and the ADO bootstrap logic. All callers (SK step, Blazor "Test Connection" button) go through this interface.

---

## Method

```
Task<PreflightResult> RunPreflightAsync(
    AdoTelemetryFieldConfig config,
    CancellationToken cancellationToken = default
)
```

### Parameters

| Parameter | Type | Notes |
|---|---|---|
| `config` | `AdoTelemetryFieldConfig` | The field config to bootstrap against. Callers load this from the embedded default or a workflow-level override before calling. |
| `cancellationToken` | `CancellationToken` | Propagated to all ADO HTTP calls and file I/O. |

### Return value: `PreflightResult`

| Property | Type | Populated when |
|---|---|---|
| `IsSuccess` | `bool` | Always |
| `ErrorMessage` | `string?` | `IsSuccess = false` only |
| `Manifest` | `PreflightManifestBase?` | `IsSuccess = true` only |

### Outcomes

| Scenario | `IsSuccess` | `Manifest` type | Notes |
|---|---|---|---|
| Config missing (org URL blank) | `false` | null | FR-004 |
| ADO org unreachable | `false` | null | FR-013 |
| Unsupported process type detected | `false` | null | FR-005 |
| Bootstrap Mode complete (all fields created or existing) | `true` | `BootstrapManifest` | |
| Bootstrap Mode partial (some fields failed after 3 retries) | `true` | `BootstrapManifest` with `FieldsFailed` non-empty | FR-014b — run continues |
| Adaptive Mode complete | `true` | `AdaptiveManifest` | |

### Side effects

- Writes the manifest to disk at `<SpecsRoot>/<feature-dir>/.ado-bootstrap-manifest.json` on every `IsSuccess = true` return. The caller does not need to persist the manifest separately.
- Does NOT create work items. Does NOT modify pipeline run state beyond writing the manifest file.

---

## ADO REST endpoints called (in order)

1. `GET {orgUrl}/_apis/process/processes?api-version=7.1` — list inherited processes to detect Agile/Scrum.
2. `GET {orgUrl}/{project}/_apis/work/process/configuration?api-version=7.1` — confirm the project's active process.
3. `GET {orgUrl}/_apis/projects/{project}/_apis/wit/fields?api-version=7.1` — (Adaptive Mode) pull available fields.
4. `GET {orgUrl}/_apis/wit/fields/{referenceName}?api-version=7.1` — (Bootstrap Mode) existence check per field.
5. `POST {orgUrl}/_apis/work/processes/lists?api-version=7.1` — (Bootstrap Mode, picklist fields only) create picklist.
6. `POST {orgUrl}/_apis/wit/fields?api-version=7.1` — (Bootstrap Mode) create org-level field.
7. `POST {orgUrl}/_apis/work/processes/{processId}/workItemTypes/{witRefName}/fields?api-version=7.1` — (Bootstrap Mode) attach field to WIT.

All calls use Basic auth (`:{PAT}` Base64-encoded). PAT is resolved from `IConnectorConfigRepository` with fallback to `IOptions<AzureDevOpsOptions>` (same as `AzureDevOpsBoardsClient`).

---

## Manifest file format

`BootstrapManifest` (written to `.ado-bootstrap-manifest.json`):

```json
{
  "mode": "bootstrap",
  "timestamp": "2026-06-23T14:30:00Z",
  "orgUrl": "https://dev.azure.com/mikejsmith1985rll",
  "project": "MyProject",
  "processType": "Agile",
  "fieldsCreated": ["Custom.AISessionID", "Custom.AIModelUsed"],
  "fieldsExisting": ["Custom.AIInputTokens"],
  "fieldsFailed": [
    { "referenceName": "Custom.AIOutputTokens", "error": "429 Too Many Requests", "attemptsExhausted": 3 }
  ],
  "mappingStrategy": "preferred"
}
```

`AdaptiveManifest` (written to `.ado-bootstrap-manifest.json`):

```json
{
  "mode": "adaptive",
  "timestamp": "2026-06-23T14:30:00Z",
  "orgUrl": "https://dev.azure.com/mikejsmith1985rll",
  "project": "MyProject",
  "processType": "Scrum",
  "mapping": {
    "Custom.AISessionID": "System.Tags",
    "Custom.AIModelUsed": "System.Tags",
    "Custom.AIInputTokens": "Microsoft.VSTS.Scheduling.StoryPoints"
  },
  "unmatchedFields": [],
  "logOnlyFields": [
    "Custom.AIEstimatedCostUSD",
    "Custom.AIOutputTokens",
    "Custom.AICacheTokens"
  ]
}
```

---

## Tags fallback encoding

When writing multiple telemetry values to `System.Tags` in Adaptive Mode:

```
ai-session:abc123 | ai-model:claude-sonnet-4-6 | ai-phase:Spec
```

- Separator: ` | ` (space-pipe-space)
- Key format: lowercase, hyphenated, `ai-` prefix
- Values appended to existing tags; existing non-AI tags preserved
