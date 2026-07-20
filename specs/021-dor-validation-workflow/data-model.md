# Data Model: Intelligent DoR Validation Workflow

Phase 1 output. Entities, the persisted state machine, and the configuration schema. Namespaces:
`DBAIAzure.Core.Models.DorWorkflow` (runtime state) and `...DorWorkflow.Config` (configuration).

## Naming

- **`DorState`** — the state **enum** (below). **`DorRunState`** — the in-flight MAF **payload record** carried
  between executors and on the `RequestPort` request (contains a `DorState` + gaps/iterations/SLA-clock/flags).
  **`DorWorkflowInstance`** — the **persisted row** (queryable lifecycle + SLA record). Three distinct types; do
  not conflate.

## State Machine

`DorState` enum — persisted on the instance row after **every** transition (spec FR-031).

| State | Entry condition | Exits |
|---|---|---|
| `Created` | Trigger received, HMAC-valid, not a duplicate | → `Reviewing` |
| `Reviewing` | Ticket hydrated + DoR document loaded | → `Passed` \| `Failed` |
| `Passed` | All DoR criteria satisfied | → `Updating` (transition-only) → `Done` |
| `Failed` | ≥1 criterion unmet | → `AwaitingResponse` |
| `AwaitingResponse` | Primary outreach sent; SLA clock started | → `Reviewing` (reply) \| `SlaBreach` (deadline) \| `ManualExit` (iterations) |
| `SlaBreach` | Primary SLA elapsed, unresolved | → `Escalated` |
| `Escalated` | Escalation outreach sent; new clock + counter | → `Reviewing` (reply) \| `ManualExit` (escalation SLA/iterations) |
| `Updating` | Resolution reached; field-update payload built | → `Done` |
| `ManualExit` | Iteration or SLA limit hit at any tier | → `Done` |
| `Done` | Terminal — transitioned, or handed off | — |

Transitions in `AwaitingResponse`/`Escalated` are driven by two sources: a **reply** (fed via MAF
`RespondAsync`, re-enters `Reviewing` for reply-eval) or the **SLA sweeper** (deadline elapsed → `SlaBreach`/
`ManualExit`). Reply-timeout and SLA are independent (FR-015): a reply after the reply-timeout but before the SLA
is still processed.

## Entities

### DorWorkflowInstance  *(persisted — table `DorWorkflowInstances`)*

The queryable lifecycle + SLA record for one ticket's run. Separate from the MAF checkpoint (opaque resume
snapshot). One instance per ticket (unique on `TicketId` while active — enforces idempotency, FR-004).

| Field | Type | Notes |
|---|---|---|
| `RunId` | string (PK) | Also the MAF session id / checkpoint `SessionId`. |
| `TicketKey` | string | Jira issue key (e.g. `SBRO-123`). Indexed. |
| `State` | `DorState` | Current state; persisted every transition. |
| `OutstandingGaps` | string (JSON) | List of unmet criterion names carried across turns. |
| `PrimaryIterations` | int | Exchange count in the primary loop (incl. timed-out, FR-014). |
| `EscalationIterations` | int | Reset to 0 on entering `Escalated` (FR-017). |
| `SlaClockStartedAt` | DateTimeOffset? | Set at first outreach (FR-016). Null before `AwaitingResponse`. |
| `SlaDeadlineAt` | DateTimeOffset? | Computed from clock start + SLA (business-hours aware). Indexed for the sweeper. |
| `SlaTier` | `SlaTier` (`Primary`/`Escalation`) | Which SLA is currently running. |
| `ActiveChannelId` | string | Primary or escalation channel currently in use. |
| `ThreadRef` | string | The chat thread id the workflow owns (reply boundary, FR-011). |
| `LastSeenReplyRef` | string? | Cursor for MCP reply polling (dedup). |
| `IsDryRun` | bool | Snapshot of the dry-run flag at start (FR-032). |
| `Outcome` | `DorOutcome?` (`Passed`/`ResolvedAuto`/`ManualRequired`) | Set at terminal. |
| `StartedAt`/`UpdatedAt`/`CompletedAt` | DateTimeOffset | Lifecycle timestamps. |
| `FailureReason` | string? | For manual-exit / error context. |

**Validation**: `SlaDeadlineAt` required whenever `State ∈ {AwaitingResponse, Escalated}`. `Outcome` required
when `State == Done`. **Idempotency (FR-004)** is enforced by a **unique index on `TicketKey` filtered to
non-terminal (active) states**: two near-simultaneous webhook deliveries both attempt an insert, the second hits
the unique-constraint violation, which the creator catches and treats as "already active → discard the
duplicate". Do not rely on a read-then-insert check alone (it races).

### DorReviewResult  *(transient + audited — AI structured output)*

The AI verdict for a ticket at a point in time.

| Field | Type | Notes |
|---|---|---|
| `Overall` | enum `Pass`/`Fail` | |
| `Criteria` | `CriterionResult[]` | each: `Name`, `Status` (Pass/Fail), `Reason`. |
| `MissingFields` | string[] | Absent/insufficient fields. |
| `SuggestedUpdates` | map<fieldKey,string> | AI-proposed values (filtered by whitelist before use, D7). |

### ReplyEvaluation  *(transient — AI structured output for a human reply)*

| Field | Type | Notes |
|---|---|---|
| `Resolved` | bool | All outstanding gaps closed by this reply. |
| `RemainingGaps` | string[] | Still-unresolved criterion names. |
| `FieldUpdates` | map<fieldKey,string> | Populated when `Resolved` (whitelist-filtered before write). |
| `ReplyMessage` | string | Posted back to the channel verbatim (focused follow-up or resolution note). |

### DorDocument  *(loaded via seam, cached)*

| Field | Type | Notes |
|---|---|---|
| `Text` | string | Full DoR markdown injected as `{{dor_document}}`. |
| `Version` | string? | Source last-modified/etag, recorded in audit (traceability). |
| `LoadedAt` | DateTimeOffset | Drives `cache_ttl_minutes`. |
| `SourceType` | `inline`/`url` (+ deferred `confluence`/`sharepoint`) | Discriminator. |

### ConversationTurn  *(audited)*

One exchange: outbound message + captured reply + AI evaluation. Emitted as a `WorkflowExecutionEvent` (reused
audit timeline), not a new table.

### ResolutionRecord / Audit  *(append-only — reuse existing stores)*

Per-instance outcome + per-step events. Reuse `WorkflowExecutionEvent` (`IWorkflowObserver`) for step audit and
`ICostLedger` for AI cost. Fields captured for metrics derivation (FR-024): outcome source (auto/escalation/
manual), duration, primary+escalation iterations, fields changed, per-criterion fail flags, AI latency/cost.
Append-only (no update/delete).

## Configuration Schema  *(the six namespaces — stored under `ConnectorType.DorWorkflow`)*

Non-secret JSON blob + encrypted secrets (by-reference). Resolved **per run** by `IDorConfigResolver` (FR-025).
See [contracts/dor-config-schema.md](./contracts/dor-config-schema.md) for the full field list. Summary:

- **jira**: `base_url`, `project_keys[]`, `issue_types[]`, `watch_fields[]`, `field_labels{}`,
  `ai_editable_fields[]`, `ready_transition_id`, `ready_status`, `manual_label`, `webhook_secret_ref`,
  `api_token_secret_ref`, `account_email`.
- **dor**: `source_type` (`inline`/`url`), `source_uri` **or** `inline_markdown`, `cache_ttl_minutes`, `format`.
- **ai**: `provider`, `model`, `review_prompt_template`, `conversation_prompt_template`, `update_prompt_template`,
  `temperature`, `max_tokens`, `api_key_secret_ref`.
- **comms**: `primary{ type, channel_id, mention_users[], reply_timeout_minutes, max_iterations }`,
  `escalation{ type, channel_id, mention_users[], reply_timeout_minutes, max_iterations }`,
  `success{ enabled, channel_id }`, `token_secret_ref`, `ignore_user_ids[]`.
- **sla**: `primary_sla_hours`, `escalation_sla_hours`, `clock_type` (`business_hours`/`wall_clock`),
  `business_hours{ timezone, start, end, working_days[] }`.
- **audit**: `store_type`, `log_ai_responses`, `jira_comment_on_pass|fail|escalation`.
- **run**: `dry_run` (global read-only gate, FR-032).

**Secret handling**: every `*_secret_ref` names a vault entry; the encrypted secret blob holds the resolved
values server-side only; nothing in plaintext in config/logs/db (FR-026, Article IX).

## Relationships

```text
DorWorkflowConfig (1 active) ──drives──> DorWorkflowInstance (N, one per ticket)
DorWorkflowInstance ──1:1──> MAF WorkflowCheckpoint (resume snapshot, existing table)
DorWorkflowInstance ──1:N──> WorkflowExecutionEvent (audit timeline, existing)
DorWorkflowInstance ──1:N──> CostLedgerEntry (AI cost, existing, keyed by RunId)
DorReviewResult / ReplyEvaluation ──audited-into──> WorkflowExecutionEvent
DorDocument ──cached, injected-into──> DorReviewExecutor / ReplyEvalExecutor
```
