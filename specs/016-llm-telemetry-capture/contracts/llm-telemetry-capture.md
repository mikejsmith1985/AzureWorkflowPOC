# Contracts: Accurate AI Usage Telemetry Capture

Internal C# seams (this is a .NET solution, no external API surface). New/changed contracts only.

## C1 — `ILlmUsageReporter` (new, `DBAIAzure.Core.Interfaces`)

```csharp
/// Receives the usage of every LLM call so it can be recorded against the current run. Optional on the
/// connector (null => no-op), keeping the Connectors project free of an observer/run-context dependency.
public interface ILlmUsageReporter
{
    void Report(LlmUsage usage);
}
```

- **Contract**: `Report` MUST NOT throw (best-effort, FR-010) and MUST be cheap (fire-and-forget to
  observers). Called once per Messages API response — on success *and* on failure.

## C2 — Connector seam (`AnthropicChatCompletionService`)

- Constructor gains an optional `ILlmUsageReporter? usageReporter = null`.
- `AnthropicResponse` wire model gains `Usage` (`input_tokens`, `output_tokens`,
  `cache_read_input_tokens`, `cache_creation_input_tokens`) and `Model`.
- Both `GetChatMessageContentsAsync` and `GetStructuredAsync`:
  - On success: parse usage, call `usageReporter?.Report(new LlmUsage(... IsError:false ...))`.
  - On non-success/exception: `usageReporter?.Report(new LlmUsage(IsError:true, zero tokens))` then
    rethrow (propagation unchanged).
- `GetChatMessageContentsAsync` ALSO populates `ChatMessageContent.Metadata["Usage"]`/`["ModelId"]`
  (back-compat for the existing filter and the Run History detail page).

## C3 — `LlmRunContext` (new, replaces filter-local `CurrentRunId`)

```csharp
public static class LlmRunContext
{
    public static readonly AsyncLocal<string?> CurrentRunId = new();
}
```

- Set by `WorkflowExecutionOrchestrator` (runner) and the phase-handler orchestrator before their
  kernels/validation run. `WorkflowFunctionInvocationFilter.CurrentRunId` is kept as a forwarding alias
  to avoid breaking existing references, or migrated in the same change.

## C4 — Web usage reporter implementation

- Records a `WorkflowExecutionEvent` (`EventType = LlmCallCompleted`, `Outcome = "success"|"error"`,
  the four token fields, model, duration, `RunId = LlmRunContext.CurrentRunId ?? "unknown"`) via the
  registered `IWorkflowObserver` fan-out. Never throws.

## C5 — Aggregate + write-back (extends #42)

- `IRunTelemetrySource`/`SqlRunTelemetrySource`: query widened to all `LlmCallCompleted` events for the
  run; project the two new cache columns + error outcome into `LlmTelemetrySample`.
- `RunTelemetryAggregate`: new `CacheReadTokens`, `CacheCreationTokens`, `ErrorCount`, derived
  `CacheHitRatePct` (see data-model §5).
- `TelemetryWriteBackService`: writes `Custom.AICacheTokens`, `Custom.AICacheHitRatePct`,
  `Custom.AIAPIErrors` when sourced; never writes `Custom.AIToolAcceptRatePct`.

## Test contracts (unit, mocked — Article V)

| Unit | Asserts |
|------|---------|
| Anthropic usage parse | `usage` (incl. both cache fields) + model parsed from a canned response body |
| Connector reporter | reporter receives correct `LlmUsage` on success; `IsError=true` on a non-2xx |
| `RunTelemetryAggregate.FromSamples` | cache sums, error count, `LlmCallCount` excludes errors, hit-rate formula + zero-input → null |
| `ModelPricing.EstimateCostUsd` | cost includes cache-read + cache-write contributions |
| `TelemetryWriteBackService` | new fields written when sourced; cache/error omitted when zero; tool-accept never written |
