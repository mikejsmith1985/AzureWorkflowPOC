# Contract: DoR Workflow State Machine & MAF Graph

The persisted state machine (FR-031) and its realization as a MAF `Workflow` (D1). State persists to
`DorWorkflowInstances` after **every** transition; the MAF checkpoint persists the resumable snapshot.

**Naming (avoids collision):** `DorState` is the **state enum** (Created/Reviewing/…/Done). `DorRunState` is the
**MAF payload record** that flows between executors and rides the `RequestPort` request (it *contains* a
`DorState` plus gaps, iterations, SLA clock, flags). `DorWorkflowInstance` is the **persisted row**.

## States & transitions

```text
Created ──hydrated+DoR loaded──> Reviewing
Reviewing ──all pass──> Passed ──transition ticket──> Updating ──> Done(PASSED)
Reviewing ──gaps──> Failed ──outreach sent, SLA clock start──> AwaitingResponse
AwaitingResponse ──reply──> Reviewing            (reply-eval; partial → focused follow-up, iteration++)
AwaitingResponse ──resolved──> Updating ──write(whitelist)+transition──> Done(RESOLVED_AUTO)
AwaitingResponse ──SLA deadline──> SlaBreach ──escalation outreach, reset counter, new clock──> Escalated
AwaitingResponse ──primary max_iterations──> ManualExit
Escalated ──reply──> Reviewing
Escalated ──resolved──> Updating ──> Done(RESOLVED_AUTO)
Escalated ──escalation SLA/iterations──> ManualExit ──tag+comment, NO transition──> Done(MANUAL_REQUIRED)
```

Drivers: **reply** (MAF `RespondAsync`, from the reply pump) and **SLA deadline** (the sweeper). Independent
timers (FR-015).

## MAF graph (MafDorWorkflowFactory)

```csharp
var hydrate   = new HydrateExecutor(...).BindExecutor();          // read ticket + load DoR
var review    = new DorReviewExecutor(...).BindExecutor();         // AI structured review
var pass      = new PassTransitionExecutor(...).BindExecutor();    // transition + success notify + audit
var outreach  = new GapOutreachExecutor(...).BindExecutor();       // post gaps, start SLA clock
var hitl      = RequestPort.Create<DorRunState, DorRunState>(MafExecutorIds.DorHitl)
                    .BindAsExecutor(allowWrappedRequests: false);  // suspend for human reply
var replyEval = new ReplyEvalExecutor(...).BindExecutor();         // AI eval of reply
var escalate  = new EscalationExecutor(...).BindExecutor();        // second-tier outreach
var update    = new TicketUpdateExecutor(...).BindExecutor();      // whitelist write + transition
var manual    = new ManualExitExecutor(...).BindExecutor();        // tag + comment, no transition
var audit     = new AuditExecutor(...).BindExecutor();             // terminal record

new WorkflowBuilder(hydrate)
  .AddEdge(hydrate, review)
  .AddEdge(review, pass,     (DorRunState s) => s.Overall == Pass)
  .AddEdge(review, outreach, (DorRunState s) => s.Overall == Fail && s.PrimaryIterations == 0)
  .AddEdge(review, update,   (DorRunState s) => s.JustResolved)          // reply-eval resolved
  .AddEdge(review, outreach, (DorRunState s) => s.Fail && !s.JustResolved && s.WithinIterations)  // follow-up
  .AddEdge(outreach, hitl)
  .AddEdge(hitl, replyEval)
  .AddEdge(replyEval, review)                                        // loop back to decide
  .AddEdge(pass, audit)
  .AddEdge(update, audit)
  .AddEdge(manual, audit)
  .WithOutputFrom(audit)
  .Build(validateOrphans: true);
```

Escalation and manual-exit routing are driven by the **orchestrator + sweeper** flipping state and calling
`RespondAsync`/re-driving (mirroring `PipelineOrchestrator.DriveMafSessionAsync`), not by timer edges (MAF has no
timer executor — D3).

## Suspend / resume / restart

- On reaching `hitl`, MAF emits `RequestInfoEvent`; the orchestrator persists `AwaitingResponse` + SLA deadline,
  posts nothing more, and returns. The run is checkpointed.
- A reply (pump → `RespondAsync`) or an SLA deadline (sweeper) resumes it.
- On restart, `DorRunRehydrationService` (mirrors `PausedRunRehydrationService`) loads instances not in `Done`,
  fetches the latest checkpoint, and `ResumeAsync` re-emits the outstanding `RequestInfoEvent` — state recovered
  from the request, not memory (FR-010, SC-003).

## Dry-run

`IsDryRun` snapshot gates `PassTransitionExecutor`, `TicketUpdateExecutor`, `GapOutreachExecutor`,
`EscalationExecutor`, `ManualExitExecutor` — each records a would-do audit entry instead of performing the
external write/send (FR-032).
