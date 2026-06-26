# Contract: IntakePipelineSeeder (US3)

**Type**: Startup seeder, `DBAIAzure.Web/Services/IntakePipelineSeeder.cs`, invoked from the existing
post-`Build()` startup scope in `Program.cs` (alongside `EnsureCreatedAsync`).

**Purpose**: Idempotently ensure a real, persisted "Intake Pipeline" `WorkflowDefinition` exists for
owner `demo`, reproducing the topology the retired hardcoded `/graph` documented, so the
per-workflow Graph view has authentic data.

## Surface

```csharp
public sealed class IntakePipelineSeeder
{
    /// <summary>
    /// Ensures the seeded "Intake Pipeline" workflow exists for the demo owner. Idempotent:
    /// if a workflow with that owner+name already exists, does nothing (never overwrites edits).
    /// </summary>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
```

## Behavioral contract

| # | Given | Then |
|---|-------|------|
| 1 | No workflow named "Intake Pipeline" exists for owner `demo` | One is built and saved via `IWorkflowRepository`/`WorkflowBuilderService`. |
| 2 | A workflow named "Intake Pipeline" already exists for owner `demo` | No write occurs; the existing (possibly user-edited) workflow is left untouched (FR-010). |
| 3 | `SeedAsync` is called repeatedly (multiple restarts) | Exactly one "Intake Pipeline" row exists — no duplicates (relies on `(OwnerId, Name)` uniqueness + the presence check). |
| 4 | The seeded workflow is opened in the gallery/Graph view | Its topology reproduces sources→intake→validation→branch(gap-analysis/estimation)→human-pause→action→done, including the validation branch and the HITL pause (structural fidelity). |
| 5 | The seeded workflow is opened in the builder | It behaves like any user workflow — inspectable and editable, and its edits persist (US1). |

## Topology (see research.md Decision 3 for the full mapping)
- `Trigger` "Ticket received (ServiceNow/Manual)"
- `AgenticReason` Intake → `AgenticReason` Validation
- `FunctionRoute` validation branch → ReadyPath / NotReadyPath / Blocked
- `AgenticReason` GapAnalysis → `HumanApproval` (HITL pause) → back to Validation
- `AgenticReason` Estimation → `FunctionNotify` Action (terminal)
- Edge labels reuse the original event names (TicketReceived, IntakeComplete, ReadyPath,
  NotReadyPath, QuestionsReady, HumanResponded, EstimationComplete).

## Tests (Article V)
- **Unit/integration** (`IntakePipelineSeederTests`): first `SeedAsync` creates exactly one workflow
  with the expected node types and edges; a second `SeedAsync` is a no-op (count stays 1); a
  pre-existing user-edited "Intake Pipeline" is not overwritten.
- **E2E (Playwright)**: the seeded workflow appears in the gallery and its Graph view renders the
  expected topology.
