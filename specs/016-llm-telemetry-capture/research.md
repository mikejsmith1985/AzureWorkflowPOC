# Phase 0 Research: Accurate AI Usage Telemetry Capture

## R1 — Why telemetry is empty today

**Finding**: `AnthropicChatCompletionService.GetChatMessageContentsAsync` returns
`new ChatMessageContent(AuthorRole.Assistant, text)` with **no metadata**, and the `AnthropicResponse`
wire record deserializes only `content` — `usage` is dropped. `WorkflowFunctionInvocationFilter` reads
`Result.Metadata["Usage"]`/`["ModelId"]`, which are never populated → tokens/model are always null on
the Anthropic path. The structured (phase-handler) path discards usage entirely.

**Decision**: Parse `usage` in the connector and report it through a dedicated seam (R2), rather than
relying on the reflective metadata filter.

## R2 — Capturing both runner and phase-handler calls (Framework-First)

**Decision**: Introduce `ILlmUsageReporter` (Core). The connector calls it after every Messages API
response on both `GetChatMessageContentsAsync` and `GetStructuredAsync`. A Web-layer implementation
records a `WorkflowExecutionEvent` via the existing `IWorkflowObserver` fan-out.

**Rationale**: SK's `IFunctionInvocationFilter` only observes kernel *function* invocations; the
phase-handler validation calls `IStructuredCompletionService.GetStructuredAsync` directly, which the
kernel never wraps. A connector-level reporter is the one place that sees every call and the raw usage.
Optional (nullable) on the connector so `DBAIAzure.Connectors` keeps no observer dependency and stays
unit-testable.

**Alternatives considered**:
- *Keep using the reflective filter and just populate metadata.* Rejected — it still misses the direct
  structured-completion path (phase handler), which is exactly where ADO work items are produced.
- *Change `IStructuredCompletionService` to return `StructuredResult<T>` with usage.* Rejected —
  larger contract churn across all callers and the test fake, for no extra coverage over the reporter.
- *Emit observer events from inside the connector.* Rejected — couples the Connectors project to
  observers and run-context.

## R3 — Run-id correlation for phase-handler events

**Finding**: `WorkflowFunctionInvocationFilter.CurrentRunId` (AsyncLocal) is set only by the workflow
orchestrator. Phase-handler runs carry their id in `PhaseHandlerState.RunId`.

**Decision**: Generalize the ambient run id into `LlmRunContext.CurrentRunId` and set it in both the
workflow orchestrator (as today) and the phase-handler orchestrator before validation runs. The usage
reporter reads it to tag each event; falls back to `"unknown"` when unset.

## R4 — Cache token semantics & hit-rate (from Clarifications)

**Decision**: Capture `cache_read_input_tokens` and `cache_creation_input_tokens` per call.
`Custom.AICacheTokens` = cache-read. Cache-hit rate = `cache_read / (cache_read + input_tokens) × 100`
(omitted when total input is zero). Both cache figures feed cost.

**Rationale**: Anthropic's `input_tokens` already excludes cached tokens, so the denominator
`cache_read + input_tokens` is the true total prompt input. Reads and writes bill differently, so cost
needs both.

## R5 — AI error capture

**Decision**: On a non-success Messages response or transport exception, the connector reports an
`LlmUsage` with `IsError = true` (zero tokens) before propagating, and the reporter records a
`WorkflowExecutionEvent` with `Outcome = "error"`. The aggregate counts error events.

**Rationale**: The connector is the only place that observes the failure with run context. Errors are
counted, never silently dropped; propagation is unchanged so callers still see the exception.

## R6 — Schema provisioning

**Finding**: `PipelineDbContext` uses `EnsureCreated` (no EF migrations). New columns won't appear on a
pre-existing dev DB.

**Decision**: Add the two nullable token columns (additive, safe). Document that dev stores are
recreated to pick them up; ACA deploys provision fresh. No migration framework introduced (matches the
existing approach — Framework-First).

## R7 — ModelPricing cache rates

**Decision**: Extend `ModelPricing` with per-tier cache-read (~0.1× input) and cache-write (~1.25×
input) multipliers as documented approximate rates. Cost = input + output + cache-read + cache-write
contributions. Kept as clearly-labelled estimates (consistent with the existing cost field).
