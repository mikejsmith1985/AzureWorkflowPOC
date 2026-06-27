# Phase 0 Research: Repo-App Build/Run/Monitor

All Technical Context unknowns are resolved below. Each decision records what was chosen, why, and
the alternatives rejected. The reference application (`C:\ProjectsWin\DBAI` workflow-poc, LangGraph)
is the behavioural target; decisions favour parity with it and reuse of existing AzureWorkflowPOC
machinery (framework-first).

## R1 — Container engine for the real "throwaway container" executor

**Decision**: Implement `DockerAppExecutor` on the official **`Docker.DotNet`** Engine API client.
A **build** runs one ephemeral container (the local repo bind-mounted read-only, build command
executed, artifact written to a per-app named volume); a **run** runs a second ephemeral container
(run command against that artifact volume). Both containers are labelled with the app id, given a
hard timeout, and **removed after completion** (AutoRemove / explicit remove). Outcomes (exit code,
captured stdout/stderr) are written straight back into the registry store — the same in-process
result path `SimAppExecutor` uses.

**Rationale**: Mirrors the reference's two-operation model (`build_app` → `/apps/<name>` volume,
then `run_app` against it) using a fresh disposable container per operation. `Docker.DotNet` is the
maintained .NET Engine API client; it is mockable and lets us stream logs and enforce timeouts
without parsing CLI text. Works against local Docker Desktop on the dev machine.

**Alternatives rejected**:
- *`docker` CLI via `Process.Start`* — brittle output parsing, harder to unit-test, weaker error
  surfacing.
- *Testcontainers for .NET* — designed for test-fixture lifecycles, not building/running arbitrary
  user repos on demand from app code.
- *Azure Container Apps Jobs (the reference's real executor)* — cloud-bound and is the concern of the
  separate `012-azure-container-deploy` feature; explicitly out of scope here (local-path source).
  The `IAppExecutor` seam leaves room to add an ACA executor later without touching callers.

## R2 — Obtaining the repo (local path)

**Decision**: The repo is a **local filesystem path** (per clarification). The build container
**bind-mounts the local path read-only**; no network clone is performed. If a branch is specified,
it is checked out inside the build container (git available in the build image) against a working
copy, never mutating the user's tree. The model retains an optional `AccessToken` field for future
remote-URL support, but it is **unused for local paths and never persisted** (Article IX).

**Rationale**: Matches the clarified scope, avoids credential handling, and keeps the user's working
tree untouched. Parity with the reference's "obtain repo → build into volume" without its private-repo
clone-token path (not needed locally).

**Alternatives rejected**: Cloning the local path over file:// (unnecessary copy); operating in-place
on the user's tree (risk of mutation).

## R3 — Auto-detecting the build command

**Decision**: When no build command is supplied, `BuildCommandAutoDetector` picks a sensible default
by inspecting the repo root: `Dockerfile` → `docker build`; `package.json` → `npm ci && npm run build`
(or `npm ci` if no build script); `requirements.txt`/`pyproject.toml` → `pip install`; `*.sln`/`*.csproj`
→ `dotnet build`. If none match, the build fails fast with an explanatory summary.

**Rationale**: Mirrors the reference's pip/npm auto-detect; keeps registration low-friction. Explicit
build command always overrides detection.

**Alternatives rejected**: Requiring a build command always (more friction, diverges from reference);
deep project analysis (over-engineering for a POC).

## R4 — How the monitoring workflow "runs against" the app

**Decision**: A monitoring cycle for a linked app calls the existing
`WorkflowExecutionOrchestrator.StartRunAsync(workflow, inputDescription)` with an input derived from
the app's current run state/health — i.e. the chosen workflow executes on the **exact same path** as
any other workflow run (FR-011). When that workflow concludes a problem exists, the detection is
turned into a **new bounded workflow run / intake attributable to the app** (close-the-loop), and the
issue signature is recorded so a recurring problem is not re-raised every cycle.

**Rationale**: Framework-first (Article VII) — no new workflow engine, bus, or state machine. This is
the .NET analogue of the reference's `ProductionMonitoringTrigger` ("a detected defect is just another
intake") implemented with the orchestrator we already have.

**Alternatives rejected**: A bespoke monitoring engine (violates Article VII and the reference's own
"no dedicated defect workflow" design); auto-spawning in-process runs without dedup (causes a new run
every cycle for an ongoing defect).

## R5 — Monitoring loop & heartbeat

**Decision**: A single hosted **`BackgroundService`** (`AppMonitoringBackgroundService`) cycles every
N seconds (configurable), and for each enabled app→workflow link invokes `AppMonitoringService`,
recording a **heartbeat** (last cycle time, ok/fail, last error) per app via `IAppHeartbeatStore`.
One failing app's cycle never stops the others.

**Rationale**: Mirrors the reference's continuous-runner + per-trigger heartbeat. `BackgroundService`
is the stock ASP.NET hosting primitive — no custom scheduler.

**Alternatives rejected**: Per-app timers/threads (harder to reason about, no central health view);
external scheduler (overkill, single-node scope).

## R6 — Log capture & secret redaction

**Decision**: Capture container stdout/stderr (bounded length) for build and run; before persisting or
displaying, `ContainerLogRedactor` removes any known secret values (e.g. an access token passed for a
build) and obvious credential patterns. Stored logs and summaries never contain plaintext secrets
(Article IX, FR-009).

**Rationale**: Matches the reference's token-redacted log handling; satisfies the secrets article.

**Alternatives rejected**: Storing raw logs (secret-leak risk); discarding logs (loses the diagnostic
value the reference exposes in its detail view).

## R7 — Persistence

**Decision**: New SQLite tables `MonitoredApps`, `AppMonitoringHeartbeats`, `AppRaisedIssues` created
via idempotent `CREATE TABLE IF NOT EXISTS` in `Program.cs` startup, with EF Core entities + repositories
following the existing `ConnectorConfigRecord`/`WorkflowRunEntity` convention. Build/run results are
stored as JSON columns on the app row (like the workflow topology JSON columns). Any per-app secret
goes through the existing `ISecretProtector`.

**Rationale**: Framework-first reuse of the established storage pattern; no migration tooling needed
(matches the project's idempotent-DDL approach).

**Alternatives rejected**: A new database/store (parallel registry — the reference deliberately keeps
all state in one SQLite DB); EF migrations (project convention is idempotent DDL).

## R8 — Sim vs real executor selection

**Decision**: `IAppExecutor` is resolved at runtime: `DockerAppExecutor` when a Docker engine is
reachable and real mode is enabled; otherwise `SimAppExecutor`. Screens, controls, status names, and
lifecycle are identical across both (FR-015); only whether real work runs differs.

**Rationale**: Mirrors the reference Sim/Aca split and guarantees the feature is demonstrable
everywhere (US4).

**Alternatives rejected**: Real-only (undemonstrable without Docker); sim-only (no real capability).

## R9 — Live status surface

**Decision**: Reuse the existing SignalR pattern (`WorkflowRunHub` / observer broadcast) to push app
status and build/run/monitor progress to the Apps pages, so status badges update without reload —
the same mechanism Run History/Review Queue already use.

**Rationale**: Framework-first reuse of the real-time surface; consistent UX.

**Alternatives rejected**: Polling (worse UX, redundant with existing hub).

## R10 — Concurrency & lifecycle safety

**Decision**: The registry enforces that an app has at most one in-flight build or run: a Build/Run
trigger while the app is already Building/Running is rejected or queued, never producing two
simultaneous containers for the same operation (FR-016). A start failure or timeout always transitions
the app out of Building/Running to a recorded failure (FR-008). Containers are removed by the specific
id created — never a wildcard kill (Article II).

**Rationale**: Matches the reference's "never leave it stuck building/running" guarantees and the
constitution's process-protection rule.

**Alternatives rejected**: Unguarded triggers (duplicate containers, races); leaving timed-out
operations in a non-terminal state (the bug the reference explicitly guards against).
