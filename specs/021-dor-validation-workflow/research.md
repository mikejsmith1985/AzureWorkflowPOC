# Research & Decisions: Intelligent DoR Validation Workflow

Phase 0 output. Consolidates the reuse-vs-build decisions from codebase reconnaissance and resolves every
Technical-Context unknown. Framework-First (Article VII) drove each choice: reuse a MAF/existing primitive
wherever one exists; build custom only against a documented gap, with the justification recorded here.

## D1 — Orchestration & HITL: reuse MAF, do not hand-roll

**Decision**: Model the DoR workflow as a MAF `Workflow` built by a new `MafDorWorkflowFactory`, mirroring
`MafIntakeWorkflowFactory`. Branching uses **predicate edges** (`AddEdge(a, b, cond)`) — the GA API has no
`AddSwitch`. HITL uses `RequestPort.Create<DorState, DorState>(id).BindAsExecutor(allowWrappedRequests:false)`
so the full paused state rides the request. Drive it with the existing `MafWorkflowSession<DorState>`
(`StartAsync`/`DriveAsync`/`RespondAsync`) and the singleton `CheckpointManager` (`EfCheckpointStore`).

**Rationale**: The intake pipeline already proves this exact pattern (suspend on `RequestInfoEvent`, resume via
`SendResponseAsync`, durable across restarts via checkpoints + `PausedRunRehydrationService`). Constitution
Article VII mandates it; spec FR-010 forbids a bespoke pause/resume.

**Alternatives rejected**: Custom state machine + polling loop (violates Article VII); a second in-memory
`TaskCompletionSource` gate like the visual orchestrator (works but loses durability guarantees the intake path
already has).

## D2 — Workflow state & SLA persistence: new instance table alongside MAF checkpoints

**Decision**: Add `DorWorkflowInstanceEntity` (table `DorWorkflowInstances`) holding the explicit state
(`DorState` enum), outstanding gaps, primary/escalation iteration counters, **SLA clock start timestamp**,
active channel/thread id, dry-run flag snapshot, and outcome. Persist **after every state transition**. Keep it
**separate** from the MAF checkpoint row: the checkpoint is the opaque resumable MAF snapshot; the instance row
is the queryable lifecycle/SLA record the sweeper and UI read.

**Rationale**: Mirrors the existing split (`WorkflowRunEntity` for lifecycle vs `WorkflowCheckpointEntity` for
resume). The SLA sweeper needs to query "instances whose SLA deadline has passed" without deserializing MAF
checkpoints — a durable, indexed timestamp column is required (spec FR-016; the current `ScheduleEscalationTimeout`
is an in-memory `Task.Delay`, lost on restart — the documented gap).

**Alternatives rejected**: Storing SLA state only inside the MAF checkpoint (not queryable by a sweeper);
reusing `WorkflowRunEntity` verbatim (its status enum and columns don't fit the DoR state machine).

## D3 — SLA clocks + multi-tier escalation: durable deadline + background sweeper (BUILD)

**Decision**: Persist an SLA **deadline** (computed at first outreach) on the instance row. A new
`DorSlaSweeperService : BackgroundService` polls on an interval (copying `AppMonitoringBackgroundService`: bounded
sweep, per-item isolation, `Task.Delay` between passes) for instances in `AwaitingResponse`/`Escalated` whose
deadline has elapsed, and drives the escalation or manual-exit transition. Business-hours vs wall-clock is
computed by a pure `BusinessHoursSlaCalculator` (timezone + working days/hours from config).

**Rationale**: No framework primitive gives a durable, restart-surviving multi-tier SLA. The existing timeout is
in-memory and single-shot (auto-reject only). The sweeper pattern already exists and is proven. Escalation must
fire when a human **never** replies — a reply-driven mechanism alone cannot do this (spec FR-017).

**Alternatives rejected**: In-memory `Task.Delay` timers (lost on restart — the current gap); MAF has no timer
executor; an external scheduler (Hangfire/Quartz) — new dependency, violates Framework-First and adds infra.

**Justification (Article VII gap)**: Business-hours SLA + durable multi-tier escalation is not provided by MAF;
built on the existing `BackgroundService` + persistence primitives.

## D4 — Slack reply capture: poll the thread over the existing MCP gateway (BUILD)

**Decision** (clarify Q1): Add `IChatReplyReader` with a `SlackMcpReplyReader` that reads thread replies via the
**existing Slack MCP gateway** (extend `IMcpMessageGateway`/the MCP server with a `conversations.replies`-style
read tool). The `DorSlaSweeperService` (or a sibling reply-poll pass) fetches new replies for waiting instances,
filters bot/ignored authors, and feeds each into the workflow via `RespondAsync`. Outbound stays on the current
`IMessageDelivery` path.

**Rationale**: One Slack integration + token; no second Slack-app Events subscription, request-URL verification,
or new public inbound endpoint. Poll latency (tens of seconds) is immaterial against hour-long SLAs, and the SLA
sweeper already forces the periodic wake the poll rides on (see spec Clarifications).

**Alternatives rejected**: Slack Events API webhook (real-time but needs a second app + public endpoint; the
scale-to-zero "wake on reply" benefit collapses because escalation must fire on *no* reply anyway); direct Slack
Web API bypassing MCP (loses the single-integration/token benefit).

**Justification (Article VII gap)**: Inbound reply capture is send-only today; the MCP gateway is extended with a
read tool rather than a parallel integration.

## D5 — Jira read + status transition: extend the adapter seam (BUILD)

**Decision**: Add two capabilities behind `IWorkTrackerAdapter`:
1. `ReadWorkItemAsync(WorkItemRef)` → a normalized field map for the review payload (fetches issue via the Jira
   REST v3 client already in `JiraWorkTrackerAdapter`).
2. `TransitionAsync(WorkItemRef, transitionId)` → `POST /rest/api/3/issue/{key}/transitions`.
Field writes continue through the existing `SetFieldsAsync` (whitelist enforced by the caller — D7).

**Rationale**: The adapter is write-biased and has **no** status-transition method and no read-into-model helper
(the documented gap). Adding them behind the existing tracker-neutral seam keeps ADO/other trackers additive and
reuses `IJiraConnectionFactory` hot-reload + credential resolution.

**Alternatives rejected**: A Jira-specific client outside the adapter (breaks the spec-018 tracker-neutral seam);
faking transitions via `SetFieldsAsync("status", …)` (Jira status changes require the transitions API, not a field
write).

## D6 — DoR document source: `inline` + `url` behind a seam (BUILD)

**Decision** (clarify Q2): `IDorDocumentSource` with a `source_type` discriminator; ship `InlineDorSource`
(markdown from config) and `UrlDorSource` (authless HTTP GET), cached for `cache_ttl_minutes`. Confluence and
SharePoint are additional implementations behind the same seam, deferred.

**Rationale**: The DoR criteria are hardcoded in `ValidationExecutor` today (the gap). A seam + two lightweight
backends delivers config-over-code and external stakeholder ownership (a published URL) with minimal integration;
the review prompt injects `{{dor_document}}`.

**Alternatives rejected**: Keep criteria in the prompt (fails FR-006); build Confluence/SharePoint now (auth
surface + effort not justified for the MVP).

## D7 — AI-editable field whitelist: enforce in code, not in the prompt

**Decision**: The `TicketUpdateExecutor` filters the AI-proposed field map to the configured
`ai_editable_fields` whitelist **before** calling `SetFieldsAsync`, dropping any non-whitelisted key regardless
of what the model returned. The update prompt is *also* told the whitelist (belt-and-suspenders), but enforcement
is programmatic (spec FR-021, SC-006).

**Rationale**: Security requirement — never trust the model to self-limit. A pure, unit-testable filter function.

**Alternatives rejected**: Prompt-only restriction (fails SC-006 the first time the model hallucinates a field).

## D8 — AI review & structured output: reuse `IStructuredCompletionService`

**Decision**: Reuse `IStructuredCompletionService.GetStructuredAsync<T>` (`ChatResponseFormat.ForJsonSchema`) for
all three AI calls (review, reply-eval, update-payload). Define typed result records with JSON schemas
(`DorReviewResult`, `ReplyEvaluation`, `FieldUpdatePayload`). Model `DorReviewExecutor` on the existing
`ValidationExecutor`, but source criteria from the DoR document (D6) instead of an inline prompt. On a malformed
result, one bounded corrective retry, else manual exit (spec edge case / FR-030).

**Rationale**: Eliminates free-text parsing (Article VII); the primitive already exists and is cost-instrumented
(`CostCapturingChatClient`). Reuse `DorVerdict` shape where it fits.

**Alternatives rejected**: Hand-parsing model JSON (fragile, violates Article VII).

## D9 — Configuration: a `DorWorkflow` connector-config namespace, hot-reloaded per run

**Decision**: Store all six config namespaces as one non-secret JSON blob under a new
`ConnectorType.DorWorkflow` row (`IConnectorConfigRepository`), with secrets (Jira token, Slack token, webhook
secret, AI key) referenced by name and resolved from the vault. An `IDorConfigResolver.ResolveActiveAsync()`
reads and parses it **per run** (mirrors `WorkTrackerConfigResolver`/LLM hot-reload). Static appsettings act only
as a first-run seed. Add a "DoR Workflow" card to `ConnectorSettings.razor` + a `DorWorkflowTester` on the
existing `IConnectorHealthChecker` seam.

**Rationale**: Matches the established connector-config pattern exactly (spec-020), giving runtime change without
restart (FR-025) and encrypted-secret handling (FR-026) for free. Single active config per instance.

**Alternatives rejected**: A new config file / options class (no hot-reload, no encrypted-secret story); a bespoke
settings table (duplicates the connector-config store).

## D10 — Builder default + node UX: replace the example, enrich config panels

**Decision**: Introduce `DefaultWorkflowProvider` that returns the DoR starter graph
(Trigger → DoR Review (AgenticReason) → Route(PASS/FAIL) → [PASS] Ticket Update/Transition; [FAIL] Human
Conversation (HumanApproval) → Escalation → Ticket Update / Manual Exit → Audit), replacing
`WorkflowBuilder.razor:BuildExampleWorkflow()`. Node kinds map to existing `WorkflowNodeType`s
(`HumanApproval`→`RequestPort`); the conversation/SLA semantics are carried in each node's `FunctionConfig` and
realized by the DoR executors. Enrich the config panels for the DoR node kinds (DoR source/prompt, channel/
timeout/iterations, whitelist/transition) with validation + readiness rules (`ApprovalNodesConfiguredRule`
pattern).

**Rationale**: Satisfies FR-027/028/029 by riding the existing builder→MAF realization path rather than a parallel
engine. The dry-run flag (FR-032) is a config value read by every write executor.

**Alternatives rejected**: A brand-new node-type enum (churns the whole builder + realization); a non-builder
hardcoded pipeline (fails "make it the default the operator starts from" and the enhance-the-tab ask).

## Resolved unknowns

| Unknown | Resolution |
|---|---|
| Reply capture mechanism | MCP thread-poll (D4) |
| DoR doc sources this feature | `inline` + `url` behind seam (D6) |
| Dry-run in scope | Yes, global config flag gating all writes (D7/D9) |
| Metrics surface | Emit audit/event data; dashboard deferred (data model D2/audit reuse) |
| SLA durability | Persisted deadline + `BackgroundService` sweeper (D3) |
| Config storage | `ConnectorType.DorWorkflow` JSON + vault secrets (D9) |
| Where the workflow lives | `DBAIAzure.Processes` MAF factory + executors; builder default in Web (D1/D10) |
