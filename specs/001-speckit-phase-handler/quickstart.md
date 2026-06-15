# Quickstart & Validation: Spec Kit Phase Handler

**Feature**: `specs/001-speckit-phase-handler` · **Date**: 2026-06-15

How to run and prove the feature end-to-end. Details of types and endpoints live in
[data-model.md](./data-model.md) and [contracts/](./contracts/) — not duplicated here.

## Prerequisites

- .NET 8 SDK (repo `global.json` resolves the user-local SDK; `dotnet --version` → `8.0.x`).
- `appsettings.Development.json` (gitignored) with:
  - `Anthropic:ApiKey`
  - `AzureDevOps:OrganizationUrl` (e.g. `https://dev.azure.com/your-org`), `AzureDevOps:Project`,
    `AzureDevOps:Pat` (PAT with **Work Items (Read & Write)** scope)
  - `WebhookSecrets:SpecKit` (shared secret for the inbound endpoints)
- An Azure DevOps project on the **Agile** process (Epic/Task/Bug types).
- A feature artifact directory to validate, e.g. this repo's own `specs/001-speckit-phase-handler/`.

## Build & test

```bash
dotnet build DBAIAzure.sln
dotnet test  DBAIAzure.sln          # unit + integration layers
```

## Run the web host

```bash
dotnet run --project src/DBAIAzure.Web/DBAIAzure.Web.csproj
# listens on http://localhost:5000 (Portal:BaseUrl)
```

## Scenario A — Specify phase → Epic (P1, the MVP loop)

1. **Send the phase-complete signal:**
   ```bash
   curl -X POST http://localhost:5000/api/webhook/speckit-phase \
     -H "X-SpecKit-Secret: <secret>" -H "Content-Type: application/json" \
     -d '{ "feature_key": "001-speckit-phase-handler", "phase": "specify" }'
   # → 202 { "runId": "ab12cd34", ... }
   ```
2. **Observe** the run on the portal (`/run/ab12cd34`): artifacts read → validation summary + flagged
   gaps shown → status `AwaitingApproval`. **Confirm nothing is on the board yet** (proves FR-006).
3. **Approve** via the decision-card callback:
   ```bash
   curl -X POST http://localhost:5000/api/webhook/speckit-approval \
     -H "X-SpecKit-Secret: <secret>" -H "Content-Type: application/json" \
     -d '{ "run_id": "ab12cd34", "approved": true, "decided_by": "tester" }'
   # → 200 { "status": "WritingBoard" }
   ```
4. **Verify** an **Epic** now exists on the board, titled/described from the artifacts, within ~30s
   (SC-006). The run is `Completed` with the work item id/url recorded.

**Reject path:** repeat steps 1–2, then POST `approved: false` → confirm **no** work item is created
and the run is `Rejected` (FR-010).

## Scenario B — Plan → Tasks, Implement → Bug (P2)

- POST `phase: "plan"` for a feature with a plan/tasks artifact, approve → confirm **one Task per
  planned unit**, each **linked under the feature's Epic** (FR-008, FR-012). If the Epic does not yet
  exist, confirm it is auto-created first (no orphans).
- POST `phase: "implement"`, approve → confirm a **Bug** (completion record) linked under the Epic.

## Scenario C — Non-destructive upsert (P3, FR-013/FR-018)

- Re-send an already-approved `specify` signal and approve again. Confirm: **no duplicate Epic**, the
  fields are refreshed, the new validation summary is **appended as a new Discussion comment**, and the
  prior comment + field history remain (open the work item History tab).

## Edge cases to exercise

| Action | Expected |
|---|---|
| `phase: "analyze"` (unsupported) | Run `Unsupported`, no work item (FR-014) |
| Missing/empty feature directory | Run `Failed` with reason; no work item (FR-003) |
| Bad/missing `X-SpecKit-Secret` | `401`; no run started (FR-002) |
| Approval for unknown `run_id` | `404` |
| Board unreachable after approval | Run `Failed` with reason; approval not lost (FR-015) |

## Regression guard

```bash
dotnet test DBAIAzure.sln --filter "FullyQualifiedName~Pipeline|FullyQualifiedName~ServiceNow"
```
The existing ticket pipeline tests MUST stay green (FR-017 / SC-007).
