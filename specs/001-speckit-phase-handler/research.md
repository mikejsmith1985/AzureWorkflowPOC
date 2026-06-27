# Phase 0 Research: Spec Kit Phase Handler

**Feature**: `specs/001-speckit-phase-handler` · **Date**: 2026-06-15

This document resolves the technical unknowns for the implementation plan. Each section follows
**Decision / Rationale / Alternatives considered**. The Article VII framework-first analysis is
the spine of this research: prefer Semantic Kernel Process Framework (SK PF) primitives and
existing codebase patterns; build custom only against a documented gap.

## 1. Framework-First Gate (Article VII)

| Capability needed | Framework / existing asset | Verdict |
|---|---|---|
| Orchestration & typed events | SK PF `ProcessBuilder`, `KernelProcess`, process events (see `IntakePipelineBuilder`, `Events.cs`) | **Reuse** — new `PhaseHandlerPipelineBuilder` |
| Human-in-the-loop pause/resume | SK PF `IExternalKernelProcessMessageChannel` + proxy step (see `HitlExternalChannel`) | **Reuse** — new `ApprovalExternalChannel` |
| Structured LLM output | Anthropic Messages API **forced tool use** (native), via existing `AnthropicChatCompletionService` | **Extend** — add a non-streaming structured method; closes a current drift |
| HTTP signal intake | ASP.NET Core controllers (see `WebhookController`) | **Reuse** — new controller, same shared-secret pattern |
| Run tracking / persistence | `IRunRepository` + EF Core `PipelineDbContext` | **Reuse / extend** — add a phase-run record |
| Background run management | `PipelineOrchestrator` pattern (background loop + event stream + HITL gate) | **Reuse pattern** — new `PhaseHandlerOrchestrator` |
| **Azure DevOps Boards write** | **Not provided by any project framework** | **Custom (documented gap)** |
| Artifact file reading | `System.IO` | Custom (trivial) |

**Drift justifications (the only custom infrastructure):**
- **Azure DevOps Boards client** — Neither SK nor any existing project component can create/update
  work items on Azure DevOps Boards. This is a genuine external-system integration gap. We use the
  **official Microsoft client library** rather than hand-rolling REST, and isolate it behind a
  narrow project interface (`IBoardsClient`) for testability.
- **Structured output method** — The current `AnthropicChatCompletionService` only exposes
  free-text completion (and *fake* streaming). Article VII requires schema-bound output, so we add
  a structured method using Anthropic's native tool-use mechanism. This is extending the framework
  seam, not rebuilding it.

**Gate result: PASS.** No bespoke state machine, event bus, pause/resume loop, or DI registry is
introduced.

## 2. Azure DevOps Boards integration

**Decision**: Use **`Microsoft.TeamFoundationServer.Client`** (latest stable `20.256.2`), authenticate
with a **PAT** via `VssBasicCredential` → `VssConnection` → `WorkItemTrackingHttpClient`. Wrap it
behind a project-defined **`IBoardsClient`** interface.

**Rationale**:
- It is the officially recommended .NET client for Work Item Tracking. There is **no `Azure.*` SDK
  replacement** for Boards. It targets `netstandard2.0`, which .NET 8 consumes normally.
- PAT scope required: **Work Items (Read & Write)** = `vso.work_write` (covers create, update,
  discussion comments, and parent/child links).
- `WorkItemTrackingHttpClient` is a concrete, connection-bound class with a huge surface; mocking it
  directly is brittle. A narrow `IBoardsClient` gives a clean unit-test seam and keeps SK steps free
  of SDK types.

**Verified API facts** (used by the implementation):
- **Create**: `CreateWorkItemAsync(JsonPatchDocument, project, type)` — work item **type is the string
  parameter** (`"Epic"`/`"Task"`/`"Bug"`), *not* a `System.WorkItemType` field patch. Fields
  (`System.Title`, `System.Description`, optional `System.AreaPath`/`System.IterationPath`) are
  `JsonPatchOperation` entries with `Operation.Add` and path `/fields/<refName>`.
- **Update**: `UpdateWorkItemAsync(JsonPatchDocument, id)`. Every update creates a new **immutable
  revision** automatically; history is read via `GetRevisionsAsync(id)`. (Optionally guard with an
  `Operation.Test` on `/rev` for optimistic concurrency.)
- **Append discussion comment (append-only)**: add `System.History` via a `JsonPatchOperation` — each
  write **appends** a comment and creates a revision; it never overwrites. This is the robust,
  documented path and can be batched into the same update call.
- **Parent link (child → Epic)**: append to `/relations/-` a relation with
  `rel = "System.LinkTypes.Hierarchy-Reverse"` (Reverse = points to the **parent**) and the parent's
  full `url`.
- **Idempotent lookup**: store the created work item **id locally** keyed by `(feature, phase)` as the
  primary idempotency key; WIQL (`QueryByWiqlAsync`) on a structured tag/custom field is the
  reconciliation fallback.

**Alternatives considered**: `Microsoft.TeamFoundationServer.ExtendedClient` (legacy SOAP, no
netstandard — rejected); raw REST via `HttpClient` (rejected — re-implements the SDK, more error
surface, no value); WIQL-only idempotency (rejected as sole mechanism — susceptible to query indexing
lag and duplicate creation under retries).

## 3. Structured LLM output (closing the Article VII drift)

**Decision**: Add a dedicated **non-streaming** structured method to `AnthropicChatCompletionService`
that uses Anthropic **forced tool use** — `tools: [{ name, description, input_schema }]` plus
`tool_choice: { type: "tool", name }` — and binds the returned `tool_use` block's `input` directly to
a typed record (`PhaseValidationResult`). No markdown stripping, no "return ONLY JSON" prompting.

**Rationale**:
- The result arrives as a structured JSON object in the `tool_use` block's `input`, already matching
  the declared `input_schema`. `tool_choice` forcing removes the "model replied with prose" failure
  mode. This satisfies Article VII ("request JSON via a response schema and bind to a typed record").
- Works on the already-configured `anthropic-version: 2023-06-01` with no SDK and no beta header.
- The existing "streaming" path is **fake streaming** (it buffers a single non-streaming call), so we
  lose nothing by making the structured call explicitly non-streaming. A forced single `tool_use`
  block has nothing meaningful to stream.

**Implementation notes**:
- The current private wire records (`AnthropicResponse`, `AnthropicContentBlock`) model only
  `type`/`text`. Extend them (or add parallel types) with `id`, `name`, and `JsonElement? input` so
  the `tool_use` block can be located and its `input` deserialized with the existing `JsonOpts`.
- `max_tokens` is currently hardcoded to 4096 — parameterize it for the structured method.
- The existing free-text path stays in place for the legacy ticket pipeline; only the new phase
  handler uses the structured method (the ticket pipeline migration is out of scope here).

**Alternatives considered**: Anthropic `output_config.format: { type: "json_schema", schema }`
(schema-constrained text — also native and valid, but newer and returns JSON-as-text you still
deserialize; kept as a fallback). Assistant-turn prefill with `{` (rejected — returns 400 on current
Opus/Sonnet models). Keeping free-text parse (rejected — exactly the Article VII anti-pattern).

## 4. Running a second SK process alongside the existing ticket pipeline

**Decision**: Add an independent process (`PhaseHandlerPipelineBuilder`) and an independent
`PhaseHandlerOrchestrator`, both registered as singletons in `Program.cs`, sharing the same
`AnthropicChatCompletionService` configuration via a kernel factory. The existing `PipelineOrchestrator`
and ticket pipeline are **not modified** (satisfies FR-017).

**Rationale**: SK processes are self-contained `KernelProcess` graphs; multiple can coexist in one
host. Mirroring the proven `PipelineOrchestrator` shape (background `Task`, event stream for the UI,
a `TaskCompletionSource` HITL gate) keeps the codebase consistent and avoids a risky refactor of the
working ticket pipeline.

**Alternatives considered**: generalizing `PipelineOrchestrator`/`IRunRepository` to be generic over
state (rejected for this iteration — large refactor of working code, higher regression risk against
FR-017; can be revisited later).

## 5. Persistence of phase-handler runs

**Decision**: Add a small `PhaseRunRecord` entity (+ `DbSet`) to the existing `PipelineDbContext`,
recording feature, phase, status, validation summary, decision, created work item id(s) and url(s),
and timestamps. The local work item id stored here doubles as the idempotency key.

**Rationale**: FR-011 (auditability) and FR-013 (idempotent upsert via stored id) both need durable
per-(feature, phase) state. Reusing `PipelineDbContext` keeps one database and one migration path;
`EnsureCreatedAsync` already runs at startup.

**Alternatives considered**: reuse `RunRecord`/`TicketState` (rejected — wrong shape, would pollute the
ticket model); a separate database (rejected — unnecessary operational surface for a POC).

## 6. Inbound signal & approval callback contracts

**Decision**: Two endpoints on a new `SpecKitWebhookController`, both guarded by a shared secret header
(mirroring `WebhookController`'s `X-SNow-Secret`):
- `POST /api/webhook/speckit-phase` — phase-complete signal → starts a `PhaseHandlerOrchestrator` run.
- `POST /api/webhook/speckit-approval` — decision-card callback → resumes the paused run via the
  approval external channel.

**Rationale**: Matches the user's description ("HTTP POST signals" + "approval signal from the HITL
decision card") and the existing webhook authorization pattern. The approval callback mirrors the
existing HITL resume mechanism (external event resumes a paused process).

**Alternatives considered**: reuse the in-app Blazor portal for approval (rejected as the primary path
— the decision card lives in Forge Terminal; the portal remains available as a manual fallback for
observing/replaying runs). Polling for the decision (rejected — Article VII forbids a custom polling
loop when the external-channel resume exists).
