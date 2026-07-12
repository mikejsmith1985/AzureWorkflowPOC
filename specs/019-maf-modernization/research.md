# Phase 0 Research: Modernize the Agent Stack onto Microsoft Agent Framework (MAF)

**Feature**: `specs/019-maf-modernization` · **Date**: 2026-07-12

This document resolves the technical unknowns for migrating the orchestration core off the experimental
Semantic Kernel (SK) Process Framework (`1.77.0-alpha`, `SKEXP0080`) onto Microsoft Agent Framework
(MAF) 1.0, keeping Claude as the default model, adding bring-your-own-AI, and preserving all behavior.

Each decision is stated as **Decision / Rationale / Alternatives**, grounded in the cited sources.

---

## D1 — Orchestration engine: MAF Workflows replaces the SK Process Framework

**Decision**: Rebuild the three pipelines on **`Microsoft.Agents.AI.Workflows`** (GA; latest **1.13.0**,
2026-07-03). Map SK primitives as:

| SK Process Framework | MAF Workflows (.NET) |
|---|---|
| `ProcessBuilder` | `WorkflowBuilder` (start executor to ctor; `.Build()` validates) |
| `KernelProcessStep` / `KernelProcessStep<TState>` | `Executor` / `Executor<TInput>` (`[MessageHandler]` or `HandleAsync`) |
| `AddStepFromType<T>()` | executor **instances** passed to the builder |
| `.OnEvent("id").SendEventTo(step)` | `AddEdge(src, tgt, condition:)` / `AddSwitch(...AddCase...WithDefault)` |
| emitted event id = **port label** | typed record + **enum**, `AddCase(msg => msg.Port == X, tgt)` |
| `context.EmitEventAsync` | `context.SendMessageAsync(...)` / `YieldOutputAsync(...)` |
| fan-out / fan-in | `AddFanOutEdge` / `AddFanInBarrierEdge` |
| `KernelProcess` local runtime | `InProcessExecution.RunStreamingAsync` / `RunAsync` |

**Rationale**: Workflows is the GA successor with the same graph/event-driven shape the app already uses,
plus a deterministic superstep (BSP/Pregel) model that makes checkpointing reliable. Routing on a typed
payload value (via `AddSwitch` + an enum) preserves the app's `KnownPortLabels` routing exactly.

**Alternatives**: (a) *Stay on SK Process Framework* — rejected: it is the experimental alpha this
feature exists to remove (FR-003/SC-002). (b) *MAF declarative YAML workflows*
(`Microsoft.Agents.AI.Workflows.Declarative`) — rejected: preview, and the app builds graphs
imperatively from a persisted `WorkflowDefinition` at runtime, which the fluent builder serves directly.
(c) *`Microsoft.Agents.AI.DurableTask`* — rejected: prerelease; violates the zero-prerelease gate.

Sources: [workflows/workflows](https://learn.microsoft.com/en-us/agent-framework/workflows/workflows),
[workflows/edges](https://learn.microsoft.com/en-us/agent-framework/workflows/edges),
[NuGet Microsoft.Agents.AI.Workflows](https://www.nuget.org/packages/Microsoft.Agents.AI.Workflows/).

---

## D2 — Human-in-the-loop: `RequestPort` + `RequestInfoEvent` replaces proxy-step + external channel

**Decision**: Replace each HITL surface (intake console prompt, phase-handler approval, visual-builder
Review Queue) with a **`RequestPort`** node. The workflow pauses and emits a **`RequestInfoEvent`**; the
host resolves it with **`StreamingRun.SendResponseAsync(req.Request.CreateResponse(decision))`**. The two
SK external channels (`HitlExternalChannel`, `ApprovalExternalChannel`) and all proxy steps are removed.

**Rationale**: `RequestPort` is MAF's first-class request/response HITL primitive — there is no separate
channel interface to implement, and **pending requests are captured in checkpoints and re-emitted on
restore**, which is exactly the durable pause/resume the app needs (FR-005/FR-006). The app keeps its
own Review Queue, SignalR updates, and `TaskCompletionSource` gating as the UI/host layer; only the SK
proxy plumbing underneath changes.

**Alternatives**: *Keep the bespoke suspend-restart-from-fresh-run pattern on top of MAF* — rejected:
redundant once `RequestPort` + checkpoint restore provide true in-place resume.

Source: [workflows/human-in-the-loop](https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop).

---

## D3 — Durable persistence: custom `ICheckpointStore<JsonElement>` over the existing database

**Decision**: Use MAF's built-in checkpointing via **`CheckpointManager.CreateJson(store, options)`**,
supplying a **custom `ICheckpointStore<JsonElement>`** (namespace
`Microsoft.Agents.AI.Workflows.Checkpointing`) backed by the app's existing EF Core store
(SQLite / SQL Server). Executors persist per-step state through `OnCheckpointingAsync` /
`OnCheckpointRestoredAsync`. Resume across restart uses `InProcessExecution.ResumeStreamingAsync`.

**Rationale**: Checkpointing is GA and pluggable; a DB-backed store keeps runs durable exactly as today
and lets the existing rehydration-on-startup service (`WorkflowRunRehydrationService`) resume paused runs
from checkpoints instead of SK state. `FileSystemJsonCheckpointStore` is the shipped example to pattern
the DB store after.

**Alternatives**: (a) *In-memory `CheckpointManager.CreateInMemory()`* — rejected: not durable. (b)
*`Microsoft.Agents.AI.DurableTask`* — rejected: prerelease (zero-prerelease gate).

Sources: [workflows/checkpoints](https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints),
[CheckpointManager API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.agents.ai.workflows.checkpointmanager?view=agent-framework-dotnet-latest).

---

## D4 — One-time migration of SK-paused runs (FR-006a)

**Decision**: Ship a **one-time migration** in the cutover release that reads runs persisted in a paused
state under SK and writes an equivalent MAF **checkpoint** (into the D3 store) with the outstanding
`RequestPort` request reconstructed, so MAF resumes them in place. Verify against a representative set of
real paused-run records (SC-009).

**Rationale**: The atomic cutover (Clarifications Q1) means paused runs may exist at the switch; the spec
requires in-place resume with no lost approvals. Because both the old and new state are the app's own
persisted run records, the converter is a data transform the app owns, run once at deploy.

**Alternatives**: drain-before-cutover / best-effort manual — rejected in clarification (chose
auto-migrate).

---

## D5 — LLM access: official `Anthropic` SDK `.AsIChatClient()` behind a provider-neutral seam

**Decision**: Reach the model through **`Microsoft.Extensions.AI` `IChatClient`**. Use the **official
`Anthropic` NuGet SDK** (v12.x; depends on `Microsoft.Extensions.AI.Abstractions`) via
`anthropicClient.AsIChatClient("claude-...")`, wrapped for agents with MAF `ChatClientAgent` /
`AsAIAgent`. Retire the hand-rolled `AnthropicChatCompletionService`; the app's own
`IStructuredCompletionService` contract is re-expressed on top of `IChatClient` (D7).

**Do NOT use `Microsoft.Agents.AI.Anthropic`** — it is still `--prerelease` and would violate the
zero-prerelease execution-path gate (FR-003/SC-002). The official `Anthropic` SDK is a stable release
line and satisfies the gate while giving native `IChatClient`, streaming, tools, and usage.

**Rationale**: Standardizing on `IChatClient` is what makes the orchestration/step code
provider-neutral, keeps Claude as the vendor, and unlocks BYO-AI (D6) as configuration. Streaming, tool
calling, and `UsageDetails` all come through M.E.AI for free.

**Alternatives**: (a) *Keep the hand-rolled HttpClient connector, adapt to `IChatClient`* — viable
fallback if the official SDK lacks a needed behavior, but redundant given the SDK already implements
`IChatClient`. (b) *`Microsoft.Agents.AI.Anthropic`* — rejected: prerelease.

Sources: [MAF Anthropic provider](https://learn.microsoft.com/en-us/agent-framework/agents/providers/anthropic),
[NuGet: Anthropic](https://www.nuget.org/packages/Anthropic),
[Use the IChatClient interface](https://learn.microsoft.com/en-us/dotnet/ai/ichatclient).

---

## D6 — Bring-your-own-AI: a provider registry keyed by configuration + hot-reload delegating client

**Decision**: Introduce an **`IChatClientProvider` registry** — one factory per provider id
(`anthropic` shipped; others addable) producing an `IChatClient` from named configuration + secrets. A
single active provider/model is selected **per deployment instance** by config (`AI:Provider`,
`AI:Model`), defaulting to `anthropic`. Preserve per-call model/key **hot-reload** with a
**`HotReloadChatClient : DelegatingChatClient`** that re-resolves the active provider/model from
current configuration on each call. Misconfiguration fails loud, naming the provider; no silent fallback.

**Rationale**: The provider-neutral `IChatClient` seam means adding a provider is a factory + config,
never a change to pipelines/steps (FR-009b). A `DelegatingChatClient` is the idiomatic hot-reload/
selection seam and composes with the cost-capture and OpenTelemetry middleware (D8).

**Alternatives**: (a) *Per-workflow/per-node/per-run provider selection* — deferred (Clarifications Q4:
per-instance global). (b) *Compile-time provider choice* — rejected: not BYO.

Sources: [Use the IChatClient interface — pipelines/DelegatingChatClient](https://learn.microsoft.com/en-us/dotnet/ai/ichatclient).

---

## D7 — Structured output: `ChatResponseFormat.ForJsonSchema` + forced-tool via raw-representation

**Decision**: Re-express `IStructuredCompletionService` (route decisions, node realization) using M.E.AI
structured output — `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(...)` with the typed
`GetResponseAsync<T>()` extension. Where the current forced-tool-use (`tool_choice`) behavior is
required, set Anthropic's provider-native parameter through **`ChatOptions.RawRepresentationFactory`** /
`AdditionalProperties`.

**Rationale**: Produces the same typed results the app binds today (`RouteDecision`, realization
records) and keeps schema enforcement. The one non-portable bit — *forcing* a specific tool — rides the
documented raw-representation escape hatch, so it is preserved without leaking into provider-neutral code.

**Alternatives**: *hand-parse free-text JSON* — rejected (constitution Article VII: use structured
output).

Sources: [ChatOptions.ResponseFormat](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatoptions.responseformat?view=net-10.0-pp),
[GetResponseAsync<T>](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.chatclientstructuredoutputextensions.getresponseasync?view=net-9.0-pp),
[ichatclient (tools / raw representation)](https://learn.microsoft.com/en-us/dotnet/ai/ichatclient).

---

## D8 — Cost/telemetry capture: `DelegatingChatClient` middleware replaces the two SK filters

**Decision**: Replace `IFunctionInvocationFilter` (token capture) and `IPromptRenderFilter` (prompt hash)
with a single **`CostCapturingChatClient : DelegatingChatClient`** registered in the `ChatClientBuilder`
pipeline. It reads `ChatResponse.Usage` (`UsageDetails.InputTokenCount/OutputTokenCount`) — and, for
streaming, the `UsageContent` in the final `ChatResponseUpdate` — and hashes the incoming
`IEnumerable<ChatMessage>` (the fully-rendered prompt). Each record is tagged with the active provider
and model (FR-009e). The existing cost ledger, binding key, and ingest are reused unchanged.

**Rationale**: In MAF/M.E.AI token usage lives on the **model call**, not a function hook, so the
`IChatClient` layer is the correct seam. This keeps spec-016/017 accounting identical and is
provider-independent.

**Alternatives**: *derive cost only from OpenTelemetry `gen_ai.usage.*` attributes* — rejected as the
source of truth for the ledger (better for dashboards); the in-process `UsageDetails` read is exact and
keeps the binding-key logic in one place.

Sources: [M.E.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai),
[DelegatingChatClient](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.delegatingchatclient),
[MAF middleware](https://learn.microsoft.com/en-us/agent-framework/agents/middleware/).

---

## D9 — Observability: `.UseOpenTelemetry(SourceName)` and repointed Azure Monitor sources

**Decision**: Add `.UseOpenTelemetry(SourceName)` on the chat-client pipeline (and/or
`.WithOpenTelemetry(SourceName)` on agents — choose one to avoid duplicate spans). Repoint the Azure
Monitor wiring from `AddSource("Microsoft.SemanticKernel*")` to an explicit **`SourceName`** constant
passed to the extension (or the defaults `Experimental.Microsoft.Agents.AI` and
`Experimental.Microsoft.Extensions.AI`), registered on **both** the tracer and meter providers. The
exporter package (`Azure.Monitor.OpenTelemetry.Exporter`) is unchanged.

**Rationale**: MAF/M.E.AI follow the OpenTelemetry **GenAI semantic conventions** (`gen_ai.usage.*`,
`gen_ai.client.token.usage`, etc.), so traces/metrics keep flowing to Azure Monitor with no coverage gap
(FR-013/SC-006). There is no SK-style wildcard source; register explicit names.

**Alternatives**: *custom middleware only, no OTel* — rejected: loses standard spans/metrics.

Sources: [MAF Observability](https://learn.microsoft.com/en-us/agent-framework/agents/observability),
[OpenTelemetryChatClient](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.ai.opentelemetrychatclient).

---

## D10 — Cutover strategy: incremental development, atomic production switch

**Decision**: Migrate pipeline-by-pipeline **off-production** behind the existing test suite, using SK↔MAF
interop where helpful, then flip **all three** pipelines to MAF in a **single release** with **no
dual-runtime routing in production** (Clarifications Q1). The full suite (SC-001) plus the paused-run
migration (D4) and the ≤10% performance budget (SC-010) are the release gates.

**Rationale**: Atomic cutover yields the simplest end state (a single stack, FR-016) and avoids
permanent hybrid routing, while incremental dev keeps risk low.

**Alternatives**: feature-flag coexistence in prod — rejected in clarification (chose atomic).

---

## Package summary (target execution path — all GA/stable)

| Package | Role | Status |
|---|---|---|
| `Microsoft.Agents.AI` | Core agents (`AIAgent`, `ChatClientAgent`) | GA 1.x |
| `Microsoft.Agents.AI.Workflows` | Workflow graph engine + checkpointing | GA (1.13.0) |
| `Microsoft.Extensions.AI` / `.Abstractions` | `IChatClient`, middleware, OTel | GA |
| `Anthropic` (official SDK) | Claude via `.AsIChatClient()` | stable release line (v12.x) |
| `Azure.Monitor.OpenTelemetry.Exporter` | Azure Monitor export (unchanged) | GA |

**Explicitly excluded** (prerelease → would violate FR-003/SC-002): `Microsoft.Agents.AI.Anthropic`,
`Microsoft.Agents.AI.DurableTask`, `Microsoft.Agents.AI.Workflows.Declarative`.

## Resolved unknowns

All Technical Context unknowns are resolved above; **no `NEEDS CLARIFICATION` remain**. The one residual
risk to watch during implementation: MAF/M.E.AI GenAI semantic conventions are marked *experimental
(v1.41)* — pin `Microsoft.Extensions.AI` and re-verify token-attribute names on upgrade (does not affect
the in-process `UsageDetails` read used for the ledger).
