# Phase 1 Data Model: Multi-Platform Messaging Connector

**Feature**: 010-messaging-connector · **Date**: 2026-06-25

Entities are expressed as the persisted/transferred shapes. Secret material is **never** part of a
non-secret shape and never returned to the UI (Article IX).

---

## Enum: `MessagingPlatform`

The instant-message platform a Messaging connector targets.

| Member | Notes |
|--------|-------|
| `Teams` | Microsoft Teams. Webhook body = Adaptive Card; success body == `"1"`. |
| `Slack` | Slack. Webhook body = `{ "text": ... }`; success body == `"ok"`. |
| `Discord` | Discord. Webhook body = `{ "content": ... }`; success = HTTP 204. |

**Extensibility (FR-014)**: adding a platform = add an enum member + a `PlatformWebhookProfile`
(payload builder + success predicate). No other type changes.

---

## Enum change: `ConnectorType`

`ConnectorType.Teams` is **renamed** to `ConnectorType.Messaging`. The other members (`ServiceNow`,
`AzureDevOps`, `LLM`) are unchanged. Legacy persisted string `"Teams"` maps to `Messaging` on read
(FR-013).

---

## Entity: `MessagingConnectorConfig` (non-secret)

Serialized to `ConnectorConfig.NonSecretConfig` (JSON). Mirrors the existing
`ServiceNowConnectorConfig` / LLM non-secret records.

| Field | Type | Required | Rules |
|-------|------|----------|-------|
| `Platform` | `MessagingPlatform` | Yes | One of Teams/Slack/Discord. |
| `McpServerUrl` | `string?` | No | Absolute `http`/`https` URL of the remote MCP (SSE/streamable HTTP) endpoint. When set → MCP path is used. |
| `McpToolName` | `string?` | Required iff `McpServerUrl` set | Name of the send-message tool to invoke. |
| `McpArgumentTemplate` | `string?` | No | JSON object string with `{{target}}`/`{{message}}` placeholders. Defaults to `{"target":"{{target}}","text":"{{message}}"}` when null/blank. |
| `Target` | `string?` | Required iff MCP path used | Channel id / recipient identifier substituted for `{{target}}`. (Webhook path: target is encoded in the webhook URL, so this may be empty.) |

**Validation**:
- If `McpServerUrl` is non-empty it MUST be an absolute http/https URI and `McpToolName` MUST be
  non-empty (else the config is "MCP partially configured" → Test Connection fails with a clear
  message).
- `McpArgumentTemplate`, when provided, MUST be a parseable JSON object.
- A connector is **usable** when EITHER (`McpServerUrl` + `McpToolName` [+ `Target`]) OR a stored
  webhook URL is present.

---

## Entity: `MessagingSecrets` (encrypted)

Serialized into the connector's encrypted secrets blob via `ISecretProtector`. Never returned to UI;
"leave blank to keep existing" on save (FR-011).

| Field | Type | Required | Rules |
|-------|------|----------|-------|
| `McpAuthToken` | `string?` | No | Bearer token sent to the MCP server (Authorization header) when present. |
| `WebhookUrl` | `string?` | No | Direct incoming-webhook URL for the platform. Treated as a credential (encrypted). Presence enables the webhook fallback path. |

---

## Value object: `PlatformWebhookProfile` (in-memory, per platform)

Encapsulates each platform's fallback contract. Not persisted.

| Member | Type | Purpose |
|--------|------|---------|
| `Platform` | `MessagingPlatform` | Key. |
| `BuildBody(message)` | `string` (JSON) | Produces the platform-correct request body. |
| `IsSuccess(statusCode, body)` | `bool` | Platform success predicate (Teams `"1"`, Slack `"ok"`, Discord 204). |

---

## Delivery selection (derived behavior, FR-005)

```
resolve(config, secrets):
  if config.McpServerUrl is non-empty:        → DeliveryPath.Mcp
  else if secrets.WebhookUrl is non-empty:    → DeliveryPath.Webhook
  else:                                       → NotConfigured  (Test/send fails clearly)
```

`DeliveryPath` ∈ { `Mcp`, `Webhook`, `NotConfigured` }. Selection is **configuration-based**, not
runtime-failover: if the MCP path is selected and the server is unreachable, the result is a failure
(non-blocking for pipeline runs) — it does **not** silently fall back to webhook (per Edge Cases).

---

## Outbound message (transient)

The content delivered; not persisted as an entity.

| Origin | Composition |
|--------|-------------|
| HITL notification | Title + numbered questions + portal deep-link (existing `IHitlNotifier.NotifyAsync` inputs). |
| Notify node | Realized message text from the workflow node payload. |
| Test connection | A fixed, clearly labelled connectivity-test string. |

For MCP delivery the message text is substituted for `{{message}}` in the argument template; for
webhook delivery it is placed into the platform body by `PlatformWebhookProfile.BuildBody`.

---

## Relationship to existing model

- `ConnectorConfig` (existing): unchanged shape; `Type` now yields `Messaging`. `NonSecretConfig`
  holds `MessagingConnectorConfig`; encrypted blob holds `MessagingSecrets`.
- `NotifyNodeConfig.Connector` (existing): continues to reference `ConnectorType` — now `Messaging`.
- No new tables, no schema migration (R5).
