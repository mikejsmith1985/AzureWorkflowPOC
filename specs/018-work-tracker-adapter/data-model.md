# Phase 1 Data Model: Work-Tracker Adapter

## 1. `WorkItemRef` (new value type)

An opaque reference to a tracker item, hiding numeric (ADO) vs string-key (Jira) identity from the core.

| Field | Type | Notes |
|-------|------|-------|
| `Value` | `string` | Native id in string form: ADO `"4242"`, Jira `"PROJ-123"` |

- Adapters own parse/format (`AzureDevOpsWorkTrackerAdapter` ↔ `int`). Equality by `Value`.
- Serialises directly into `BindingWorkItemMap.WorkItemId` (widened `int → string`).

## 2. `LogicalField` (new — tracker-neutral field names)

Constants the cost/telemetry layers use instead of `Custom.*`. Each adapter maps these to native refs.

```
AISessionID, AIModelUsed, AITriggeredBy, CostBindingKey,
AIRuntimeCostUSD, AIDevCostUSD, AIInputTokens, AIOutputTokens, AICacheTokens,
AIEstimatedCostUSD, AISessionDurationSec, AIToolCalls, AIToolAcceptRatePct,
AIAPIErrors, AICacheHitRatePct, SpeckitPhase
```

- ADO mapping: `Custom.<logical>` (its existing reference names — **live ADO fields unchanged**).
- Jira mapping: `customfield_NNNNN`, resolved by field display name at provision time and cached.
- `default-telemetry-config.json` field `reference` values become these logical names (the ADO adapter
  re-prefixes to `Custom.*`, so no live regression).

## 3. `WorkItemType` (logical)

`Epic | UserStory | Task | Bug` — the four logical types the pipeline creates. Each adapter maps to its
native type name (ADO `Microsoft.VSTS.WorkItemTypes.*` resolved via the existing preflight; Jira issue
type names `Epic`/`Story`/`Task`/`Bug`/`Sub-task`).

## 4. `ProvisioningResult` (new)

| Field | Type | Notes |
|-------|------|-------|
| `FieldsReady` | `IReadOnlyList<string>` | logical fields now usable on the relevant item types |
| `FieldsFailed` | `IReadOnlyList<FieldFailure>` | name + actionable reason |
| `Mode` | `string` | tracker-specific provisioning mode (e.g. ADO Bootstrap/Adaptive; Jira ContextScreen) |
| `IsSuccess` | `bool` | true when no required field failed |

## 5. `RollupCapability` (new)

| Field | Type | Notes |
|-------|------|-------|
| `Kind` | enum | `Native` \| `RequiresAddOn` \| `None` |
| `NativeTool` | `string?` | e.g. "ADO Analytics", "Jira Advanced Roadmaps" |
| `Notice` | `string?` | operator-facing message when not `Native` (FR-010, SC-005) |

## 6. `BindingWorkItemMap` (existing — modified)

`WorkItemId` widens **`int` → `string`** to hold a `WorkItemRef.Value` for any tracker. `IBindingWorkItemMap`
signatures change `int → WorkItemRef`.

**`CostLedgerEntry.WorkItemId` also widens `int? → string?`** — it records the anchor work item, which on
Jira is a string key (`PROJ-123`) that an `int?` cannot hold. Everything else about the ledger (append-only
records, dimensioned totals, `ICostLedger`) is unchanged.

## 7. Unchanged (tracker-neutral, reused as-is)

- `CostLedgerEntry` / `ICostLedger` — append-only cost records + totals.
- `IBindingKeyMinter` — source-neutral `BIND-*` keys.
- Dev-usage ingest (`POST /api/telemetry/dev-usage`) — resolves via the adapter now, behaviour identical.
- `ModelPricing`, `RunTelemetryAggregate`, the capture path (spec-016).

## Selection / config

The active tracker is the configured work-tracker connector (`ConnectorType` gains `Jira`; `AzureDevOps`
unchanged). `IWorkTrackerAdapterProvider` resolves one active adapter from connector config; its
`GetAdapter(routingContext?)` reserves per-project routing for later (FR-005).
