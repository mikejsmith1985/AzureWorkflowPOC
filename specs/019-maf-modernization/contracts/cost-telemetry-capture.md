# Contract: Cost / Telemetry Capture + Observability

**Owner**: `DBAIAzure.Web` (+ `DBAIAzure.Core` records). **Basis**: research.md D8/D9.

## Cost capture (replaces the two SK kernel filters)

- **`CostCapturingChatClient : DelegatingChatClient`** in the `ChatClientBuilder` pipeline:
  - reads `ChatResponse.Usage` (`UsageDetails.InputTokenCount` / `OutputTokenCount` / `TotalTokenCount`);
    for **streaming**, reads the `UsageContent` in the final `ChatResponseUpdate`;
  - hashes the incoming `IEnumerable<ChatMessage>` (the fully-rendered prompt) — replaces `IPromptRenderFilter`;
  - tags every usage record with the active **provider** + **model** (FR-009e).
- Existing **cost ledger, binding key, and ingest are reused unchanged** (spec-016/017). Token usage lives
  on the model call in MAF/M.E.AI, so this is the correct seam (not a function hook).
- SK `IFunctionInvocationFilter` + `IPromptRenderFilter` are **removed**.

## Structured output (preserve typed results — FR-011)
- `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(...)` + `GetResponseAsync<T>()` for
  RouteDecision / node realization. Forced-tool (`tool_choice`) via `ChatOptions.RawRepresentationFactory`
  (Anthropic-native), kept out of provider-neutral code.

## Streaming (preserve Run Detail Stream tab — FR-011a)
- `GetStreamingResponseAsync` → `IAsyncEnumerable<ChatResponseUpdate>`; UI streams tokens as today;
  usage captured from the final update.

## Observability (repoint Azure Monitor — FR-013/SC-006)
- Add `.UseOpenTelemetry(SourceName)` on the chat pipeline (and/or `.WithOpenTelemetry(SourceName)` on
  agents — pick one to avoid duplicate spans).
- Register the explicit `SourceName` (or defaults `Experimental.Microsoft.Agents.AI` +
  `Experimental.Microsoft.Extensions.AI`) on **both** tracer and meter providers; **remove**
  `AddSource("Microsoft.SemanticKernel*")`.
- Exporter unchanged: `Azure.Monitor.OpenTelemetry.Exporter`. GenAI semconv provides `gen_ai.usage.*`.

## Acceptance
- Token counts + computed cost equal the pre-migration build for equivalent runs (SC-004), tagged by provider/model.
- No LLM path depends on the retired SK chat-completion service (SC-005).
- Orchestration + model-call spans reach Azure Monitor with no coverage gap (SC-006).
