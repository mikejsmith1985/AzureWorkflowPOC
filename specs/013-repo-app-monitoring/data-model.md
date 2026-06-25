# Phase 1 Data Model: Repo-App Build/Run/Monitor

Entities, fields, relationships, validation rules, and state transitions derived from the spec
(FR-001…FR-018) and research decisions. Storage follows the existing EF Core + SQLite + idempotent-DDL
convention; secrets via `ISecretProtector`. Naming follows Article IV.

## Entity: MonitoredApp

A target repository registered for build/run/monitoring. Persisted in table `MonitoredApps`
(one row per app). Build/run results stored as JSON columns on the row.

| Field | Type | Notes |
|-------|------|-------|
| `AppId` | string (GUID) | Primary key |
| `Name` | string | **Unique** (per owner); required; folder/identity of the app |
| `OwnerId` | string | Owner scope (matches workflow ownership convention) |
| `RepoLocalPath` | string | Required; must exist and be accessible at registration |
| `Branch` | string? | Optional git ref; empty → working tree as-is / default |
| `BuildCommand` | string? | Optional; empty → auto-detected (R3) |
| `RunCommand` | string | **Required** (FR-002) |
| `Status` | `AppStatus` | Lifecycle state (see below) |
| `LastBuildResult` | `AppBuildResult?` | JSON; null until first build |
| `LastRunResult` | `AppRunResult?` | JSON; null until first run |
| `LinkedWorkflowId` | string? | FK → `WorkflowDefinitions.Id`; null = not monitored |
| `LastBuiltAt` | DateTimeOffset? | |
| `LastRunAt` | DateTimeOffset? | |
| `CreatedAt` / `UpdatedAt` | DateTimeOffset | |

**Validation rules**:
- `Name` unique within owner; reject duplicate (FR-002).
- `RepoLocalPath` must exist/be accessible at registration (FR-002); re-checked at build time.
- `RunCommand` non-empty (FR-002).
- At most **one** `LinkedWorkflowId` at a time (one monitor per app).

## Enum: AppStatus

Mirrors the reference app status machine.

| Value | Meaning |
|-------|---------|
| `Registered` | Row exists, not yet built |
| `Building` | Build container in progress |
| `Ready` | Built artifact present; runnable on demand |
| `BuildFailed` | Last build attempt failed (summary + logs captured) |
| `Running` | Run container in progress (transient → returns to `Ready`) |

### State transitions

```text
(register) ─────────────▶ Registered
Registered ──build──────▶ Building
Building   ──success────▶ Ready
Building   ──fail/timeout/start-error──▶ BuildFailed
Ready      ──build──────▶ Building            (rebuild)
BuildFailed──build──────▶ Building            (retry)
Ready      ──run────────▶ Running
Running    ──complete───▶ Ready               (outcome recorded regardless of success/failure)
Running    ──timeout/start-error──▶ Ready     (run recorded as failed; never stuck — FR-008)
```

Invariants: an app is never left in `Building`/`Running` after a timeout or start failure (FR-008);
no two concurrent build/run operations for one app (FR-016).

## Value Object: AppBuildResult

Outcome of a build. Stored as JSON in `MonitoredApp.LastBuildResult`.

| Field | Type | Notes |
|-------|------|-------|
| `Succeeded` | bool | |
| `Summary` | string | One-line result |
| `Logs` | string | Full captured build logs, **secret-redacted** (FR-009) |
| `At` | DateTimeOffset | |

## Value Object: AppRunResult

Outcome of a run. Stored as JSON in `MonitoredApp.LastRunResult`.

| Field | Type | Notes |
|-------|------|-------|
| `Outcome` | `RunOutcome` (`Succeeded` / `Failed` / `TimedOut`) | |
| `Summary` | string | One-line result |
| `Logs` | string | Full captured run logs, **secret-redacted** |
| `At` | DateTimeOffset | |

## DTO: AppExecutionRequest

The inputs an `IAppExecutor` receives (the .NET analogue of the reference's `build_app`/`run_app`
env block). Not persisted.

| Field | Type | Notes |
|-------|------|-------|
| `AppId` / `Name` | string | Identity / artifact folder |
| `RepoLocalPath` | string | Bind-mounted into the build container |
| `Branch` | string? | Optional checkout |
| `Command` | string | Resolved build or run command |
| `Mode` | `ExecutionMode` (`Build` / `Run`) | |
| `TimeoutSeconds` | int | Hard cutoff (default 900) |
| `AccessToken` | string? | Transient, clone-only, **never stored** (Article IX) |

## DTO: MonitoringSnapshot

The defined input a monitoring cycle hands the linked workflow (not persisted). Resolves the "what
does the workflow observe" question (FR-018) — the app's latest run plus status, not a live process.

| Field | Type | Notes |
|-------|------|-------|
| `AppId` / `Name` | string | Identity |
| `Status` | `AppStatus` | Current lifecycle state |
| `LastRunOutcome` | `RunOutcome?` | From `LastRunResult` (null if never run) |
| `LastRunSummary` | string? | One-line summary of the latest run |
| `RecentLogTail` | string | Bounded, secret-redacted tail of the latest run's logs |

## Entity: AppMonitoringHeartbeat

Latest monitoring-cycle health per app. Table `AppMonitoringHeartbeats` (one row per app).

| Field | Type | Notes |
|-------|------|-------|
| `AppId` | string | PK / FK → `MonitoredApps.AppId` |
| `LastCycleAt` | DateTimeOffset | |
| `LastCycleOk` | bool | |
| `LastError` | string? | |
| `CycleCount` | long | |

## Entity: AppRaisedIssue

Close-the-loop de-duplication: which detected problems already produced a workflow run/intake, so a
recurring issue is raised once (mirrors the reference `raised_production_defects`). Table
`AppRaisedIssues`.

| Field | Type | Notes |
|-------|------|-------|
| `Signature` | string | PK — stable hash of (app + issue type + description) |
| `AppId` | string | FK → `MonitoredApps.AppId` |
| `WorkflowRunId` | string? | The run/intake created for this issue |
| `CreatedAt` | DateTimeOffset | |

## Relationships

```text
MonitoredApp 1 ──── 0..1 WorkflowDefinition   (LinkedWorkflowId — the chosen monitor; reused unchanged)
MonitoredApp 1 ──── 0..1 AppMonitoringHeartbeat
MonitoredApp 1 ──── 0..*  AppRaisedIssue
MonitoredApp 1 ──── 0..*  WorkflowBuilderRun   (close-the-loop creates runs on the existing path; reused unchanged)
```

The monitoring workflow's execution reuses the existing `WorkflowBuilderRuns` /
`WorkflowExecutionEvents` records and the SignalR hub — **no changes** to those entities.

## Reused (unchanged) entities

- `WorkflowDefinitionRecord` / `IWorkflowRepository` — the saved-workflow gallery (source of the chosen monitor).
- `WorkflowRunEntity` + `WorkflowExecutionEventEntity` + `IWorkflowObserver` — run/event recording for monitoring runs.
- `ConnectorConfigRecord` pattern + `ISecretProtector` — the config/secret convention the app registry follows.
