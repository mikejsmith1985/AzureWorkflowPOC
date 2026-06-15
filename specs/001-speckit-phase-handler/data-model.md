# Phase 1 Data Model: Spec Kit Phase Handler

**Feature**: `specs/001-speckit-phase-handler` · **Date**: 2026-06-15

All domain types are **immutable records** with `required` members, mirroring the existing
`TicketState` / `DorVerdict` conventions (snake_case `[JsonPropertyName]` where they cross the wire).
State flows through SK steps via `with` expressions — no in-place mutation.

## Enumerations

### SpecKitPhase
The completed phase named by the inbound signal, and the driver of the work-item mapping.

| Member | Maps to work item type | Notes |
|---|---|---|
| `Specify` | `Epic` | One Epic per feature |
| `Plan` | `Task` (one per planned unit) | Children of the feature's Epic |
| `Implement` | `Bug` | Completion / defect record, child of the Epic |
| `Unsupported` | (none) | Any other phase value; recorded, no work item created (FR-014) |

> Process confirmed **Agile** (Epic/Task/Bug all valid). The mapping is centralized in
> `PhaseWorkItemMap` so a different process template can be accommodated without touching steps.

## Process State

### PhaseHandlerState
The single state object that flows through the phase-handler SK process. (Analogous to `TicketState`
in the ticket pipeline.)

| Field | Type | Purpose |
|---|---|---|
| `RunId` | `string` | Correlates the run across events, persistence, and the approval callback |
| `FeatureDirectory` | `string` | Repo-relative `specs/NNN-feature-name/` path resolved from the signal |
| `FeatureKey` | `string` | Stable feature identifier (the `NNN-feature-name` slug) — idempotency key part 1 |
| `Phase` | `SpecKitPhase` | Completed phase — idempotency key part 2 |
| `Artifacts` | `IReadOnlyList<PhaseArtifact>` | Files read for this phase (name + text content) |
| `Validation` | `PhaseValidationResult?` | Structured summary + flagged gaps (null until validated) |
| `Decision` | `ApprovalDecision?` | Human approve/reject (null until the card responds) |
| `PlannedItems` | `IReadOnlyList<PlannedWorkItem>` | For the Plan phase: the parsed units of work |
| `CreatedWorkItems` | `IReadOnlyList<CreatedWorkItemRef>` | Ids/urls created or updated on the board |
| `Status` | `PhaseRunStatus` | Lifecycle (see transitions below) |
| `FailureReason` | `string?` | Populated on a recorded failure (missing artifacts, board write, etc.) |

### PhaseArtifact
| Field | Type | Purpose |
|---|---|---|
| `FileName` | `string` | e.g. `spec.md`, `plan.md`, `tasks.md` |
| `Content` | `string` | UTF-8 file text (bounded; see Constraints) |

### PhaseValidationResult  *(structured LLM output — bound from the tool_use `input`)*
| Field | JSON name | Type | Purpose |
|---|---|---|---|
| `Summary` | `summary` | `string` | One-paragraph plain-language summary of the phase artifacts |
| `Gaps` | `gaps` | `IReadOnlyList<PhaseValidationGap>` | Flagged gaps/risks/omissions (empty if none) |

### PhaseValidationGap
| Field | JSON name | Type | Purpose |
|---|---|---|---|
| `Label` | `label` | `string` | Short gap label |
| `Description` | `description` | `string` | What is missing and why it matters |

### PlannedWorkItem  *(Plan phase only)*
| Field | Type | Purpose |
|---|---|---|
| `Title` | `string` | Title for the child Task |
| `Description` | `string` | Body derived from the planned unit of work |

### ApprovalDecision
| Field | Type | Purpose |
|---|---|---|
| `IsApproved` | `bool` | True = approved (proceed to board write); false = rejected |
| `DecidedBy` | `string?` | Reviewer identity from the decision card, if provided |
| `Note` | `string?` | Optional reviewer note, appended to the board comment for traceability |

### CreatedWorkItemRef
| Field | Type | Purpose |
|---|---|---|
| `WorkItemId` | `int` | Azure DevOps work item id |
| `WorkItemType` | `string` | `Epic` / `Task` / `Bug` |
| `Url` | `string` | Browser/API url of the work item |
| `WasUpdated` | `bool` | False = newly created; true = upserted (existing item refreshed) |

## State Transitions (PhaseRunStatus)

```
Received ──▶ ArtifactsRead ──▶ Validated ──▶ AwaitingApproval
                                                  │
                          ┌───────────────────────┼───────────────────────┐
                          ▼ (approved)             ▼ (rejected)            ▼ (no answer)
                     WritingBoard              Rejected            AwaitingApproval (stays)
                          │
              ┌───────────┴───────────┐
              ▼ (ok)                   ▼ (write fails)
          Completed                 Failed

Any read/validate error ──▶ Failed (FailureReason set)
Unsupported phase ──▶ Unsupported (terminal, no work item)
Duplicate signal (already Completed) ──▶ upsert path ──▶ Completed (no new item)
```

- **No board write occurs before `Status == WritingBoard`**, which is only reachable through an
  approved `ApprovalDecision` (enforces FR-006).
- `Rejected`, `Unsupported`, and `Failed` are terminal and create no work item.
- A repeat signal for a `Completed` (feature, phase) re-enters and upserts (FR-013): fields refreshed,
  validation summary appended as a new discussion comment; no duplicate.

## Persistence

### PhaseRunRecord  *(new EF Core entity in `PipelineDbContext`)*
Durable per-run audit record; the stored `WorkItemIdsJson` is the idempotency anchor.

| Column | Type | Notes |
|---|---|---|
| `RunId` | `string` (PK) | |
| `FeatureKey` | `string` (indexed) | Idempotency key part 1 |
| `Phase` | `string` (indexed) | Idempotency key part 2 |
| `Status` | `string` | `PhaseRunStatus` as string |
| `Summary` | `string?` | Validation summary |
| `GapsJson` | `string?` | Serialized gaps |
| `DecisionJson` | `string?` | Serialized `ApprovalDecision` |
| `WorkItemIdsJson` | `string?` | Serialized `CreatedWorkItemRef[]` — used for upsert lookup |
| `FailureReason` | `string?` | |
| `StartedAt` | `DateTimeOffset` | |
| `CompletedAt` | `DateTimeOffset?` | |

A **unique index on `(FeatureKey, Phase)`** enforces single-record-per-phase and backs idempotent
upsert.

## Validation Rules (from requirements)

- `FeatureDirectory` MUST resolve to an existing directory containing the file(s) expected for the
  phase; otherwise → `Failed` with `FailureReason` (FR-003, edge case "missing artifacts").
- `Phase` parsed to `Unsupported` → `Unsupported` status, no work item (FR-014).
- Inbound signal MUST carry feature reference + phase + valid shared secret, else rejected before a run
  starts (FR-002).
- `CreatedWorkItems` MUST be non-empty only after an approved decision (FR-006, FR-010).
- Plan phase: one `CreatedWorkItemRef` per `PlannedItem` (parsed from `tasks.md` when present, else
  derived from `plan.md` sections), each linked to the Epic (FR-008, FR-012).
