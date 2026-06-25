# Contract: Connector Settings — Messaging section (UI)

**Feature**: 010-messaging-connector · Page: `ConnectorSettings.razor` (`/settings/connectors`)

Replaces the former "Microsoft Teams" card. Mirrors the LLM provider-dropdown pattern (#27).

## Fields

| Label | Bound to | Secret? | Notes |
|-------|----------|---------|-------|
| Platform | `Platform` | No | `<select>`: Microsoft Teams / Slack / Discord. Required. |
| MCP Server URL | `McpServerUrl` | No | Optional. Empty → webhook path. |
| MCP Tool Name | `McpToolName` | No | Shown/required when MCP Server URL set. |
| MCP Argument Template | `McpArgumentTemplate` | No | Optional JSON; placeholder shows the default `{"target":"{{target}}","text":"{{message}}"}`. |
| Target (channel / recipient) | `Target` | No | Required when MCP path used. |
| MCP Auth Token | secret `mcpAuthToken` | **Yes** | `type=password`, "leave blank to keep existing". |
| Webhook URL | secret `webhookUrl` | **Yes** | `type=password`, "leave blank to keep existing". Enables fallback. |

## Behavior

| # | Given | When | Then |
|---|-------|------|------|
| U1 | Card loaded | render | Header "Messaging"; Configured/Not-configured badge; Healthy/Unhealthy badge when a test result exists. |
| U2 | Edit opened | toggle | Non-secret fields pre-filled from stored config; secret fields blank (Article IX). |
| U3 | Save with blank secret | save | Stored secret preserved; non-secret fields persisted; prior test result cleared (FR-012). |
| U4 | Platform changed + saved | save | Only the saved platform's config persists; stale prior test result cleared. |
| U5 | "Check Health" clicked | click | Calls health check; result names platform + path (MCP/webhook) (FR-009). |
| U6 | Stored secret undecryptable | health check | Reports "re-enter credentials"; no crash. |

## Test coverage (Article V)

- Playwright: Messaging card renders, platform `<select>` switches the visible/required fields, Save
  round-trips non-secret fields, secret fields never pre-populate.
- Health-check result string asserts presence of platform name and path label.
