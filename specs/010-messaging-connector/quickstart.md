# Quickstart & Validation: Multi-Platform Messaging Connector

**Feature**: 010-messaging-connector

Runnable scenarios that prove the feature end-to-end. Details live in
[data-model.md](./data-model.md) and [contracts/](./contracts/).

## Prerequisites

- The solution builds with the pinned SDK (`global.json`).
- `ModelContextProtocol.Core` package referenced by `DBAIAzure.Connectors`.
- For the live MCP scenario: a reachable MCP server endpoint exposing a send-message tool.
- For the webhook scenario: a valid incoming-webhook URL for Teams, Slack, or Discord.

## Build & test

```pwsh
# Unit + design tests (fast, mocked)
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test tests/DBAIAzure.Tests/DBAIAzure.Tests.csproj

# E2E (Playwright) — Messaging settings card
./scripts/run-e2e.ps1
```

## Scenario 1 — Webhook fallback (no MCP), per platform (US1, US3 / SC-001)

1. Run the app; open `/settings/connectors`; edit the **Messaging** card.
2. Select **Slack**; leave MCP fields blank; paste a Slack incoming-webhook URL into **Webhook URL**; Save.
3. Click **Check Health** → expect a success result naming **Slack** and the **webhook** path; a test
   message appears in the Slack channel.
4. Repeat with **Discord** (expect 204-based success) and **Microsoft Teams** (expect `"1"`-based success).

**Pass**: each platform reports success and the labelled test message arrives.

## Scenario 2 — MCP-first delivery (US3 / SC-003)

1. Edit the Messaging card; keep the same platform; set **MCP Server URL**, **MCP Tool Name**, **Target**,
   and (optionally) an **MCP Auth Token** and **MCP Argument Template**; Save.
2. Click **Check Health** → expect success naming the platform and the **MCP** path.
3. Remove the **MCP Server URL** (leave the webhook); Save; Check Health → now reports the **webhook**
   path — same connector, no other change.

**Pass**: with MCP url set, path == MCP; with it cleared, path == webhook (SC-003).

## Scenario 3 — HITL notification (US2 / SC-002, SC-006)

1. With the Messaging connector configured, drive a pipeline run into the **AwaitingHuman** state.
2. Confirm a message arrives on the linked platform containing the clarifying questions and a working
   portal deep-link.
3. Misconfigure the connector (revoke the webhook / wrong MCP url) and repeat → the run still reaches
   AwaitingHuman; the failure is logged; nothing throws (SC-006, FR-010).

## Scenario 4 — Secret hygiene (SC-005 / FR-011)

1. Save a connector with an MCP Auth Token and a Webhook URL.
2. Re-open Edit → secret fields are blank ("leave blank to keep existing"); Save without re-typing →
   secrets preserved (Check Health still passes).
3. Inspect the page source and application logs → **no** token or webhook URL value appears.

## Expected outcomes (traceability)

| Scenario | Requirements | Success Criteria |
|----------|--------------|------------------|
| 1 | FR-001, FR-002, FR-005, FR-006, FR-009 | SC-001, SC-004 |
| 2 | FR-003, FR-005, FR-007 | SC-003, SC-004 |
| 3 | FR-008, FR-010 | SC-002, SC-006 |
| 4 | FR-011 | SC-005 |
