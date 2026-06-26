# Contract: IAppRegistryRepository

Persistence seam for registered apps and their build/run results. Follows the existing
`IConnectorConfigRepository` / `IWorkflowRunRepository` conventions (async, `CancellationToken`,
owner-scoped). Backed by `SqliteAppRegistryRepository` over `PipelineDbContext`.

```csharp
public interface IAppRegistryRepository
{
    /// <summary>Register a new app. Throws / returns a typed failure on duplicate name or invalid path.</summary>
    Task<MonitoredApp> RegisterAsync(MonitoredApp app, CancellationToken cancellationToken);

    Task<MonitoredApp?> GetAsync(string appId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MonitoredApp>> ListByOwnerAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>Atomically set lifecycle status (+ stamp timestamps). Guards against illegal transitions.</summary>
    Task SetStatusAsync(string appId, AppStatus status, CancellationToken cancellationToken);

    Task SetBuildResultAsync(string appId, AppBuildResult result, CancellationToken cancellationToken);

    Task SetRunResultAsync(string appId, AppRunResult result, CancellationToken cancellationToken);

    /// <summary>Link / unlink the chosen monitoring workflow (null = unlink).</summary>
    Task SetLinkedWorkflowAsync(string appId, string? workflowId, CancellationToken cancellationToken);

    Task RemoveAsync(string appId, CancellationToken cancellationToken);

    Task<bool> ExistsByNameAsync(string ownerId, string name, CancellationToken cancellationToken);
}
```

**Behavioral guarantees**
- `RegisterAsync` rejects a duplicate `(OwnerId, Name)` and a non-existent/inaccessible `RepoLocalPath`
  and a missing `RunCommand` (FR-002) — no partial row is left behind.
- `SetStatusAsync` enforces the `AppStatus` transition table (data-model); a timeout/start-failure
  caller always moves the app to a terminal-for-the-operation status (FR-008).
- `SetBuildResultAsync` sets `Ready` (succeeded) or `BuildFailed` (failed) and stamps `LastBuiltAt`.
- `SetRunResultAsync` returns the app to `Ready` and stamps `LastRunAt` regardless of run outcome.
- Results persisted are already secret-redacted (FR-009).
