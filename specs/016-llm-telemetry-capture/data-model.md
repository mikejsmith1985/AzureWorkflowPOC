# Phase 1 Data Model: Accurate AI Usage Telemetry Capture

All changes are additive. New fields are nullable so existing rows/events remain valid.

## 1. `LlmUsage` (new — `DBAIAzure.Core.Models.AdoTelemetry`)

Raw per-call usage reported by the connector to the usage reporter.

| Field | Type | Notes |
|-------|------|-------|
| `ModelName` | `string?` | Model id from the response (`model`) |
| `InputTokens` | `int` | `usage.input_tokens` (excludes cached) |
| `OutputTokens` | `int` | `usage.output_tokens` |
| `CacheReadTokens` | `int` | `usage.cache_read_input_tokens` |
| `CacheCreationTokens` | `int` | `usage.cache_creation_input_tokens` |
| `IsError` | `bool` | True when the call failed (no token data) |
| `DurationMs` | `long` | Wall-clock of the call |

## 2. `WorkflowExecutionEvent` (extend — `DBAIAzure.Core.Models`)

Add two nullable fields (alongside existing `LlmModelName`, `LlmInputTokens`, `LlmOutputTokens`):

| New field | Type | Notes |
|-----------|------|-------|
| `LlmCacheReadTokens` | `int?` | null for non-AI / no-cache events |
| `LlmCacheCreationTokens` | `int?` | null for non-AI / no-cache events |

Errors reuse the existing `Outcome` field (`"success"` / `"error"`). No new error column.

## 3. `WorkflowExecutionEventEntity` (extend — `DBAIAzure.Storage.Entities`)

Mirror the two new nullable columns: `LlmCacheReadTokens`, `LlmCacheCreationTokens`. Provisioned by
`EnsureCreated` (R6).

## 4. `LlmTelemetrySample` (extend — `DBAIAzure.Core.Models.AdoTelemetry`)

Add to the existing sample record: `CacheReadTokens int`, `CacheCreationTokens int`, `IsError bool`.

## 5. `RunTelemetryAggregate` (extend)

| New field | Type | Derivation |
|-----------|------|------------|
| `CacheReadTokens` | `int` | Σ sample cache-read |
| `CacheCreationTokens` | `int` | Σ sample cache-creation |
| `ErrorCount` | `int` | count of samples with `IsError` |
| `CacheHitRatePct` (derived) | `double?` | `CacheReadTokens / (CacheReadTokens + InputTokens) × 100`; null when total input = 0 |

`FromSamples` updated accordingly. `LlmCallCount` counts non-error samples; `ErrorCount` counts error
samples (a failed call is not a "successful LLM call").

## 6. Write-back field mapping (`TelemetryWriteBackService`)

Adds to the values built for the work item (when a captured source exists):

| Work item field | Source |
|-----------------|--------|
| `Custom.AICacheTokens` | `aggregate.CacheReadTokens` (when > 0) |
| `Custom.AICacheHitRatePct` | `aggregate.CacheHitRatePct` (when not null) |
| `Custom.AIAPIErrors` | `aggregate.ErrorCount` (when > 0) |

Unchanged: existing session id / model / input / output / call count / duration / estimated cost.
**Not written**: `Custom.AIToolAcceptRatePct` (FR-009, out of scope).

## 7. `ModelPricing` (extend)

Add cache multipliers per tier; `EstimateCostUsd` gains cache-read + cache-creation token params so
cost reflects cache usage. Unknown model → null (unchanged).
