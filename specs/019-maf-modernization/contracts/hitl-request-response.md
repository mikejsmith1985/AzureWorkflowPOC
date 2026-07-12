# Contract: Human-in-the-Loop (RequestPort request/response)

**Owner**: `DBAIAzure.Processes` + `DBAIAzure.Web` (Review Queue/host). **Basis**: research.md D2.

## Rules

- Each HITL gate is a **`RequestPort.Create<TRequest, TResponse>(name)`** node in the workflow
  (replaces `ApprovalPauseStep`/`HitlPauseStep`/`HumanApprovalStep` proxy + `IExternalKernelProcessMessageChannel`).
- When execution reaches the port, the run **pauses** and emits a **`RequestInfoEvent`** carrying
  `.Request`. The host resolves it: `await run.SendResponseAsync(evt.Request.CreateResponse(decision))`.
- **Durability**: pending requests are captured in checkpoints (see checkpoint-store contract) and
  **re-emitted as `RequestInfoEvent` on restore**, so a request outstanding at shutdown is recoverable
  (FR-006).
- **Host layer preserved**: the Review Queue store, SignalR run-update hub, and `TaskCompletionSource`
  gating remain; they now bridge to `RequestInfoEvent` / `SendResponseAsync` instead of SK channels.
- **Timeout/escalation**: existing auto-reject/escalation on timeout preserved (FR-007) by the host
  resolving the request with the timeout decision.

## Three surfaces
| Surface | Request payload | Response |
|---|---|---|
| Intake (console runner) | clarifying prompt | user text/decision |
| Phase-handler (web) | approval card | approve / reject |
| Visual builder (web) | human-approval node | approve / reject (Review Queue) |

## Acceptance
- Run suspends, pending item appears, decision recorded, run resumes from the correct point (FR-005).
- At least one resume **across an application restart** (SC-003).
