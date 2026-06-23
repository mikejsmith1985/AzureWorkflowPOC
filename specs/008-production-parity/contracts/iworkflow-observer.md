# Contract: IWorkflowObserver

`DBAIAzure.Core/Interfaces/IWorkflowObserver.cs`

Single write path for all workflow execution events. Multiple implementations registered
simultaneously via `IEnumerable<IWorkflowObserver>`. Fan-out is fire-and-forget; failures in
one observer do not propagate to others or to the step.

---

```csharp
public interface IWorkflowObserver
{
    /// <summary>
    /// Records a workflow execution event. Called by WorkflowExecutionOrchestrator on
    /// step entry/exit, by SK IFunctionInvocationFilter for LLM call metadata, and by
    /// the HITL pause/resume path. Implementations must not throw — log and swallow errors.
    /// </summary>
    Task RecordEventAsync(WorkflowExecutionEvent evt, CancellationToken ct = default);
}
```

---

**Implementations**:

| Class | Location | Behaviour |
|-------|----------|-----------|
| `SqlWorkflowObserver` | `DBAIAzure.Web/Services/` | Writes to `WorkflowExecutionEvents` via EF Core |
| `SignalRWorkflowObserver` | `DBAIAzure.Web/Services/` | Pushes event to `WorkflowRunHub` for live UI |
| `AzureMonitorWorkflowObserver` | `DBAIAzure.Web/Services/` | Sends telemetry to `TelemetryClient` (conditionally registered) |

**Registration**:
```csharp
services.AddScoped<IWorkflowObserver, SqlWorkflowObserver>();
services.AddScoped<IWorkflowObserver, SignalRWorkflowObserver>();
// Conditional:
if (!string.IsNullOrEmpty(config["AzureMonitor:ConnectionString"]))
    services.AddScoped<IWorkflowObserver, AzureMonitorWorkflowObserver>();
```

**SK filter hooks** (registered on kernel factory):
- `IFunctionInvocationFilter` — captures pre/post invocation; emits `LlmCallCompleted` event with model, tokens, latency.
- `IPromptRenderFilter` — captures rendered prompt (for future prompt-logging; not stored in V1).
Both filters resolve `IEnumerable<IWorkflowObserver>` from the DI scope to fan-out events.
