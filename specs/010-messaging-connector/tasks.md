# Tasks: Multi-Platform Messaging Connector

**Feature**: 010-messaging-connector · **Branch**: `feature/messaging-connector`
**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)

## Format: `[ID] [P?] [Story] Description`

- **[P]** = parallelizable (different files, no dependency on an incomplete task).
- **[USn]** = the user story the task serves (story phases only).
- TDD per Constitution Article V: each story's tests are written first and must fail (Red) before
  implementation (Green). Run tests with the user-local SDK:
  `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test …`; E2E via `./scripts/run-e2e.ps1`.

## Path Conventions

Multi-project .NET solution: `src/DBAIAzure.Core`, `src/DBAIAzure.Connectors`, `src/DBAIAzure.Web`,
`src/DBAIAzure.Storage`, `tests/DBAIAzure.Tests`, `tests/DBAIAzure.E2ETests`.

---

## Phase 1: Setup (Shared Infrastructure)

- [ ] T001 Create folders `src/DBAIAzure.Connectors/Messaging/` and `tests/DBAIAzure.Tests/Messaging/`, and confirm a baseline green build/test run with the pinned SDK before changes begin.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Model rename + shared types every story depends on. No story can start until these are done.

- [ ] T002 Rename `ConnectorType.Teams` → `ConnectorType.Messaging` in `src/DBAIAzure.Core/Models/ConnectorType.cs` and update every reference across the solution (mechanical compile-driven sweep).
- [ ] T003 [P] Create `MessagingPlatform` enum (`Teams`, `Slack`, `Discord`) in `src/DBAIAzure.Core/Models/MessagingPlatform.cs` with XML docs noting each platform's webhook body/success signal.
- [ ] T004 [P] Create non-secret record `MessagingConnectorConfig` (Platform, McpServerUrl, McpToolName, McpArgumentTemplate, Target) in `src/DBAIAzure.Core/Models/MessagingConnectorConfig.cs`.
- [ ] T005 [P] Create `IMessageDelivery` interface plus `MessageDeliveryResult` record and `DeliveryPath` enum (Mcp/Webhook/NotConfigured) in `src/DBAIAzure.Core/Interfaces/IMessageDelivery.cs` per `contracts/imessage-delivery.md`.
- [ ] T006 Add read-time legacy mapping so a persisted `"Teams"` connector string resolves to `ConnectorType.Messaging` (default platform Teams) in `src/DBAIAzure.Storage/Repositories/SqliteConnectorConfigRepository.cs` (FR-013).
- [ ] T007 [P] Unit tests for `MessagingConnectorConfig` (de)serialization and the legacy `"Teams"` read-map in `tests/DBAIAzure.Tests/Messaging/MessagingConfigSerializationTests.cs` (Red→Green).

**Checkpoint**: Solution compiles with `ConnectorType.Messaging`; new shared types available.

---

## Phase 3: User Story 1 — Link a platform and verify via Test Connection (Priority: P1) 🎯 MVP

**Goal**: An operator picks a platform, enters a webhook URL, saves, and gets a clear pass/fail
Test Connection result naming the platform and path.

**Independent Test**: Configure each of Teams/Slack/Discord with a webhook URL only; Check Health
returns success naming the platform + "webhook" path and a labelled test message arrives; invalid
URL returns an actionable failure.

### Tests for User Story 1 (write first — Red)

- [ ] T008 [P] [US1] Unit tests for per-platform webhook profiles — body shape + success predicate (Teams `"1"`, Slack `"ok"`, Discord `204`), incl. JSON-escaping of messages — in `tests/DBAIAzure.Tests/Messaging/PlatformWebhookProfileTests.cs`.
- [ ] T009 [P] [US1] Unit tests for `MessageDelivery` selection: webhook path when only a webhook is stored (C2); `NotConfigured` when neither MCP nor webhook present (C3); failures returned not thrown (FR-010) — in `tests/DBAIAzure.Tests/Messaging/MessageDeliverySelectionTests.cs` (mocked `HttpMessageHandler` + fake repo).

### Implementation for User Story 1 (Green)

- [ ] T010 [P] [US1] `IPlatformWebhookProfile` interface (Platform, BuildBody, IsSuccess) in `src/DBAIAzure.Connectors/Messaging/IPlatformWebhookProfile.cs` per `contracts/mcp-gateway-and-webhook.md`.
- [ ] T011 [P] [US1] `TeamsWebhookProfile` reusing the existing Adaptive Card builder + `"1"` success check in `src/DBAIAzure.Connectors/Messaging/TeamsWebhookProfile.cs`.
- [ ] T012 [P] [US1] `SlackWebhookProfile` (`{"text":…}` body, `"ok"` success) in `src/DBAIAzure.Connectors/Messaging/SlackWebhookProfile.cs`.
- [ ] T013 [P] [US1] `DiscordWebhookProfile` (`{"content":…}` body, HTTP 204 success) in `src/DBAIAzure.Connectors/Messaging/DiscordWebhookProfile.cs`.
- [ ] T014 [US1] `MessageDelivery : IMessageDelivery` — resolve config + decrypted secrets from `IConnectorConfigRepository`, select path (webhook now; MCP returns `NotConfigured` until US3), POST via the matching webhook profile, return `MessageDeliveryResult` naming platform + path — in `src/DBAIAzure.Connectors/Messaging/MessageDelivery.cs`.
- [ ] T015 [US1] Rename `TeamsConnectorTester` → `MessagingConnectorTester`; delegate `TestConnectionAsync` to `IMessageDelivery.TestConnectionAsync` in `src/DBAIAzure.Connectors/MessagingConnectorTester.cs`.
- [ ] T016 [US1] Map `ConnectorType.Messaging` → `MessagingConnectorTester.TestConnectionAsync` in `src/DBAIAzure.Connectors/ConnectorHealthChecker.cs`.
- [ ] T017 [US1] Rework the Messaging card in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`: rename to "Messaging", add Platform `<select>` (Teams/Slack/Discord), Target field, and Webhook URL secret field ("leave blank to keep"); serialize `MessagingConnectorConfig` + `{webhookUrl}` secret via `SerializeToJson`/`LoadDraftFromJson`.
- [ ] T018 [US1] Update display name Teams → Messaging in `src/DBAIAzure.Web/Shared/ConnectorStatusBadge.razor` (and any `ConnectorDisplayName`/modal label).
- [ ] T019 [US1] Register the three webhook profiles, `MessageDelivery` (as `IMessageDelivery`), and `MessagingConnectorTester` in `src/DBAIAzure.Web/Program.cs`.
- [ ] T020 [P] [US1] Playwright E2E: Messaging card renders, platform dropdown switches visible/required fields, Save round-trips non-secret fields, secret field never pre-populates — in `tests/DBAIAzure.E2ETests/Tests/ConnectorSettingsTests.cs`.

**Checkpoint**: Webhook-only link + Test Connection works for all three platforms (MVP shippable).

---

## Phase 4: User Story 2 — Be notified on the linked platform when the pipeline pauses (Priority: P1)

**Goal**: HITL pauses and notify-nodes deliver through the configured Messaging connector; delivery
failures never block a run.

**Independent Test**: Drive a run to AwaitingHuman with a configured connector → message with
questions + portal link arrives; with a broken connector → run still pauses, failure logged, no throw.

### Tests for User Story 2 (write first — Red)

- [ ] T021 [P] [US2] Unit test: `MessagingHitlNotifier.NotifyAsync` composes title + numbered questions + portal link and calls `IMessageDelivery.SendAsync`; a delivery failure is swallowed (non-blocking) — in `tests/DBAIAzure.Tests/Messaging/MessagingHitlNotifierTests.cs`.

### Implementation for User Story 2 (Green)

- [ ] T022 [US2] Rename `TeamsHitlNotifier` → `MessagingHitlNotifier` (implements `IHitlNotifier`), delegating formatting + send to `IMessageDelivery`, in `src/DBAIAzure.Web/Integrations/Messaging/MessagingHitlNotifier.cs` (remove the legacy named-HttpClient base-address path superseded by `MessageDelivery`).
- [ ] T023 [US2] Rename `TeamsConnectorAdapter` → `MessagingConnectorAdapter` (implements `IConnectorAdapter`, `ConnectorType.Messaging`), delegating `ExecuteAsync`/`HealthCheckAsync` to `IMessageDelivery`, in `src/DBAIAzure.Connectors/MessagingConnectorAdapter.cs`.
- [ ] T024 [US2] Update DI in `src/DBAIAzure.Web/Program.cs`: bind `IHitlNotifier` → `MessagingHitlNotifier`, register `MessagingConnectorAdapter`; remove obsolete `TeamsHitlNotifier` HttpClient wiring.
- [ ] T025 [US2] Confirm the HITL trigger in `src/DBAIAzure.Processes/Pipeline/PipelineOrchestrator.cs` and `WorkflowRealizationService.cs` compile/operate against `ConnectorType.Messaging` (reference sweep).

**Checkpoint**: HITL + notify-node sends flow through `IMessageDelivery` on any platform.

---

## Phase 5: User Story 3 — MCP-first delivery with webhook fallback (Priority: P2)

**Goal**: When an MCP server is configured, deliver via its send-message tool; otherwise use the
webhook. Selection is configuration-based with no silent runtime fallback.

**Independent Test**: With MCP url + webhook both set, sends use MCP (path == Mcp); clear the MCP url
(no other change) and sends use webhook (path == Webhook). MCP server down → failure on Mcp path,
non-throwing.

### Tests for User Story 3 (write first — Red)

- [ ] T026 [P] [US3] Unit tests for argument-template substitution: `{{target}}`/`{{message}}` replacement, JSON-escaping of special characters, and default template when blank — in `tests/DBAIAzure.Tests/Messaging/McpArgumentTemplateTests.cs`.
- [ ] T027 [P] [US3] Extend `MessageDeliverySelectionTests.cs`: MCP path chosen when `McpServerUrl` set (C1); MCP transport failure stays `DeliveryPath.Mcp` and returns a failure result without throwing and without webhook fallback (C4) — using a fake `IMcpMessageGateway`.

### Implementation for User Story 3 (Green)

- [ ] T028 [US3] Add `ModelContextProtocol.Core` (latest stable 1.x) PackageReference to `src/DBAIAzure.Connectors/DBAIAzure.Connectors.csproj`.
- [ ] T029 [P] [US3] `IMcpMessageGateway` + `McpSendRequest`/`McpSendResult` records in `src/DBAIAzure.Connectors/Messaging/IMcpMessageGateway.cs` per `contracts/mcp-gateway-and-webhook.md`.
- [ ] T030 [US3] `McpMessageGateway` impl: build args from template, connect via `SseClientTransport` (optional bearer header), `CallToolAsync`, map `IsError`/not-found/transport errors to `McpSendResult`, per-operation connect/dispose — in `src/DBAIAzure.Connectors/Messaging/McpMessageGateway.cs`.
- [ ] T031 [US3] Extend `MessageDelivery` to select and invoke the MCP path when `McpServerUrl` is configured (reporting `DeliveryPath.Mcp`); keep webhook as the fallback only when no MCP url is set — in `src/DBAIAzure.Connectors/Messaging/MessageDelivery.cs`.
- [ ] T032 [US3] Add MCP fields to the Messaging card in `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`: MCP Server URL, MCP Tool Name, MCP Argument Template (non-secret, placeholder shows default), and MCP Auth Token (secret); serialize into `MessagingConnectorConfig` + `{mcpAuthToken}` secret.
- [ ] T033 [US3] Register `IMcpMessageGateway` → `McpMessageGateway` in `src/DBAIAzure.Web/Program.cs`.
- [ ] T034 [P] [US3] Env-gated integration tests for live Slack + Discord webhook delivery and a live MCP tool send in `tests/DBAIAzure.Tests/Integration/ConnectorFunctionalTests.cs` (skip when env vars absent).

**Checkpoint**: MCP-first routing works end-to-end with webhook fallback (full feature).

---

## Phase 6: User Story 4 — Status at a glance & safe platform switching (Priority: P3)

**Goal**: The Messaging section clearly shows configured/healthy state and the platform, and changing
platforms does not leave stale config or a misleading prior health result.

**Independent Test**: A configured+tested connector shows Configured + Healthy/Unhealthy + platform +
last-test age; switching platform and saving persists only the new platform's config and clears the
prior test result.

### Tests for User Story 4 (write first — Red)

- [ ] T035 [P] [US4] Playwright: status badges show platform name + Configured/Healthy state; switching platform clears stale fields; the health-check message names platform + path — in `tests/DBAIAzure.E2ETests/Tests/ConnectorSettingsTests.cs`.

### Implementation for User Story 4 (Green)

- [ ] T036 [US4] In `src/DBAIAzure.Web/Pages/ConnectorSettings.razor`, on save clear the in-memory test result and persist only the selected platform's config (field-change invalidation, FR-012).
- [ ] T037 [US4] Ensure the result messages always include the platform name and delivery-path label (FR-009) in `src/DBAIAzure.Connectors/Messaging/MessageDelivery.cs` and `src/DBAIAzure.Connectors/MessagingConnectorTester.cs`.
- [ ] T038 [US4] Show the platform name in the Messaging status badge/header in `src/DBAIAzure.Web/Shared/ConnectorStatusBadge.razor` and the card header.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T039 [P] Update `CHANGELOG.md` (Unreleased): Teams connector generalized to Messaging; Slack + Discord added; MCP-first delivery with webhook fallback.
- [ ] T040 [P] Add/verify XML doc comments on all new public types/members; confirm nullable honored with no `!` suppression and methods stay under ~40 lines (Article IV).
- [ ] T041 Run full unit suite (`dotnet test`) and `./scripts/run-e2e.ps1`; confirm green; commit per phase with conventional `type: description` messages.
- [ ] T042 [P] Remove obsolete Teams-only artifacts (`src/DBAIAzure.Web/Integrations/Teams/`, duplicate `TeamsSecrets`) once all references are migrated, and delete the now-empty Teams namespaces.

---

## Dependencies & Execution Order

```
Setup (T001)
  └─ Foundational (T002 → T003|T004|T005 [P] → T006 → T007)
       └─ US1 / MVP (T008|T009 [P] → T010-T013 [P] → T014 → T015 → T016 → T017 → T018 → T019 → T020)
            ├─ US2 (T021 → T022 → T023 → T024 → T025)
            ├─ US3 (T026|T027 [P] → T028 → T029 → T030 → T031 → T032 → T033 → T034)
            └─ US4 (T035 → T036 → T037 → T038)
                 └─ Polish (T039-T042)
```

- **US2, US3, US4 all depend on US1** (they consume `IMessageDelivery` + the Messaging card built in US1).
- US2 and US3 are independent of each other; US4 builds on the US1 UI.
- **MVP = Phase 1 + 2 + US1.** Phase A in the plan = US1 (+ US2 wiring); Phase B = US3.

## Parallel Opportunities

- Foundational: **T003, T004, T005** in parallel (separate new files), then T006/T007.
- US1: tests **T008, T009** in parallel; webhook profiles **T010–T013** in parallel before T014.
- US3: tests **T026, T027** in parallel; **T029** alongside test authoring.
- Polish: **T039, T040, T042** in parallel.

## Independent Test Criteria (per story)

| Story | Independently shippable proof |
|-------|-------------------------------|
| US1 (P1) | Link each platform via webhook and get a path-named pass/fail Test Connection result. |
| US2 (P1) | A paused run delivers a question+link message on the linked platform; broken connector never blocks the run. |
| US3 (P2) | Same connector delivers via MCP when a server is set, via webhook when it is cleared. |
| US4 (P3) | Status badges and platform-switch behavior are correct; no stale health result after save. |
