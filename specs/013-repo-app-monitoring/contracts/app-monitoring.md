# Contract: App Monitoring (link + cycle + close-the-loop)

Runs a user-chosen saved workflow as the monitor for a running app, on the **existing** workflow
execution path (no new engine — Article VII). The .NET analogue of the reference's
`ProductionMonitoringTrigger` + continuous-runner + heartbeat, where "a detected problem is just
another intake."

```csharp
public interface IAppMonitoringService
{
    /// <summary>
    /// Run one monitoring cycle for a linked app: execute its linked workflow (via the existing
    /// WorkflowExecutionOrchestrator) against the app's current run state; if a NEW problem is
    /// detected, create a bounded workflow run/intake attributable to the app (close-the-loop),
    /// de-duplicated by issue signature. Returns the run ids raised this cycle (possibly empty).
    /// </summary>
    Task<IReadOnlyList<string>> RunCycleAsync(MonitoredApp app, CancellationToken cancellationToken);
}

public interface IAppHeartbeatStore
{
    Task RecordCycleAsync(string appId, bool ok, string? error, CancellationToken cancellationToken);
    Task<AppMonitoringHeartbeat?> GetAsync(string appId, CancellationToken cancellationToken);

    /// <summary>True once a given issue signature has already produced a run (cross-cycle dedup).</summary>
    Task<bool> IsRaisedAsync(string signature, CancellationToken cancellationToken);
    Task RecordRaisedAsync(AppRaisedIssue issue, CancellationToken cancellationToken);
}
```

**AppMonitoringService.RunCycleAsync behavior**
- Builds a `MonitoringSnapshot` (status + latest run outcome/summary + secret-redacted log tail) and
  passes it as the workflow input — this is the defined signal the workflow inspects (FR-018).
- Resolves the app's `LinkedWorkflowId` via `IWorkflowRepository`; if missing/deleted, returns empty
  and the app is reported **unlinked** — monitoring does not crash (FR-017).
- Executes the linked workflow through `WorkflowExecutionOrchestrator.StartRunAsync(...)` with an input
  describing the app's running state — **the same path any other run uses** (FR-011).
- On a detected problem, computes a stable signature `hash(appId + issueType + description)`; if already
  raised (`IsRaisedAsync`), skips; otherwise creates a new bounded run/intake attributable to the app
  and records the signature (FR-012). A recurring/ongoing problem is therefore raised once, not per cycle.
- One bad signal never stops processing the rest of the cycle.

**AppMonitoringBackgroundService** (hosted loop)
- Every N seconds (configurable; default ~60), for each enabled app→workflow link, calls
  `RunCycleAsync` and `RecordCycleAsync` (heartbeat: last cycle time, ok/fail, last error — FR-013).
- One failing app's cycle never blocks the others. A no-op cycle (nothing detected) records a healthy
  heartbeat.

**Linking**
- The chosen workflow is selected from the existing gallery and stored as `MonitoredApp.LinkedWorkflowId`
  (FR-010); changing/removing the link takes effect on the next cycle. One monitor per app.
