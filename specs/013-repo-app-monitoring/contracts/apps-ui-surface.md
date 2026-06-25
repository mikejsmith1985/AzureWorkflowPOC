# Contract: Apps UI Surface

Blazor Server pages that present the app registry, build/run actions, logs, workflow linking, and
monitoring health — with parity to the reference admin console (FR-014). Follows the existing
`WorkflowGallery.razor` / `ConnectorSettings.razor` page conventions and reuses the SignalR live-status
surface.

## Navigation
- New nav link **"Apps"** in `MainLayout.razor`, placed near "Workflow Builder" / "Run History".

## Page: Apps (`/apps`) — list + register + per-app actions
- **App list**: one card/row per registered app showing **name**, **repo path**, **branch**,
  **status badge** (`AppStatusBadge`: Registered=neutral, Building=in-progress, Ready=success,
  BuildFailed=error, Running=active), **last built** and **last run** times, and **last run outcome**.
- **Register App** form (modal/panel): inputs **Name**, **Repo local path**, **Branch** (optional),
  **Build command** (optional — placeholder notes auto-detect), **Run command** (required). Submit
  validates (unique name, path exists, run command present) and shows a clear inline error on failure.
- **Per-app actions**: **Build**, **Run** (enabled per status), **Link workflow**, **Open** (detail),
  **Remove**. Build/Run reflect live status transitions via SignalR without reload.
- **Link workflow** control: a picker populated from the current user's saved workflows
  (`IWorkflowRepository.ListByOwnerAsync`); selecting one links it as the monitor; clearing unlinks.

## Page: App Detail (`/apps/{AppId}`)
- **Header**: name, repo path, branch, status badge, linked-workflow name (or "Not monitored").
- **Build section**: last build summary + expandable **full logs** (secret-redacted).
- **Run section**: last run outcome + summary + expandable **full logs**.
- **Monitoring health**: last cycle time, ok/fail, last error (from `IAppHeartbeatStore`); list of
  runs/intakes raised for this app (links into the existing Run History/Run Detail).
- **Actions**: Build, Run, Link/Unlink workflow, Remove.

## Demo/sim parity
- With the simulated executor active, all screens, controls, status names, and transitions are
  identical to real mode (FR-015) — only whether a real container runs differs. A small indicator
  shows whether the active executor is **Simulated** or **Docker**.

## Live status
- App status badges and build/run progress update in place via the existing SignalR hub
  (`WorkflowRunHub` / app status broadcast), matching Run History's real-time behavior.
