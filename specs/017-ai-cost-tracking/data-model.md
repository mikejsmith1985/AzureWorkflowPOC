# Phase 1 Data Model: Two-Dimensional AI Cost Tracking

All additions are additive/nullable. The ledger is append-only; per-item fields are projections.

## 1. Binding key

- **Carried on** `PhaseHandlerState.CostBindingKey` (`string`) — minted at intake.
- **Form**: source-neutral, branch-safe, e.g. `BIND-<base32>` (no slashes/spaces; resolvable as a
  branch segment and an ADO query value).
- **Persisted to**: ADO work item `Custom.CostBindingKey` (queryable) + the originating ServiceNow
  ticket (a field on the SNow record). One key spans both systems.

## 2. `CostLedgerEntry` (new table — source of truth)

| Field | Type | Notes |
|-------|------|-------|
| `Id` | `Guid` | PK |
| `BindingKey` | `string` | indexed; join key |
| `Dimension` | `string`/enum | `Runtime` \| `Development` |
| `WorkItemId` | `int?` | the anchor work item; null when unattributed |
| `ModelName` | `string?` | |
| `InputTokens` / `OutputTokens` / `CacheReadTokens` | `int` | |
| `CostUsd` | `double` | priced via `ModelPricing` |
| `OccurredAt` | `DateTimeOffset` | |
| `SourceId` | `string` | run id (runtime) or session id (development) |
| `IsUnattributed` | `bool` | true when the binding key didn't resolve (FR-010) |

**Invariants**: append-only (no updates/deletes); per-ticket total per dimension = `SUM(CostUsd)` over
`BindingKey` + `Dimension`. Runtime: **one** entry per run (anchor work item). Development: one entry
per ingested session usage post.

## 2b. `BindingWorkItemMap` (new table — resolution, remediation C1)

The pipeline knows `bindingKey → workItemId` at creation, so it persists the mapping **locally** rather
than querying ADO per ingest. This is how dev-usage resolves a supplied key to a work item (FR-003/005).

| Field | Type | Notes |
|-------|------|-------|
| `BindingKey` | `string` | PK / unique |
| `WorkItemId` | `int` | the anchor work item the key was minted for |
| `CreatedAt` | `DateTimeOffset` | |

A supplied key absent from this map → the dev-usage entry is recorded `IsUnattributed = true` (FR-010).

## 3. Work-item cost projection (for ADO Analytics)

Cumulative numeric custom fields, recomputed from the ledger on each write so Analytics can roll them up:

| Field | Source |
|-------|--------|
| `Custom.AIRuntimeCostUSD` | Σ ledger `Runtime` cost for the item's binding key |
| `Custom.AIDevCostUSD` | Σ ledger `Development` cost for the item's binding key |

(The spec-016 token/cache fields remain; these add the two **cumulative cost** dimensions.)

## 4. `DevUsageIngestPayload` (new — ingest DTO)

| Field | Type | Notes |
|-------|------|-------|
| `binding_key` | `string` | required |
| `model` | `string?` | |
| `input_tokens` / `output_tokens` / `cache_read_tokens` | `int` | |
| `cost_usd` | `double?` | optional; re-priced from tokens when absent (R5) |
| `session_id` | `string` | the agent session id |
| `occurred_at` | `DateTimeOffset?` | defaults to receipt time |

## 5. State transitions (binding lifecycle)

```
intake → mint CostBindingKey on state
       → ValidationStep asserts present  (DoR gate; fail if missing)
       → approval
       → CreateWorkItemStep: write key to ADO (Custom.CostBindingKey) + ServiceNow ticket
       → runtime ledger entry appended (anchor work item)
   ... independently, anytime ...
       → dev-usage ingest appends Development entries by binding key
       → per-item cumulative cost fields recomputed → ADO Analytics rolls up
```
