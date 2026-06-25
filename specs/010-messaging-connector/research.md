# Phase 0 Research: Multi-Platform Messaging Connector

**Feature**: 010-messaging-connector · **Date**: 2026-06-25

This document resolves the technical unknowns in the plan's Technical Context. Each section records
the **Decision**, **Rationale**, and **Alternatives considered**.

---

## R1 — MCP client library for .NET 8 (HTTP/SSE transport)

**Decision**: Use the official **`ModelContextProtocol`** C# SDK (maintained in collaboration with
Microsoft). Reference the **`ModelContextProtocol.Core`** package (client + low-level APIs, no server
hosting) pinned to the latest stable **1.x** line (1.4.0 at time of writing). Connect with
`SseClientTransport` (streamable HTTP/SSE) via `McpClientFactory.CreateAsync(...)`, then call the
configured tool with `IMcpClient.CallToolAsync(toolName, argumentsDictionary, ct)`.

**Rationale**:
- It is the canonical, Microsoft-collaborated SDK — satisfies the Framework-First gate (Article VII):
  we consume the protocol's own SDK rather than hand-rolling JSON-RPC framing or SSE plumbing.
- `.Core` is the smallest surface that provides a client; the app is an MCP **client**, not a server,
  so the hosting/DI and AspNetCore packages are unnecessary.
- `SseClientTransport` accepts a remote endpoint URI and optional headers — exactly the
  remote-HTTP/SSE transport the spec mandates (no local subprocess management).
- `CallToolAsync` returns structured content plus an `IsError` flag, giving a clean success/failure
  signal for FR-007.

**Alternatives considered**:
- *Hand-rolled JSON-RPC over HttpClient*: rejected — re-implements the protocol the SDK already
  provides; violates Article VII.
- *`ModelContextProtocol` (main package) with full DI/hosting*: rejected for now — pulls hosting
  extensions we do not need for a pure client; `.Core` keeps the dependency footprint minimal. (Can
  be revisited if we later want `IMcpClient` registered through the SDK's DI helpers.)
- *Stdio transport*: rejected — out of scope per clarification; the web app must not spawn
  subprocesses per send.

---

## R2 — MCP tool invocation vs. Semantic Kernel plugin mapping

**Decision**: Invoke the send-message tool **directly** via `IMcpClient.CallToolAsync`. Do **not**
route the send through Semantic Kernel / an LLM. Argument values are produced by substituting
`{{target}}` and `{{message}}` into the operator-supplied **JSON argument template** (per the
2026-06-25 clarification), then parsed into the argument dictionary `CallToolAsync` expects.

**Rationale**:
- Delivery is deterministic: we know the tool and the arguments. Putting an LLM in the send path
  would add cost, latency, and nondeterminism for zero benefit.
- The SDK's `AsKernelFunction()` bridge exists for the *LLM-orchestrated* case; our case is a fixed
  RPC, so the direct client call is the simpler, correct primitive.
- The JSON template keeps us server-agnostic: any tool's argument names are expressible without code
  changes (supports FR-014 extensibility).

**Alternatives considered**:
- *Register MCP tools as `KernelFunction`s and let the kernel call them*: rejected for the send path
  (unnecessary LLM dependency). The mapping remains available if a future feature wants the model to
  choose among messaging tools.
- *Fixed argument names per platform*: rejected in clarify — brittle across MCP servers.

---

## R3 — MCP connection lifecycle in a long-running Blazor Server app

**Decision**: Open an MCP client connection **per delivery operation** (connect → call tool →
dispose), with a per-operation timeout. Do not hold a long-lived pooled connection in v1.

**Rationale**:
- Sends are infrequent (HITL pauses, notify nodes, manual tests) — connection setup cost is
  negligible relative to send frequency, and per-op connections avoid stale-session and
  reconnect-handling complexity.
- Mirrors the existing connectors' "resolve credentials and call at each use" hot-reload pattern
  (FR-015), so a configuration change takes effect on the next send with no restart.

**Alternatives considered**:
- *Cached/pooled `IMcpClient` per server URL*: deferred — a performance optimization with real
  lifecycle complexity (idle timeouts, server restarts, auth-token refresh) not justified at this
  volume. Revisit if send volume grows.

---

## R4 — Per-platform webhook payload formats and success signals (fallback path)

**Decision**: Implement one payload builder + success predicate per platform, selected by the
configured `MessagingPlatform`:

| Platform | Request body shape | Success signal |
|----------|--------------------|----------------|
| Microsoft Teams | Adaptive Card message (`attachments[].content` AdaptiveCard) | response body trimmed == `"1"` |
| Slack | `{ "text": "<message>" }` (Block Kit optional later) | response body trimmed == `"ok"` |
| Discord | `{ "content": "<message>" }` | HTTP `204 No Content` |

**Rationale**: These are the documented contracts for each platform's incoming webhook. Teams' "1"
and Slack's "ok" string responses, and Discord's 204, are each distinct, so a generic "HTTP 2xx"
check would produce false passes (the current Teams tester already verifies `"1"` specifically). The
existing Teams Adaptive Card builder and `"1"` check are preserved verbatim as the Teams branch.

**Alternatives considered**:
- *Generic "any 2xx is success"*: rejected — masks malformed-URL and wrong-platform cases that the
  per-platform signals catch (an edge case in the spec).
- *Slack Block Kit by default*: deferred — plain `text` covers v1 notifications; Block Kit can be a
  later template enhancement without architectural change.

---

## R5 — Connector model rename and storage compatibility

**Decision**: Rename `ConnectorType.Teams` → `ConnectorType.Messaging`. On the storage read path,
map any legacy persisted string `"Teams"` to `ConnectorType.Messaging` (platform defaulting to
Microsoft Teams). No data migration script is required (no Teams row exists today — verified by
inspecting `pipeline.db`).

**Rationale**: A single enum slot keeps the four-connector model intact and reuses the entire
existing settings/secret/health-check machinery. The defensive legacy mapping (FR-013) costs one
small branch and prevents a hard failure if a `"Teams"` row ever appears (e.g., a developer's older DB).

**Alternatives considered**:
- *Add `Messaging` as a 5th type and keep `Teams`*: rejected — leaves a dead connector slot and two
  overlapping concepts in the UI.
- *EF migration to rewrite rows*: unnecessary — there is no data to migrate; the read-time map is
  sufficient and simpler.

---

## R6 — Secret storage for MCP auth token + webhook URL

**Decision**: Store both the MCP auth token and the webhook URL in the existing encrypted-secrets
blob for the Messaging connector (`{ mcpAuthToken?, webhookUrl? }`), reusing `ISecretProtector` and
the "leave blank to keep existing" save semantics already used by ServiceNow/ADO/LLM.

**Rationale**: Satisfies Article IX with zero new crypto surface; both values are credentials/bearer
material that must never reach the UI or logs. Non-secret routing data (platform, server URL, tool
name, argument template, target) lives in `NonSecretConfig`.

**Alternatives considered**:
- *Treat the webhook URL as non-secret* (it's "just a URL"): rejected — an incoming-webhook URL **is**
  a bearer credential (anyone with it can post to the channel); it must be encrypted and never echoed.

---

## Resolved unknowns summary

| Technical Context item | Resolution |
|------------------------|------------|
| MCP client library | `ModelContextProtocol.Core` 1.x, `SseClientTransport` + `CallToolAsync` (R1) |
| MCP invocation style | Direct tool call, no LLM; JSON argument template (R2) |
| Connection lifecycle | Per-operation connect/dispose with timeout (R3) |
| Webhook fallback contracts | Per-platform builder + success predicate (R4) |
| Model rename / compat | `Teams`→`Messaging`, read-time legacy map, no migration (R5) |
| Secret storage | Existing encrypted blob, reuse `ISecretProtector` (R6) |

No `NEEDS CLARIFICATION` markers remain.
