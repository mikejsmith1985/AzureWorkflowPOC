# Quickstart & Validation Guide: Per-Workflow Graph View & Trustworthy Node Editing

A runnable guide proving the three user stories end-to-end. Details live in [plan.md](./plan.md),
[research.md](./research.md), [data-model.md](./data-model.md), and [contracts/](./contracts).

## Prerequisites

- Pinned .NET 8 SDK (repo `global.json`).
- The Blazor Server app `DBAIAzure.Web` (SQLite auto-created on startup).
- Playwright browsers installed for E2E (`scripts/run-e2e.ps1` handles launch on port 5099).

## Build & test

```powershell
dotnet build
dotnet test                       # unit + integration (xUnit)
./scripts/run-e2e.ps1             # Playwright E2E against real Kestrel (port 5099)
```

## Validate US1 — node edits actually persist, commit lives by the fields

1. Run the app; open **Workflow Builder**; double-click the **Start / Trigger** node.
2. Edit **"What starts this workflow?"** and **"What information is available at the start?"**.
3. Confirm a **Save** control sits **in/next to the panel** (not only in the top toolbar).
4. Commit via that in-panel Save.
5. Reload the page (or navigate away and back) so the workflow reloads from storage.
6. **Pass:** both edited values are shown on the node and in the panel — no reversion to prior/default.
7. **Also verify the old failure path is fixed:** edit a field, then use the **top-toolbar Save**
   (without any separate "Done" step), reload — the edit still persists.
8. **Failure injection:** force a save error → a toast/banner appears; success is never shown over
   lost data.

Expected: FR-001..FR-004, FR-012; SC-001, SC-002.

## Validate US2 — per-workflow Graph view from real data; standalone graph gone

1. In the primary nav, confirm there is a **Workflows** entry and **no** standalone **Graph** entry;
   visiting `/graph` no longer serves the old hardcoded page.
2. Open **Workflows**; on any workflow card, click **Graph**.
3. **Pass:** a read-only, auto-laid-out diagram of **that** workflow's real steps and connections
   renders, with understandable step labels and labelled connections.
4. Open the same workflow in the builder, rename/add a step, save; reopen its Graph view.
5. **Pass:** the diagram reflects the change (proves it is generated from real data, not static).
6. Use the **Open in builder** link from the Graph view to round-trip back to editing.
7. Edge cases: a single-node and a disconnected-node workflow render without error; an unknown id
   shows a clear not-found state (not a crash/blank).

Expected: FR-005..FR-009, FR-011; SC-003, SC-004, SC-007.

## Validate US3 — seeded Intake Pipeline reproduces the old topology

1. Start the app fresh; open **Workflows**.
2. **Pass:** an **Intake Pipeline** workflow is present as a real, openable card.
3. Open its **Graph** view.
4. **Pass:** the diagram shows sources → intake → validation → branch (gap-analysis / estimation) →
   human pause → action → done, including the validation branch and the HITL pause.
5. Restart the app; confirm there is still exactly **one** Intake Pipeline (idempotent seed).
6. Edit the seeded workflow, save, restart; confirm your edits are **not** overwritten by re-seeding.

Expected: FR-010; SC-005.

## Evidence to capture (Article X — Verification & Proof)

- `dotnet test` summary (unit + integration green), including the new
  `WorkflowMermaidGeneratorTests`, `IntakePipelineSeederTests`, and the node-persistence test.
- Playwright run output (`NodeEditPersistenceTests`, `WorkflowGraphViewTests`) green.
- A screenshot of a real workflow's Graph view and of the edited-then-reloaded node text.
