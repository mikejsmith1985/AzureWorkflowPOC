# Feature Specification: Multi-Platform Messaging Connector

**Feature Branch**: `feature/messaging-connector`

**Created**: 2026-06-25

**Status**: Draft

**Input**: User description: "Generalize the existing single-purpose 'Teams' connector into a multi-platform 'Messaging' connector that lets an operator link the pipeline to any direct-message / instant-message platform — Microsoft Teams, Slack, and Discord in v1 — connecting MCP-first (the app acts as an MCP client to a per-platform MCP server) with a direct-webhook fallback."

---

## Clarifications

### Session 2026-06-25

- Q: What should the generalized connector section be called? → A: **Messaging**.
- Q: Which messaging platforms must v1 support? → A: **Microsoft Teams, Slack, and Discord**, with the design open to adding more as a small, well-defined extension.
- Q: How is a platform connected? → A: **MCP-first**. The application is an MCP client that delivers messages by calling a per-platform MCP server's send-message tool.
- Q: How are MCP servers reached from the long-running web app? → A: **Remote MCP endpoint (HTTP/SSE streamable transport URL)** — no local subprocess management.
- Q: What happens when a platform has no MCP server configured? → A: **MCP-first with webhook fallback** — delivery falls back to the platform's direct incoming-webhook URL, preserving today's working delivery path.
- Q: Are bot/OAuth token flows, threading, or inbound message receiving in scope? → A: **No** — out of scope for v1.
- Q: How does the connector know what argument names/shape to pass to the configured MCP tool? → A: **A configurable JSON argument template** per connector, with `{{target}}` and `{{message}}` placeholders the connector substitutes at send time (server-agnostic — works with any tool's argument names).

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Link a messaging platform and confirm it works (Priority: P1)

An operator opens the Connector Settings page and finds a **Messaging** section (where the
Teams-only connector used to be). They choose their platform — Microsoft Teams, Slack, or
Discord — from a dropdown, provide the connection details for that platform, save, and click
**Test Connection**. They want clear confirmation that the pipeline can reach their chat
channel, and a plainly worded reason if it cannot.

**Why this priority**: Linking a channel is the entry point for every other messaging behavior.
Without a configured, verified connector, no notification can ever be delivered.

**Independent Test**: With no messaging connector configured, select each platform in turn,
enter valid connection details, save, and run Test Connection — a clear success result must
appear naming the platform and the path used. Entering invalid details must produce an
actionable failure message, not a silent or generic error.

**Acceptance Scenarios**:

1. **Given** no messaging connector is configured, **When** the operator selects "Slack",
   enters a valid MCP server endpoint, tool name, and target channel, saves, and clicks Test
   Connection, **Then** a success result is shown stating the platform (Slack) and that the MCP
   path was used.
2. **Given** a platform is selected with only a direct webhook URL (no MCP server), **When** the
   operator runs Test Connection, **Then** the test posts a labelled test message via the
   webhook and reports success naming the platform and the webhook path.
3. **Given** a configured connector, **When** the operator re-opens the section to edit it,
   **Then** non-secret fields are pre-filled and secret fields are blank with a "leave blank to
   keep existing" affordance; saving without re-entering a secret preserves the stored secret.

---

### User Story 2 — Be notified on the linked platform when the pipeline pauses for input (Priority: P1)

When a pipeline run pauses for human input (HITL), the responsible person should receive a
message on whichever platform the operator linked — Teams, Slack, or Discord — containing the
clarifying questions and a deep link back to the run, so they do not have to watch the web UI.

**Why this priority**: HITL notification is the connector's primary production purpose; a missed
pause notification stalls a run indefinitely.

**Independent Test**: Configure each platform, drive a run into the AwaitingHuman state, and
confirm a message arrives on that platform containing the questions and a working portal link.

**Acceptance Scenarios**:

1. **Given** a configured Slack messaging connector, **When** a run enters AwaitingHuman,
   **Then** a message containing the questions and a portal deep-link is delivered to the Slack
   target.
2. **Given** the messaging connector is unreachable at pause time, **When** the notification
   fails, **Then** the failure is logged and the run still enters AwaitingHuman (notification is
   non-blocking — the pipeline never fails because of a missed ping).

---

### User Story 3 — Deliver via MCP when configured, fall back to webhook otherwise (Priority: P2)

The operator wants delivery to prefer their organization's MCP server for the platform (the
standard, reusable integration surface), but still work via a plain incoming webhook when no
MCP server is available for that platform.

**Why this priority**: This is the core "MCP-first, webhook fallback" routing promise. It
determines reliability and lets teams adopt MCP incrementally.

**Independent Test**: For one platform, configure both an MCP endpoint and a webhook and confirm
delivery uses MCP; remove the MCP endpoint and confirm the same connector now delivers via the
webhook — without changing any other field.

**Acceptance Scenarios**:

1. **Given** a connector with both an MCP endpoint and a webhook URL, **When** a message is sent,
   **Then** delivery is attempted via the MCP server's send-message tool.
2. **Given** a connector with a webhook URL and no MCP endpoint, **When** a message is sent,
   **Then** delivery uses the direct webhook with the platform-correct payload and success signal.
3. **Given** a connector with neither an MCP endpoint nor a webhook URL, **When** Test Connection
   runs, **Then** it fails with a message stating the connector is not configured.

---

### User Story 4 — See messaging status at a glance and switch platforms safely (Priority: P3)

The operator wants the Messaging section to show whether it is configured and healthy, the
selected platform, and to be able to change platforms without leftover, irrelevant fields from
the previously selected platform causing confusion.

**Why this priority**: Convenience and correctness for ongoing administration; not required for
first delivery but prevents misconfiguration.

**Acceptance Scenarios**:

1. **Given** a configured, recently tested connector, **When** the operator views the section,
   **Then** a "Configured" indicator, a Healthy/Unhealthy indicator, the platform name, and the
   last test message + age are visible.
2. **Given** the operator changes the selected platform, **When** they save, **Then** only the
   newly selected platform's configuration is persisted and a prior successful test result is
   cleared (a field change invalidates the prior test result).

---

### Edge Cases

- **MCP server unreachable / times out** → Test Connection and sends report a clear "could not
  reach the MCP server" failure; a send during a run is logged and non-blocking.
- **MCP tool name not found on the server** → reported as a distinct, actionable failure
  ("the named send-message tool was not found on the MCP server").
- **MCP server returns a tool error** → surfaced verbatim-but-sanitized (no secret echoed) as a
  failure result.
- **Webhook success signal mismatch** (e.g. HTTP 200 but unexpected body) → reported as a failure
  noting the URL may not be a valid incoming webhook for that platform.
- **Stored secret cannot be decrypted** (key rotation) → treated as "not configured — re-enter",
  never as a hard crash.
- **Legacy "Teams" connector value** ever encountered in storage → tolerated and treated as the
  Messaging connector with the Teams platform.
- **Platform changed but stale platform-specific fields remain** → only fields relevant to the
  saved platform are used; irrelevant leftovers are ignored.
- **Secret accidentally entered into a non-secret field** → non-secret fields (server URL, tool
  name, target) must never be treated as, or stored as, secrets.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST present a single connector section named **Messaging** (replacing
  the former Teams-only section) on the Connector Settings page, alongside ServiceNow, Azure
  DevOps, and the LLM provider.
- **FR-002**: The operator MUST be able to select a target **platform** — Microsoft Teams, Slack,
  or Discord — from a dropdown before entering connection details.
- **FR-003**: The system MUST allow configuring, per messaging connector, an MCP server endpoint
  (a remote HTTP/SSE streamable transport URL), the send-message tool name to invoke, a non-secret
  Target (channel or recipient identifier), and a non-secret **MCP argument template** — a JSON
  object with `{{target}}` and `{{message}}` placeholders describing the tool's input arguments.
- **FR-004**: The system MUST allow configuring an optional MCP authentication token and an
  optional direct incoming-webhook URL, both stored as encrypted secrets.
- **FR-005**: When sending or testing, the system MUST prefer the MCP path when an MCP server
  endpoint is configured, and MUST fall back to the direct webhook path when no MCP endpoint is
  configured for the selected platform (MCP-first, webhook fallback).
- **FR-006**: The webhook fallback MUST use the correct payload format and success signal for the
  selected platform: Microsoft Teams (Adaptive Card; success signalled by the body value "1"),
  Slack (text/Block Kit body; success signalled by the body "ok"), Discord (content body; success
  signalled by HTTP 204 No Content).
- **FR-007**: The MCP delivery path MUST invoke the configured send-message tool on the configured
  MCP server, building the tool's input arguments by substituting the Target and message text into
  the configured MCP argument template (`{{target}}`/`{{message}}` placeholders), and MUST interpret
  the tool result to determine success or failure. When no template is configured, a sensible default
  template (`{"target":"{{target}}","text":"{{message}}"}`) is used.
- **FR-008**: The system MUST support the same three behaviors for both delivery paths:
  (a) HITL pause notifications, (b) workflow notify-node message sends, and (c) the Settings
  "Test Connection" / health check.
- **FR-009**: The Test Connection / health-check result MUST state the platform and which delivery
  path (MCP or webhook) was used, and on failure MUST give an actionable reason.
- **FR-010**: HITL and notify-node delivery MUST be non-blocking: a delivery failure MUST be logged
  and MUST NOT fail or stall the pipeline run.
- **FR-011**: Secret values (MCP auth token, webhook URL) MUST never be returned to the UI, written
  to logs, or otherwise exposed; on edit, secret fields are blank with "leave blank to keep
  existing" semantics, and saving blank preserves the stored secret.
- **FR-012**: Saving the messaging connector MUST set it as configured and MUST clear any prior
  test result (a field change invalidates the previous Healthy/Unhealthy status), consistent with
  the other connectors.
- **FR-013**: The system MUST tolerate a legacy stored "Teams" connector identifier by treating it
  as the Messaging connector with the Microsoft Teams platform, without error.
- **FR-014**: Adding a new platform in the future MUST be a small, well-defined extension —
  selecting it in the dropdown and providing its payload format and success signal — without
  reworking the connector, notification, or settings architecture.
- **FR-015**: Connector configuration changes MUST take effect for subsequent sends without an
  application restart (hot-reload), consistent with the other connectors.

### Key Entities *(include if feature involves data)*

- **Messaging Connector Configuration**: The single configured messaging connector. Non-secret
  attributes: selected platform, MCP server endpoint, MCP tool name, MCP argument template
  (JSON with `{{target}}`/`{{message}}` placeholders), Target (channel/recipient), configured flag,
  last test result + timestamp. Secret attributes (encrypted): MCP auth token, webhook URL.
- **Messaging Platform**: An enumerated platform the connector can target — Microsoft Teams,
  Slack, Discord — each with its own webhook payload format and success signal.
- **Delivery Path**: The route a message takes — MCP (via a per-platform MCP server's send-message
  tool) or Webhook (direct incoming webhook) — chosen MCP-first with webhook fallback.
- **Outbound Message**: The content delivered — HITL questions + portal deep-link, a notify-node
  message, or a labelled connectivity test message — addressed to the configured Target.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can link any one of the three supported platforms and receive a clear
  pass/fail Test Connection result in under 2 minutes, without editing files or restarting the app.
- **SC-002**: For a correctly configured connector, a pipeline pause (HITL) results in a message
  arriving on the linked platform that contains the clarifying questions and a working link back
  to the run.
- **SC-003**: With both an MCP endpoint and a webhook configured, 100% of sends are delivered via
  the MCP path; with the MCP endpoint removed and nothing else changed, 100% of sends are
  delivered via the webhook path.
- **SC-004**: Every Test Connection result identifies both the platform and the delivery path used.
- **SC-005**: No secret value (MCP auth token or webhook URL) ever appears in the UI after save, in
  application logs, or in any persisted non-secret field — verified by inspection.
- **SC-006**: A delivery failure (unreachable MCP server, revoked webhook, etc.) never fails a
  pipeline run; the run still reaches its paused state and the failure is recorded in logs.
- **SC-007**: Adding a fourth platform requires changes confined to the platform definition (its
  payload format and success signal) — demonstrable as a localized change, with no edits to the
  settings page layout logic, notification trigger points, or path-selection logic.

## Assumptions

- No real Microsoft Teams connector configuration currently exists in the database, so no
  migration of live data is required; the rename is safe and only defensive handling of a legacy
  "Teams" identifier is needed.
- MCP servers are reached over a remote HTTP/SSE streamable transport; the application does not
  spawn or manage local MCP server subprocesses in v1.
- A single messaging connector is configured at a time (one platform/target), consistent with the
  current single-slot connector model; multiple simultaneous platforms are not required for v1.
- The "leave blank to keep existing" secret-handling and encryption-at-rest approach already used
  by the ServiceNow, Azure DevOps, and LLM connectors is reused unchanged.
- The MCP send-message tool accepts, at minimum, a target identifier and a text body; the exact
  argument names are not assumed — they are supplied by the operator's MCP argument template — and
  richer capabilities (threads, attachments, blocks) are not required for v1 delivery.
- Test Connection sends a clearly labelled, harmless test message that a human can recognize in
  the channel.

## Dependencies

- Existing connector configuration storage with encrypted-secret support and "leave blank to keep"
  semantics (the ServiceNow/ADO/LLM connector configuration subsystem).
- Existing HITL notification trigger (the pipeline's pause/AwaitingHuman path) and workflow
  notify-node execution path, which currently target the Teams connector.
- Existing Connector Settings page, status indicators, and "Test Connection"/health-check
  mechanism shared across connectors.
- Availability of a reachable per-platform MCP server endpoint (for the MCP path) and/or a valid
  incoming-webhook URL (for the fallback path), supplied by the operator.
- Out of scope (explicit non-dependencies): OAuth/bot-token flows (Slack bot, Microsoft Graph),
  message threading/replies, inbound message receiving, and platforms beyond Teams/Slack/Discord.
