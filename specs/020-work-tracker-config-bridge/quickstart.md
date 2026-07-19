# Quickstart: Work-Tracker Config Bridge — Validation Guide

End-to-end scenarios that prove the bridge. Run the app via `scripts/start-web.ps1` (stop via
`scripts/stop-web.ps1`); E2E via `scripts/run-e2e.ps1`. Constitution Article II: never wildcard-kill
`dotnet`; target the PID `stop-web.ps1` prints.

## Prerequisites

- A Jira Cloud site with an API token (Site URL, account email, token, a project key).
- Optional: an ADO org/project/PAT to validate the ADO regression + migration path.

## Scenario 1 — Configure Jira entirely from the UI (US1, SC-001)

1. Start the app; open **Settings → Connectors**.
2. Confirm a single **Work Tracking System** card (no standalone "Azure DevOps" card) — FR-002, SC-006.
3. Set provider = **Jira**; enter Site URL, email, API token, project key; **Save**.
4. **Expected**: no `WorkTracker:Jira` env/appsettings entries exist anywhere, yet the config persists.

## Scenario 2 — Test Connection for Jira (US3, SC-004)

1. On the Jira card, click **Test Connection**.
2. **Expected (valid)**: pass within a few seconds, message naming the authenticated account + project; no
   Jira issue created.
3. Enter a wrong token → **Test Connection** → **Expected**: fail with "token invalid or expired"; wrong
   project key → "project key not found or no access".

## Scenario 3 — Run a ticket onto Jira (US1, SC-001)

1. With Jira saved + tested, run a demo ticket through the pipeline.
2. **Expected**: a Jira issue is created; the binding key + cost fields are set; run comments are appended —
   observed on the real Jira issue, via the existing adapter with no pipeline code change.

## Scenario 4 — Switch providers live, no restart (US2, SC-002)

1. With a run completed on Jira, switch provider = **Azure DevOps** (enter ADO creds if needed); **Save**.
2. Run another ticket **without restarting the app**.
3. **Expected**: the new run targets ADO; switch back to Jira and the next run targets Jira — each without a
   restart (per-run resolution, FR-005).

## Scenario 5 — Existing ADO deployment upgrades cleanly (US4, SC-003)

1. Start from a DB that has an `AzureDevOps` connector row (pre-upgrade state).
2. Launch the new build.
3. **Expected**: no operator action; the Work Tracking System card shows provider = Azure DevOps with the
   existing org/project (secret preserved); a run behaves exactly as before. Restart again → migration is a
   no-op.

## Scenario 6 — Secrets never leak (SC-005)

1. Re-open a saved connector (ADO or Jira).
2. **Expected**: the secret field is blank (never pre-filled); the token/PAT never appears in the page, logs,
   or the DB in plaintext (encrypted blob only).

## Regression gate

- `dotnet test` — ADO parity suite green with no changed expectations beyond the generic presentation (SC-003).
- `scripts/run-e2e.ps1` — Connectors tab: provider select renders, provider-conditional fields, Test
  Connection for both providers, save/reload round-trip.
