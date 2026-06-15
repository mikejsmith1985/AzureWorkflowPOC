# HTTP Contracts: Spec Kit Phase Handler

**Feature**: `specs/001-speckit-phase-handler`

Two inbound endpoints on `SpecKitWebhookController` (route base `api/webhook`). Both require a shared
secret header (mirrors the existing ServiceNow webhook `X-SNow-Secret` pattern). Secret is resolved
from configuration (`WebhookSecrets:SpecKit`); never logged.

---

## 1. Phase-complete signal

`POST /api/webhook/speckit-phase`

**Headers**
| Header | Required | Notes |
|---|---|---|
| `X-SpecKit-Secret` | yes | Shared secret; mismatch → `401` |
| `Content-Type: application/json` | yes | |

**Request body**
```json
{
  "feature_key": "001-speckit-phase-handler",
  "feature_directory": "specs/001-speckit-phase-handler",
  "phase": "specify"
}
```
| Field | Type | Required | Notes |
|---|---|---|---|
| `feature_key` | string | yes | Stable feature slug; idempotency key part 1 |
| `feature_directory` | string | no | Repo-relative dir; if omitted, derived from `feature_key` under `specs/` |
| `phase` | string | yes | One of `specify` \| `plan` \| `implement` (case-insensitive); others accepted but recorded `Unsupported` |

**Responses**
| Status | When | Body |
|---|---|---|
| `202 Accepted` | Signal accepted; run started | `{ "runId": "ab12cd34", "featureKey": "...", "phase": "specify" }` |
| `400 Bad Request` | Missing `feature_key` or `phase` | `{ "error": "feature_key is required" }` |
| `401 Unauthorized` | Bad/missing secret | (empty) |

`202` is returned immediately; validation + approval happen asynchronously (same fire-and-return shape
as the ServiceNow intake).

---

## 2. Approval decision callback

`POST /api/webhook/speckit-approval`

Delivered by the Forge Terminal HITL decision card. Resumes the paused run via the approval external
channel (mirrors the existing `HumanResponded` resume).

**Headers**: same `X-SpecKit-Secret` requirement.

**Request body**
```json
{
  "run_id": "ab12cd34",
  "approved": true,
  "decided_by": "j.smith",
  "note": "Looks good; proceed."
}
```
| Field | Type | Required | Notes |
|---|---|---|---|
| `run_id` | string | yes | The `runId` returned by endpoint 1 |
| `approved` | bool | yes | `true` = create/upsert work item; `false` = reject (no write) |
| `decided_by` | string | no | Reviewer identity, recorded for audit |
| `note` | string | no | Optional note, appended to the work item discussion comment |

**Responses**
| Status | When | Body |
|---|---|---|
| `200 OK` | Decision accepted and applied to the waiting run | `{ "runId": "...", "status": "accepted" \| "rejected" }` (stable external status, not the internal `PhaseRunStatus` name) |
| `400 Bad Request` | Missing `run_id`/`approved` | `{ "error": "run_id is required" }` |
| `401 Unauthorized` | Bad/missing secret | (empty) |
| `404 Not Found` | No run with `run_id`, or run not awaiting approval | `{ "error": "no run awaiting approval for run_id" }` |
| `409 Conflict` | Run already decided | `{ "error": "run already decided" }` |

---

## Outbound: Azure DevOps Boards
The handler calls Azure DevOps via the official client behind `IBoardsClient` (see
[iboards-client.md](./iboards-client.md)); it does not expose this as an HTTP contract. Configuration
keys: `AzureDevOps:OrganizationUrl`, `AzureDevOps:Project`, `AzureDevOps:Pat` (secret),
optional `AzureDevOps:AreaPath` / `AzureDevOps:IterationPath`.

## Outbound: decision-card notification
When a run pauses for approval, the handler POSTs to the Forge Terminal decision-card endpoint via
`IPhaseApprovalNotifier` (config `SpecKit:DecisionCardUrl`). Fire-and-forget and failure-tolerant
(mirrors `TeamsHitlNotifier`). Payload:
```json
{
  "run_id": "ab12cd34",
  "feature_key": "001-speckit-phase-handler",
  "phase": "specify",
  "summary": "<one-paragraph validation summary>",
  "gaps": [ { "label": "...", "description": "..." } ],
  "portal_url": "http://localhost:5000/run/ab12cd34"
}
```
The reviewer approves/rejects from the card, which calls back `POST /api/webhook/speckit-approval`.
