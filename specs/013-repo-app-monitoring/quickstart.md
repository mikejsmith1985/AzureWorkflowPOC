# Quickstart / Validation Guide: Repo-App Build/Run/Monitor

Runnable scenarios that prove the feature end-to-end. They map to the spec's user stories and
acceptance scenarios. Implementation details live in `tasks.md`; this is a run/validate guide.

## Prerequisites
- .NET 8 SDK (pinned via user-local `global.json`).
- For **real** container mode: a reachable Docker engine (Docker Desktop). Without it, the app falls
  back to the **simulated** executor and every scenario below still runs (US4).
- Start the web app the standard way: `scripts/start-web.ps1` (or `dotnet run --project src/DBAIAzure.Web`).
- E2E: `scripts/run-e2e.ps1` (Playwright, headless Chromium) — never by building the binary directly.

## Scenario 1 — Register a local repo as an app (US1)
1. Navigate to **Apps** (`/apps`).
2. Click **Register App**; enter a Name, a valid local repo path, and a Run command; leave Build
   command blank. Submit.
3. **Expect**: the app appears in the list with status **Registered**; reloading the page still shows
   it (persisted).
4. Try registering a second app with the **same name**, or a **non-existent path**, or **no run
   command**. **Expect**: a clear inline rejection; no broken app created.

## Scenario 2 — Build then run in a throwaway container (US2)
1. For the registered app, click **Build**. **Expect**: status moves **Registered → Building → Ready**
   (or **Build Failed** with a summary + logs). With Docker present, a build container ran and was
   removed; with sim, a synthesized success.
2. Click **Run**. **Expect**: status moves **Ready → Running → Ready**; the run outcome, summary, and
   full logs are visible in **App Detail**.
3. Register an app whose run command sleeps beyond the configured timeout and Run it. **Expect**: the
   run is recorded as **TimedOut**/failed and the app returns to Ready — it never hangs.
4. Confirm (Docker mode) no leftover container remains after build/run (`docker ps -a` shows none for
   the app) — each operation used a throwaway container.
5. Open **App Detail** and confirm logs contain **no plaintext secret** (redaction).

## Scenario 3 — Link a workflow and monitor (US3)
1. Ensure at least one saved workflow exists in the gallery.
2. On the app, **Link workflow** and pick that workflow. **Expect**: the app shows it is monitored by
   that workflow.
3. With the app running and the monitoring loop active, **Expect**: the linked workflow executes as the
   monitor on the normal execution path; a detected problem creates **one** new run/intake attributable
   to the app (visible in Run History), and a recurring problem is **not** re-raised every cycle.
4. View **monitoring health** on App Detail. **Expect**: last cycle time, ok/fail, last error update
   each cycle.
5. Unlink (or delete) the workflow. **Expect**: monitoring reports the app as unlinked and does not
   crash; other apps keep monitoring.

## Scenario 4 — Full flow with no container engine (US4)
1. Stop/disable Docker (or select demo mode). The active-executor indicator shows **Simulated**.
2. Repeat Scenarios 1–3. **Expect**: identical screens, controls, status names, and transitions; all
   outcomes synthesized; nothing hangs.

## Automated validation (evidence — Article X)
- **Unit** (`dotnet test`, `DBAIAzure.Tests/Apps`): status machine; registry validation (dup name /
  bad path / missing run cmd); `SimAppExecutor` never hangs; build-command auto-detect; log redaction;
  monitoring-cycle dedup (close-the-loop raises once).
- **Integration** (env-gated, real Docker): `DockerAppExecutorTests` builds + runs a tiny fixture repo
  in a throwaway container and asserts captured logs + cleanup + timeout behavior.
- **E2E** (`run-e2e.ps1`): `AppsPageTests` — register → build → run → link workflow, asserting status
  badges, log surfaces, and the monitoring-health panel (sim mode, so it runs anywhere).

## Maps to success criteria
SC-001 ↔ Scenario 1; SC-002/003/008 ↔ Scenario 2; SC-004/005 ↔ Scenario 3; SC-006/007 ↔ Scenarios 3–4.
