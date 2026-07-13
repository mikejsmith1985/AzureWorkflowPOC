# Contract: AI Provider Seam (BYO-AI)

**Consumers**: every LLM-using executor/service. **Owner**: `DBAIAzure.Connectors` + `DBAIAzure.Core`.

## Seam

All model access goes through `Microsoft.Extensions.AI.IChatClient`. Orchestration/step code depends on
`IChatClient` only — never a provider-specific client type (FR-008).

```
IChatClientProvider                      // one per provider id
  string ProviderId { get; }             // e.g. "anthropic"
  IChatClient Create(AiProviderConfig config)   // build from named config + secret references

IChatClientProviderRegistry
  IChatClient ResolveActive()            // reads AI:Provider / AI:Model; default "anthropic"
                                         // throws NamedProviderException (fail-loud, no fallback) on miss
```

- **Default**: `anthropic` via the official `Anthropic` SDK `AnthropicClient(...).AsIChatClient(model)` (D5).
- **Hot-reload**: `HotReloadChatClient : DelegatingChatClient` re-resolves active provider/model from
  current configuration per call (preserves today's per-call key/model reload — FR-009).
- **Pipeline** (composition order): `provider IChatClient → CostCapturingChatClient → OpenTelemetry → (FunctionInvocation)`.
- **Add a provider**: implement `IChatClientProvider`, register it, supply config + secret refs. **No**
  change to pipelines/steps (FR-009b). Secrets by reference only (FR-009c).

## Acceptance
- Only Claude configured → app runs end-to-end; no other AI subscription required (SC-008).
- Switching `AI:Provider` to another registered adapter → same run executes on it, zero code change (SC-008).
- Unknown/misconfigured provider → error naming the provider; no silent fallback (FR-009d).
