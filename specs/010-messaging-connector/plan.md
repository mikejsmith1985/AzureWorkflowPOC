# Implementation Plan: Multi-Platform Messaging Connector

**Branch**: `feature/messaging-connector` | **Date**: 2026-06-25 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/010-messaging-connector/spec.md`

## Summary

Generalize the single-purpose Teams connector into a **Messaging** connector that targets Microsoft
Teams, Slack, or Discord. Delivery is **MCP-first**: when an MCP server endpoint is configured, the
app (as an MCP client over remote HTTP/SSE) invokes a send-message tool whose arguments are built
from an operator-supplied JSON template; otherwise it falls back to the platform's direct
incoming-webhook with the platform-correct payload and success signal. A single `IMessageDelivery`
seam backs all three existing behaviors — HITL notifications, notify-node sends, and the Settings
Test Connection/health check — so callers never branch on platform or path. See
[research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/),
[quickstart.md](./quickstart.md).

## Technical Context

**Language/Version**: C# 12 / .NET 8 (pinned via `global.json`, SDK 8.0.422)

**Primary Dependencies**: Semantic Kernel 1.77 (existing); **`ModelContextProtocol.Core` 1.x** (new —
MCP client over `SseClientTransport`); ASP.NET Core Blazor Server (existing); `System.Text.Json`;
ASP.NET Core Data Protection (existing, for secrets)

**Storage**: Existing SQLite `ConnectorConfigs` table via `IConnectorConfigRepository` — **no schema
change, no migration** (R5). Non-secret config + encrypted secrets blob reused.

**Testing**: xUnit unit tests (mocked `HttpMessageHandler` + fake MCP gateway/client); env-gated
integration tests for live MCP/webhook; Playwright E2E for the Messaging settings card (`run-e2e.ps1`)

**Target Platform**: Windows/Linux server (Kestrel) — long-running Blazor Server app

**Project Type**: Web application (.NET multi-project solution)

**Performance Goals**: Not latency-sensitive; sends are infrequent (HITL pauses, notify nodes, manual
tests). Per-operation MCP connect/dispose acceptable (R3). Test/send timeout ≈ 30 s (matches existing
connectors).

**Constraints**: Secrets never in UI/logs/non-secret fields (Article IX); delivery failures
non-blocking for pipeline runs (FR-010); config changes hot-reload without restart (FR-015); remote
MCP transport only — no subprocess spawning (clarification).

**Scale/Scope**: One configured messaging connector at a time; 3 platforms in v1; adding a platform =
enum member + webhook profile.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Article | Gate | Status |
|---------|------|--------|
| I — Prime Directive (best route) | Reuse existing connector/secret/health machinery; official MCP SDK; one clean delivery seam | ✅ PASS |
| III — Branching | Work on `feature/messaging-connector`, PR to merge | ✅ PASS |
| IV — Code Quality | PascalCase/camelCase/`_camel`; predicate booleans; `Async`+`CancellationToken`; nullable honored; XML docs; <40-line methods; guard clauses | ✅ PASS (enforced in impl) |
| V — Testing (3-layer) | Unit (mocked handler/gateway), integration (env-gated live), E2E (Playwright card). Red→Green | ✅ PASS |
| VI — Documentation | CHANGELOG.md updated in the PR | ✅ PASS |
| VII — **Framework-First** | MCP client via official `ModelContextProtocol` SDK (no hand-rolled JSON-RPC); HITL via existing `IHitlNotifier`; structured config via existing repository; webhook via plain HttpClient (no framework primitive exists for a 3-line POST) | ✅ PASS — see justification below |
| IX — Secrets | MCP auth token + webhook URL encrypted via existing `ISecretProtector`; "leave blank to keep"; never logged | ✅ PASS |
| X — Verification | Quickstart scenarios + tests provide behavioral evidence (not just 200/compiles) | ✅ PASS |
| XI — Output restraint | No scratch dashboards; spec-tree artifacts only | ✅ PASS |

**Article VII justification (recorded at the custom component)**: The MCP client is built on the
official `ModelContextProtocol.Core` SDK — we do **not** hand-roll JSON-RPC/SSE. The only custom code
is (a) the JSON argument-template substitution (a documented gap — no framework maps `{{placeholders}}`
to tool args) and (b) the per-platform webhook payload builders (each platform's body/success signal
is its own contract; no SK primitive covers a raw incoming-webhook POST). Both are minimal and
unit-tested. Semantic Kernel's `AsKernelFunction()` MCP bridge was evaluated and rejected for the send
path (R2) because delivery is a deterministic RPC that must not depend on an LLM.

**No violations** → Complexity Tracking section omitted.

## Project Structure

### Documentation (this feature)

```text
specs/010-messaging-connector/
├── plan.md              # This file
├── spec.md              # Feature spec (clarified)
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/           # Phase 1 (imessage-delivery, mcp-gateway-and-webhook, ui-messaging-section)
└── tasks.md             # Phase 2 (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── DBAIAzure.Core/
│   ├── Models/
│   │   ├── ConnectorType.cs                 # Teams → Messaging (rename)
│   │   ├── MessagingPlatform.cs             # NEW enum (Teams/Slack/Discord)
│   │   └── MessagingConnectorConfig.cs      # NEW non-secret record
│   └── Interfaces/
│       └── IMessageDelivery.cs              # NEW delivery seam (+ MessageDeliveryResult, DeliveryPath)
├── DBAIAzure.Connectors/
│   ├── Messaging/
│   │   ├── MessageDelivery.cs               # NEW — MCP-first/webhook selection (impl IMessageDelivery)
│   │   ├── IMcpMessageGateway.cs            # NEW — MCP client wrapper interface
│   │   ├── McpMessageGateway.cs             # NEW — ModelContextProtocol.Core impl + template substitution
│   │   ├── IPlatformWebhookProfile.cs       # NEW — per-platform body/success contract
│   │   ├── TeamsWebhookProfile.cs           # NEW (reuses existing Adaptive Card builder + "1")
│   │   ├── SlackWebhookProfile.cs           # NEW ({text} / "ok")
│   │   └── DiscordWebhookProfile.cs         # NEW ({content} / 204)
│   ├── MessagingConnectorTester.cs          # RENAME of TeamsConnectorTester → delegates to IMessageDelivery
│   ├── MessagingConnectorAdapter.cs         # RENAME of TeamsConnectorAdapter → delegates to IMessageDelivery
│   └── ConnectorHealthChecker.cs            # Map ConnectorType.Messaging → tester
├── DBAIAzure.Web/
│   ├── Integrations/Messaging/
│   │   └── MessagingHitlNotifier.cs         # RENAME of TeamsHitlNotifier (IHitlNotifier) → IMessageDelivery
│   ├── Pages/ConnectorSettings.razor        # Messaging card: platform dropdown + MCP/webhook fields
│   ├── Shared/ConnectorStatusBadge.razor    # Display name Teams → Messaging
│   └── Program.cs                           # DI: register gateway, profiles, delivery, tester, notifier
└── DBAIAzure.Storage/
    └── Repositories/SqliteConnectorConfigRepository.cs  # read-time legacy "Teams"→Messaging map (R5)

tests/
├── DBAIAzure.Tests/
│   ├── Messaging/
│   │   ├── McpArgumentTemplateTests.cs      # {{target}}/{{message}} substitution + JSON-escaping
│   │   ├── PlatformWebhookProfileTests.cs   # body + success predicate per platform
│   │   └── MessageDeliverySelectionTests.cs # MCP-first vs webhook vs not-configured (C1–C6)
│   └── Integration/ConnectorFunctionalTests.cs  # add env-gated Slack/Discord/MCP live checks
└── DBAIAzure.E2ETests/Tests/ConnectorSettingsTests.cs   # Messaging card + platform dropdown
```

**Structure Decision**: Existing multi-project layout is kept. New messaging code is grouped under a
`Messaging/` folder in `DBAIAzure.Connectors` (delivery strategies live with the other connector
clients); the HITL notifier stays in `DBAIAzure.Web/Integrations` (web-layer concern). The rename of
the three Teams-named classes is mechanical; behavior moves behind `IMessageDelivery`.

## Phasing (delivery order — each independently shippable)

1. **Phase A — Multi-platform via webhook** (no MCP yet): rename model + classes; add
   `MessagingPlatform`, `MessagingConnectorConfig`, `IMessageDelivery` with webhook profiles for all
   three platforms; UI platform dropdown; tests. Ships working Teams/Slack/Discord webhook delivery.
2. **Phase B — MCP-first**: add `ModelContextProtocol.Core`, `IMcpMessageGateway` + impl, argument
   template, MCP-path selection, MCP auth-token + server/tool/target/template UI fields; tests.

(`/speckit-tasks` will expand both phases into dependency-ordered tasks.)

## Complexity Tracking

No constitution violations — section intentionally empty.
