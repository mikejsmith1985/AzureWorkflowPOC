# Contract: IAppExecutor (Sim + Docker)

The seam between the app registry and *where build/run work happens* — the .NET analogue of the
reference's `AppExecutor` ABC (`SimAppExecutor` + the real ACA executor). Two implementations:
`SimAppExecutor` (synthesizes outcomes; default/dev) and `DockerAppExecutor` (real throwaway
containers via `Docker.DotNet`). Both report outcomes back through `IAppRegistryRepository` — callers
never branch on which executor is active (FR-015).

```csharp
public interface IAppExecutor
{
    /// <summary>
    /// Begin building the app into its artifact for the given request. Sets status Building, then
    /// records an AppBuildResult (Ready or BuildFailed) when finished. Never leaves the app stuck.
    /// </summary>
    Task BuildAsync(MonitoredApp app, AppExecutionRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Begin running the built artifact. Sets status Running, then records an AppRunResult and
    /// returns the app to Ready. Enforces the request timeout.
    /// </summary>
    Task RunAsync(MonitoredApp app, AppExecutionRequest request, CancellationToken cancellationToken);
}
```

**SimAppExecutor**
- Transitions Building → Ready (synth success) and Running → Ready (synth outcome) after a short delay
  with synthesized summary/logs. Never performs real I/O; never hangs. Used when Docker is unavailable
  or demo mode is selected.

**DockerAppExecutor** (Docker.DotNet)
- **Build**: start one ephemeral container; bind-mount `RepoLocalPath` **read-only**; optional branch
  checkout inside the container; run the resolved (or auto-detected, R3) build command; write the
  artifact to a per-app **named volume**; capture stdout/stderr; on completion record success/failure +
  redacted logs; **remove the container** (throwaway, FR-007).
- **Run**: start a second ephemeral container against the artifact volume; run `RunCommand` under the
  hard `TimeoutSeconds`; capture logs; on completion/timeout record the outcome; **remove the container**.
- **Safety**: container removal/stop targets the **specific container id** created (labelled with
  `AppId`) — never a wildcard kill (Article II). A failure to even start the container is recorded
  immediately as a failed build/run (FR-008). A run exceeding the timeout is stopped and recorded as
  `TimedOut`.
- **Secrets**: an `AccessToken` (if ever supplied) is passed only to the container and never persisted;
  logs are redacted before persistence (FR-009).

**Supporting components**
- `BuildCommandAutoDetector` — resolves a build command from repo contents when none supplied (R3).
- `ContainerLogRedactor` — removes known secret values from captured logs (R6).
