# Quickstart: Validate the Intelligent DoR Validation Workflow

End-to-end validation scenarios that prove the feature works. Assumes the app runs locally
(`scripts/start-web.ps1`, http://localhost:5000) with the SDK from `global.json`. Secrets are injected from the
vault by reference — never pasted (Article IX). See [contracts/](./contracts/) and [data-model.md](./data-model.md)
for shapes; this is a run/validation guide, not implementation.

## Prerequisites

- Jira Cloud project reachable (e.g. `SBRO` on the configured site) with a bot account + API token in the vault.
- Slack workspace with the bot in the primary and escalation channels; Slack token in the vault; the MCP gateway
  extended with a thread-read tool (D4).
- An LLM connector configured (BYO key; not deployed to cloud).
- A DoR document reachable at a URL (or pasted inline in config).

## Setup — configure the DoR Workflow connector

1. **Connectors → DoR Workflow** card. Fill the six namespaces (see
   [contracts/dor-config-schema.md](./contracts/dor-config-schema.md)): Jira project/transition/whitelist, DoR
   source (`url` + URI or `inline`), AI model/prompts, primary/escalation/success channels + timeouts +
   iterations, SLA hours + business-hours, audit options.
2. Leave **dry-run = ON** for first validation.
3. **Check Health** — verifies Jira reachability + `ready_transition_id` exists, Slack channels reachable, DoR
   document loads, AI key valid. Expect all green before proceeding.
4. Register the Jira webhook (issue_created) pointing at `/webhooks/jira` with the HMAC secret from the vault.

## Scenario A — Ready ticket auto-advances (US1, dry-run then live)

1. Create a well-formed Jira ticket (title, description, acceptance criteria, estimate).
2. **Dry-run**: confirm an audit "would-do: transition → Ready to Work; success notify" entry; ticket status
   unchanged; no Slack message. ✅ proves review + pass path without side effects.
3. Turn **dry-run = OFF**, create another ready ticket → ticket transitions to the ready status; success notice
   posted (if enabled); audit tagged **PASSED**. ✅ SC-001.

## Scenario B — Not-ready ticket resolved by conversation (US2)

1. Create a ticket missing acceptance criteria.
2. Confirm a gap message naming the unmet criterion + ticket link is posted to the **primary** channel; the
   instance is `AwaitingResponse`. ✅ FR-009.
3. **Restart the app** (`stop-web.ps1` → `start-web.ps1`) while it waits. Confirm the instance rehydrates and is
   still `AwaitingResponse`. ✅ SC-003 / FR-010.
4. Reply **in-thread** with acceptance criteria. Confirm: AI evaluates → resolved → **only whitelisted** fields
   written to Jira → ticket transitioned to ready → internal comment summarizing changes → audit **RESOLVED_AUTO**.
   ✅ SC-002.
5. Variant — partial reply: reply resolving one of two gaps → confirm a focused follow-up about only the
   remaining gap and `PrimaryIterations` increments. ✅ FR-013.

## Scenario C — SLA breach → escalation → manual handoff (US3/US4)

1. Set a short `primary_sla_hours` (e.g. business-hours math yielding minutes) and create a not-ready ticket; do
   not reply.
2. At the deadline, confirm the **SLA sweeper** posts an escalation summary to the **escalation** channel, resets
   the iteration counter, and starts the escalation clock (`SlaTier = Escalation`). ✅ FR-017.
3. Exhaust the escalation SLA/iterations without resolving. Confirm: final message posted, ticket tagged
   `manual_label`, internal summary comment added, **status unchanged**, audit **MANUAL_REQUIRED**. ✅ SC-004 /
   FR-020.
4. Variant — reply after reply-timeout but before SLA: confirm it is still processed. ✅ FR-015.

## Scenario D — Whitelist & config hot-reload (US6, SC-005/006)

1. In config, change `ready_status`/`ready_transition_id` (or an SLA, or the DoR document); create a new ticket;
   confirm the new value is used **without restart**. ✅ SC-005.
2. Craft a reply that would tempt the AI to set a non-whitelisted field; confirm that field is **never** written
   (dropped by the programmatic filter). ✅ SC-006 / FR-021.
3. Inspect config/logs/db — no secret value appears in plaintext. ✅ SC-007.

## Scenario E — Builder default (US5)

1. **Automation → Workflow Builder → new**. Confirm the canvas loads the **DoR Validation Workflow** graph (not
   "Support Request Flow", which is gone everywhere). ✅ FR-027 / SC-008.
2. **Make it real** — confirm every node realizes (including the human-conversation node → RequestPort) with no
   unrealized placeholders; save succeeds. ✅ FR-028.
3. Open each DoR node's config panel — confirm node-appropriate settings + validation, and that an incomplete
   config blocks the run. ✅ FR-029.

## Automated coverage

- **Unit** (`tests/DBAIAzure.Tests/Dor/`): config resolve/validate; business-hours SLA math; reply-eval routing
  (resolved/partial/unresolved); whitelist filter; dry-run gate; each state transition; doc-source cache/fallback.
- **Integration** (`.../Dor/Integration/`): MAF graph runs for pass, fail→resolve, escalation, manual-exit, and
  restart-resume (checkpoint rehydration) against in-memory SQLite + fake Jira/Slack handlers.
- **E2E** (`tests/DBAIAzure.E2ETests`, `scripts/run-e2e.ps1`): builder loads the DoR default; DoR config card
  save + health; make-it-real realization.
