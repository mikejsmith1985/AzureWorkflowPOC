# Feature Specification: Point at a Repo, Run Its App in a Throwaway Container, Monitor It With a Chosen Workflow

**Feature Branch**: `feature/013-repo-app-monitoring`

**Created**: 2026-06-25

**Status**: Draft

**Input**: User description: "'C:\ProjectsWin\DBAI' I want this project's feature to point to a repo and run its application against the chosen workflow pipeline implemented exactly in the same manner."

---

> **Reference application.** `C:\ProjectsWin\DBAI` (its `workflow-poc`, the LangGraph version) is the
> canonical behavioural target. Where this spec says "mirror the reference" or "exactly the same
> manner," it means the observable behaviour and surfaces a user experiences must match: the way a
> repo is registered as an app, the way that app is built and run inside its own disposable
> container, the way a workflow is linked to monitor it, and the console screens, status lifecycle,
> and close-the-loop behaviour around all of that. There is no CrewAI involvement; CrewAI was
> retired in the reference and is out of scope here.

---

## Clarifications

### Session 2026-06-25

- Q: What role does the target repo play when a workflow runs against it? → A: **The repo is the
  source for a demo application.** This project clones/obtains the repo, **builds and runs that
  repo's application in its own throwaway container**, and the chosen workflow **monitors** that
  running container.
- Q: "Implemented exactly in the same manner" — same as what? → A: **Same as the reference LangGraph
  app** (`C:\ProjectsWin\DBAI`), not CrewAI (long since removed). The **UI, container creation,
  monitoring, and the linking between a workflow and a monitored app must be handled identically** —
  architectural and behavioural parity with the reference.
- Q: Which "chosen workflow pipeline" runs against the repo, and how is the repo referenced? → A:
  **Any saved workflow** the user picks from the existing Workflow gallery, and the repo is
  referenced by a **local filesystem path**.
- Q: While a linked app is monitored, what does the workflow observe — a continuously running
  process, or the app's latest run? → A: **The app's latest run plus its status.** The app need
  not stay running; each monitoring cycle hands the linked workflow a defined *snapshot* — the
  app's status, its most recent run outcome + summary, and a secret-redacted tail of that run's
  logs. "Monitor the running app" means watch its latest run/health, not require a long-lived
  process. (A long-lived "serve" run mode and an HTTP health probe are possible future extensions,
  out of scope here.)

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Register a local repo as a monitored app (Priority: P1)

A user points the product at a repository on their machine by giving its local path, a name, an
optional branch, an optional build command, and a run command. The repo is recorded as a
registered "app" and appears in an Apps list showing its current lifecycle status — exactly the way
the reference application's admin console registers and lists apps.

**Why this priority**: Nothing downstream (building, running, monitoring) can happen until a repo is
registered as an app. This is the entry point and is independently demonstrable on its own.

**Independent Test**: Open the Apps surface, register a repo by local path with a name and run
command, and confirm it appears in the Apps list with status "Registered" and the details that were
entered — and that re-opening the page shows it persisted.

**Acceptance Scenarios**:

1. **Given** the Apps surface, **When** the user registers an app with a local repo path, name, and
   run command (branch and build command optional), **Then** the app is saved and listed with status
   **Registered**.
2. **Given** a registered app, **When** the user reloads the page or returns later, **Then** the app
   and its entered configuration are still present (persisted, not in-memory only).
3. **Given** the registration form, **When** the user supplies a name that is already in use or a
   local path that does not exist, **Then** the registration is rejected with a clear, specific
   message rather than creating an unusable app.
4. **Given** the Apps list, **When** the user views an app, **Then** its status is shown with a
   clear status indicator and its key fields (name, repo path, branch, last build/run times) are
   visible — mirroring the reference console's app list.
5. **Given** a registered app, **When** the user chooses to remove it, **Then** it is unregistered
   and disappears from the list.

---

### User Story 2 — Build and run the app in its own throwaway container (Priority: P1)

From the Apps list, the user triggers a **Build** for a registered app: its application is built
inside a fresh, isolated, disposable container, and the outcome (success or failure), a one-line
summary, and the full logs are captured and shown. Once built (status **Ready**), the user triggers
a **Run**: the built application executes in an isolated container with a time limit, and that
outcome, summary, and logs are likewise captured — after which the app returns to **Ready**. The
container is thrown away after each operation; nothing persists between containers except the
recorded outcome and the built artifact.

**Why this priority**: Building and running the target repo's app in a disposable container is the
mechanical heart of the request — "run its application." It is independently testable once an app is
registered (US1).

**Independent Test**: For a registered app, trigger Build and watch status move
Registered → Building → Ready with captured build logs; then trigger Run and watch status move
Ready → Running → Ready with captured run logs and an outcome.

**Acceptance Scenarios**:

1. **Given** a registered app, **When** the user triggers Build, **Then** the app moves to status
   **Building**, its repo is obtained and built in a fresh isolated container, and on completion the
   status becomes **Ready** (success) or **Build Failed** (failure) with a summary and full logs
   recorded.
2. **Given** an app with status **Ready**, **When** the user triggers Run, **Then** the app moves to
   **Running**, the built application is executed in an isolated, disposable container, and on
   completion the run **outcome** (succeeded/failed), summary, and logs are recorded and the app
   returns to **Ready**.
3. **Given** a Run that exceeds the configured time limit, **When** the limit is reached, **Then**
   the container is stopped and the run is recorded as failed with a timeout reason — it never hangs
   indefinitely.
4. **Given** a build or run that fails to even start (e.g. the container engine is unavailable),
   **When** the failure occurs, **Then** the app is immediately recorded as failed with an
   explanatory summary rather than being left stuck in **Building**/**Running**.
5. **Given** a completed build or run, **When** the user opens the app's detail view, **Then** the
   summary and the full captured logs are viewable, with any secrets redacted from the log text.
6. **Given** any build or run, **When** it finishes, **Then** the container used for it is disposed
   of (throwaway) — a subsequent build or run starts from a fresh container.

---

### User Story 3 — Link a chosen saved workflow to monitor the running app (Priority: P1)

The user links one of their **saved workflows** (chosen from the existing Workflow gallery) to a
registered app as its **monitoring pipeline**. While the app runs, the linked workflow watches it;
when the workflow detects a problem, the detection feeds back into the workflow as its own bounded
run (closing the loop) — exactly the linking-and-monitoring relationship the reference application
implements between a workflow and a monitored app.

**Why this priority**: "Against the chosen workflow pipeline" is the defining purpose — the repo's
app exists to be watched by a user-chosen workflow. It depends on US1/US2 but is the feature's
reason for being.

**Independent Test**: Link a saved workflow to a registered app, run the app, and confirm the linked
workflow executes as the monitor and that a detected issue produces a new workflow run (or queued
intake) attributable to that app — and that the linkage and its health are visible.

**Acceptance Scenarios**:

1. **Given** a registered app and at least one saved workflow, **When** the user links a workflow to
   the app, **Then** the link is recorded and shown on the app, and the app indicates it is being
   monitored by that workflow.
2. **Given** a linked app that is running, **When** the monitoring cycle runs, **Then** the linked
   workflow executes against the running app as its monitor, on the same execution path any other
   workflow run uses (no special-casing).
3. **Given** the monitoring workflow detects a problem with the running app, **When** the detection
   occurs, **Then** it feeds back as a new bounded workflow run / intake attributable to that app
   (close-the-loop), and a recurring/ongoing problem is not re-raised on every cycle (de-duplicated).
4. **Given** a linked workflow, **When** the user changes or removes the link, **Then** monitoring
   uses the new workflow (or stops) on the next cycle, and a removed/deleted workflow does not crash
   monitoring — it is reported as unlinked.
5. **Given** monitored apps, **When** the user views monitoring status, **Then** the most recent
   monitoring cycle's health (last run time, success/failure, last error) is shown per app —
   mirroring the reference's trigger/heartbeat status display.

---

### User Story 4 — Demonstrate the whole flow without real infrastructure (Priority: P2)

On a developer machine with no container engine and no real target repo, the user can still drive
the full register → build → run → monitor flow against a **simulated** executor that synthesizes
build/run/monitor outcomes — so the UI and the lifecycle are fully demonstrable, exactly as the
reference application offers a simulated executor alongside its real one.

**Why this priority**: It guarantees the feature is demonstrable and testable everywhere (matching
the reference's sim/live split), but it is supporting capability rather than the core loop.

**Independent Test**: With the simulated executor active, register an app, build it, run it, and
link a workflow — and confirm every status transition, log surface, and monitoring indicator behaves
as it would with a real container, without any container actually being created.

**Acceptance Scenarios**:

1. **Given** no real container engine is available or the user selects demo mode, **When** the user
   builds and runs an app, **Then** the simulated executor produces realistic Building → Ready and
   Running → Ready transitions with synthesized summaries and logs, never hanging.
2. **Given** the simulated executor, **When** the user links and runs a monitoring workflow, **Then**
   the monitoring cycle and its close-the-loop behaviour are demonstrable with synthesized detections.
3. **Given** either executor (simulated or real), **When** the user performs the same actions, **Then**
   the screens, controls, status names, and lifecycle are identical — the only difference is whether
   real work runs.

---

### Edge Cases

- **Non-existent / inaccessible local path** → registration (or the next build) fails with a clear
  message; no half-created app is left behind.
- **Missing run command** → registration is rejected (a run command is required to run the app).
- **No build command supplied** → the system attempts a sensible auto-detected build for the repo's
  ecosystem, mirroring the reference; if it cannot, the build fails with an explanatory summary.
- **Build/run exceeds the time limit** → the container is stopped and the operation is recorded as
  failed (timeout), never left hanging.
- **Container engine unavailable** → the operation fails fast with an explanatory message (or falls
  back to the simulated executor in demo mode), and the app never sticks in Building/Running.
- **Duplicate app name** → rejected; names are unique.
- **Linked workflow deleted or unlinked mid-flight** → monitoring reports the app as unlinked rather
  than crashing.
- **Monitoring cycle finds nothing** → it completes as a healthy no-op and records a successful
  heartbeat.
- **Recurring/ongoing detected problem** → raised once, not re-raised every cycle (de-duplicated by a
  stable signature, as the reference does).
- **Logs containing secrets/tokens** → secrets are redacted from captured/displayed logs and are
  never persisted in plaintext.
- **Concurrent build/run of the same app** → a second trigger while one is in flight is prevented or
  queued, never producing two simultaneous containers for the same app operation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A user MUST be able to register a target repository as a monitored "app" by supplying a
  **local filesystem path**, a unique **name**, an optional **branch**, an optional **build command**,
  and a required **run command**. The registration MUST be persisted (survives reload/restart).
- **FR-002**: The system MUST validate a registration: reject a duplicate name, reject a local path
  that does not exist or is inaccessible, and reject a missing run command — each with a clear,
  specific message and no partially-created app.
- **FR-003**: Each app MUST have a lifecycle status that mirrors the reference application:
  **Registered → Building → (Ready | Build Failed)**, and from Ready **→ Running → Ready**. Status
  transitions MUST be atomic and recorded with timestamps.
- **FR-004**: A user MUST be able to trigger a **Build** that obtains the repo and builds the app
  **inside a fresh, isolated, disposable container**; on completion the system MUST record success or
  failure, a one-line summary, and the full logs, and set status to Ready or Build Failed.
- **FR-005**: When no build command is supplied, the system MUST attempt a sensible auto-detected
  build appropriate to the repo's ecosystem (mirroring the reference), and fail with an explanatory
  summary if it cannot build.
- **FR-006**: A user MUST be able to trigger a **Run** of a Ready app that executes the built
  application **in an isolated, disposable container** under a configurable time limit; on completion
  the system MUST record the run outcome, summary, and logs, and return the app to Ready.
- **FR-007**: Every build and run MUST use a throwaway container that is disposed of after the
  operation; no container state persists between operations beyond the recorded outcome and the
  built artifact.
- **FR-008**: A build or run that exceeds its time limit, or that fails to start (e.g. container
  engine unavailable), MUST be recorded as failed with an explanatory reason and MUST NOT leave the
  app stuck in Building/Running.
- **FR-009**: Captured build/run/monitor logs MUST have secrets/tokens redacted, and no secret value
  MUST be persisted in plaintext or shown in any log or summary.
- **FR-010**: A user MUST be able to **link a chosen saved workflow** (selected from the existing
  Workflow gallery) to a registered app as its monitoring pipeline, and to change or remove that link.
- **FR-011**: While a linked app is running, the linked workflow MUST execute as its monitor on the
  same workflow-execution path used for any other workflow run (no special-casing of the monitoring
  workflow).
- **FR-012**: When the monitoring workflow detects a problem with the running app, the detection MUST
  feed back as a new bounded workflow run / intake attributable to that app (close-the-loop), and a
  recurring/ongoing problem MUST be raised once, not on every cycle (de-duplicated by a stable
  signature) — matching the reference's monitoring/close-the-loop behaviour.
- **FR-013**: The system MUST surface per-app **monitoring health**: the last monitoring cycle time,
  whether it succeeded, and the last error — mirroring the reference's trigger/heartbeat status.
- **FR-014**: The Apps experience MUST present, with parity to the reference admin console: an app
  **list** with status indicators and last build/run times; a **register-app** form; per-app
  **Build**, **Run**, **Link workflow**, and **Remove** actions; and an app **detail** view exposing
  build and run summaries and full logs.
- **FR-015**: The system MUST provide both a **simulated** executor (synthesizes build/run/monitor
  outcomes for demonstration without real containers, never hanging) and a **real** container
  executor, with identical screens, controls, status names, and lifecycle between them — only whether
  real work runs differs.
- **FR-016**: Removing an app MUST unregister it and remove it from the list; an in-flight build/run
  for the same app MUST NOT be duplicated by a concurrent trigger (prevented or queued).
- **FR-017**: A linked workflow that is later deleted or unlinked MUST NOT crash monitoring; the app
  MUST be reported as unlinked and monitoring MUST continue for other apps.
- **FR-018**: Each monitoring cycle MUST provide the linked workflow a **defined snapshot** of the
  app — its current status, its most recent run outcome and summary, and a bounded, secret-redacted
  tail of that run's logs — so "detecting a problem" is based on concrete, specified input rather
  than an unspecified live signal.

### Key Entities *(include if feature involves data)*

- **Monitored App (Registered App)**: A target repository registered for build/run/monitoring —
  name (unique), local repo path, optional branch, optional build command, required run command,
  current status, last build result, last run result, and an optional linked monitoring workflow.
- **App Status**: The lifecycle state of a Monitored App — Registered, Building, Ready, Build Failed,
  Running — transitioning exactly as the reference application's app status machine does.
- **Build Result**: The outcome of a build — succeeded/failed, one-line summary, full (secret-redacted)
  logs, and timestamp.
- **Run Result**: The outcome of a run — succeeded/failed (incl. timeout), summary, full
  (secret-redacted) logs, and timestamp.
- **Disposable Execution Container**: A fresh, isolated, throwaway sandbox created per build and per
  run and discarded afterward; the place "where the work happens."
- **Workflow Monitoring Link**: The association between a Monitored App and a user-chosen saved
  workflow that monitors it; one app is monitored by at most one workflow at a time.
- **Monitoring Cycle / Heartbeat**: A record of the latest monitoring pass for an app — when it ran,
  whether it succeeded, and any last error — plus de-duplication of recurring detections.
- **Saved Workflow**: An existing user-built workflow from the Workflow gallery, reused unchanged as
  the monitoring pipeline (this feature does not change how workflows are authored or executed).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can register a local repo as a monitored app and see it persisted across a
  reload/restart in 100% of attempts, with invalid registrations (bad path, duplicate name, missing
  run command) rejected with a clear message rather than creating an unusable app.
- **SC-002**: For a registered app, a user can trigger Build and then Run and observe the status move
  Registered → Building → Ready → Running → Ready, with build and run summaries and full logs
  available afterward, in 100% of successful attempts.
- **SC-003**: Every build and run leaves no lingering container behind, and no operation remains stuck
  in Building/Running — a timeout, a start failure, or an engine outage always resolves to a recorded
  failure with an explanatory reason.
- **SC-004**: A user can link any saved workflow to an app, and a detected problem during monitoring
  produces exactly one new workflow run/intake for an ongoing issue (not one per cycle), attributable
  to that app.
- **SC-005**: Per-app monitoring health (last cycle time, success/failure, last error) is visible and
  updates after each monitoring cycle.
- **SC-006**: A first-time viewer who has used the reference application's admin console recognises
  the Apps screens, controls, status names, and the build → run → monitor lifecycle as equivalent —
  the behaviour and surfaces match the reference ("the same manner").
- **SC-007**: The full register → build → run → monitor flow is demonstrable end-to-end with the
  simulated executor on a machine that has no container engine and no real target repo, with the same
  screens and status transitions as the real executor.
- **SC-008**: No secret value ever appears in a persisted record, a displayed log, or a summary.

## Assumptions

- The repo is referenced by a **local filesystem path** (per clarification); obtaining a remote repo
  by URL is not required for this feature, though the registration model should not preclude it later.
- The **reference application** (`C:\ProjectsWin\DBAI` workflow-poc, LangGraph) is the behavioural and
  architectural source of truth for app registration, the build/run-in-a-throwaway-container model,
  the workflow-to-app linking, the monitoring/close-the-loop behaviour, and the admin-console
  surfaces. "Exactly the same manner" means parity of observable behaviour and surfaces, not a
  line-for-line port of a different language/stack.
- The **chosen workflow** is an existing saved workflow from the current Workflow gallery, reused
  unchanged; this feature does not alter how workflows are authored, realized, or executed — it adds
  the ability to point a workflow at a running app as its monitor.
- "Run its application" means build then run the target repo's own application (via its build/run
  commands) inside a disposable container; this feature does not interpret or modify the target
  repo's source.
- A **simulated** executor exists for demonstration parity (mirroring the reference's sim executor),
  so the feature is fully demonstrable without real container infrastructure.
- Existing application capabilities are reused rather than rebuilt (framework-first): the saved-workflow
  store and gallery, the workflow-execution/run-recording path, the connector-style configuration and
  encrypted-secret storage pattern, the run/event observability and live-status surfaces, and the
  primary navigation. The genuinely new capability is orchestrating a target repo's build/run inside a
  disposable container and linking a workflow to monitor it.
- Each app is monitored by at most one workflow at a time (the reference links one monitor per app).
- Monitored apps are **owner-scoped** like saved workflows: a registered app belongs to the user who
  created it, and its name is unique **per owner**.
- "Monitor the running app" observes the app's **latest run plus status** via a defined snapshot
  (FR-018); the app is not required to be a continuously running process for monitoring to function.

## Dependencies

- The existing **saved-workflow storage and Workflow gallery** (the source of the chosen monitoring
  workflow).
- The existing **workflow-execution and run-recording path** (the same path the linked monitoring
  workflow runs on, with its run history, events, and live status).
- The existing **connector-style configuration pattern and encrypted-at-rest secret storage** (the
  pattern the app-registration configuration and any per-app secrets follow).
- The existing **primary navigation and page conventions** (where the new Apps surface attaches) and
  the existing **live-status (real-time update) surface** (for build/run/monitor progress).
- A **container engine / disposable-sandbox capability** for real build/run execution — the one
  genuinely new piece of infrastructure — with a **simulated** executor as the no-infrastructure
  fallback.
- The **reference application** at `C:\ProjectsWin\DBAI` as the behavioural specification to mirror.
- Out of scope (explicit non-dependencies): deploying *this* product itself to a public/Azure URL for
  sharing (a separate concern); modifying the target repo's source; changing how workflows are
  authored or executed; supporting remote repo URLs/cloning in this iteration; and monitoring more
  than one workflow per app simultaneously.
