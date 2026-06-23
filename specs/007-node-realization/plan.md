# Implementation Plan: Node Realization — Convert Plain-Language Nodes into Production-Ready Agentic & Function Nodes

**Branch**: `feature/node-realization` | **Date**: 2026-06-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/007-node-realization/spec.md`

## Summary

Add a guided **"Make it real"** step to the Visual Workflow Builder that converts plain-language
nodes into validated, executable per-node configuration the existing Semantic Kernel Process
runtime runs directly. The LLM *proposes* a structured configuration for each node (derived from
its goal, I/O labels, graph neighbours, and the workspace's configured connectors); the user
reviews and accepts each proposal in plain language; the workflow becomes "production-ready" only
when every node is realized, valid, cross-consistent, and its bound connectors pass a health
check.

**Technical approach**: Reuse the existing `IStructuredCompletionService` (forced-tool JSON
output) to generate a typed per-node-type config record that is serialized into the node's
existing `FunctionConfig` field — no new node-graph schema. A new scoped `WorkflowRealizationService`
orchestrates per-node proposal generation (mirroring the existing `WorkflowDesignSkillService`
LLM-loop pattern but with structured output). A new `WorkflowReadinessService` evaluates
production-readiness (structural validation + per-type config validation + connector-binding
presence + `IConnectorHealthChecker` health check). The four POC function-node runtime steps
(Notify/Data/Transform/Route) are upgraded to *consume* the realized `FunctionConfig` so a
realized workflow genuinely executes. Review/accept happens in a new realization panel that
reuses the config-panel UX patterns; HITL approval nodes bind to the framework's existing
`IExternalKernelProcessMessageChannel` pause/resume.

## Technical Context

**Language/Version**: C# / .NET 8 (pinned via `global.json`).

**Primary Dependencies**: Semantic Kernel **Process Framework** (orchestration, typed steps,
process events, HITL channel); `IStructuredCompletionService` (Anthropic-backed forced-tool JSON
output) for structured proposals; Blazor Server + Z.Blazor.Diagrams (builder UI); EF Core +
SQLite (persistence); existing connector subsystem (`IConnectorConfigRepository`,
`IConnectorHealthChecker`, `ConnectorType`).

**Storage**: SQLite via `WorkflowDefinition` JSON blobs (`NodesJson`, `EdgesJson`, `SettingsJson`).
Realized config lives in the existing `WorkflowNode.FunctionConfig` string; realization provenance
(for out-of-date detection) lives in `WorkflowSettings` (same channel as `DesignSkillAnswers`).

**Testing**: xUnit unit tests (mocked `IStructuredCompletionService`, readiness rules), xUnit
integration tests (real structured output + real connector health check), Playwright E2E
(`scripts/run-e2e.ps1`) for the "Make it real" → review → ready → run flow.

**Target Platform**: Blazor Server web app on Kestrel (single web process + class libraries).

**Project Type**: Web application — one Blazor Server app (`DBAIAzure.Web`) over Core/Connectors/
Processes/Storage class libraries.

**Performance Goals**: Reviewable proposals for a typical 5–8 node workflow within one guided
session (~2 min of assistant work, SC-2), with visible per-node progress. Readiness re-evaluation
(incl. connector health) on demand, not on every keystroke.

**Constraints**: Never fabricate credentials/endpoints/bindings (FR-16.3, Article IX); every node
reviewed & explicitly accepted before "realized" (FR-16.1); production-ready gate = config
completeness + cross-node consistency + connector health (FR-17.3); plain-language layer preserved
and re-editable (FR-16.5).

**Scale/Scope**: Single-user authoring session; workflows up to a few dozen nodes; 7 node types.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Article | Gate | Assessment |
|---------|------|------------|
| I — Prime Directive | Production-ready, no quick-and-dirty | ✅ Plan upgrades the POC function-steps to genuinely execute realized config rather than stubbing readiness. |
| II — Process Protection | No wildcard kills | ✅ N/A to design; honored operationally. |
| III — Branching | Feature branch | ✅ `feature/node-realization`. |
| IV — Code Quality | Naming, XML docs, <40-line methods, nullable | ✅ New services/records follow conventions; per-type config records are small and named. |
| V — Testing (3-layer + TDD) | Unit mocked, integration real, Playwright E2E, Red→Green | ✅ Unit (mocked structured svc), integration (real SK structured output + real health check), E2E (make-it-real flow). Tests authored before implementation. |
| VI — Docs | CHANGELOG, no ad-hoc status docs | ✅ CHANGELOG updated at PR; only `specs/007-*` pipeline artifacts created. |
| **VII — Framework-First** | Reuse SK primitives, don't rebuild | ✅ **Structured output** → `IStructuredCompletionService` (not hand-parsed). **HITL** → existing `HumanApprovalStep` + `IExternalKernelProcessMessageChannel`. **Orchestration/state** → existing `KernelProcess` runtime; realization adds *configuration*, not a new state machine. **Connectors/health** → existing repo + `IConnectorHealthChecker`. **LLM-propose-user-review loop** → mirrors existing `WorkflowDesignSkillService`. No parallel registries built. |
| VIII — Release | Reproducible build | ✅ N/A to design. |
| IX — Secrets | No secrets in source/logs/config | ✅ Realization binds nodes to a `ConnectorType` *reference*; secrets stay in the encrypted connector repo and never enter proposals, prompts, or `FunctionConfig`. |
| X — Verification & Proof | Evidence, not "it compiled" | ✅ SC-6 requires a realized workflow to actually run end-to-end; E2E + a live run are the proof. |
| XI — Output restraint | No phase narration / stray dashboards | ✅ Honored. |

**Result**: PASS. No violations → Complexity Tracking table omitted.

## Project Structure

### Documentation (this feature)

```text
specs/007-node-realization/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (service + config-schema contracts)
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/DBAIAzure.Core/
├── Models/
│   ├── NodeConfig/                       # NEW typed per-node-type realized config records
│   │   ├── AgentNodeConfig.cs            #   instruction, model ref, structured output shape, tool bindings
│   │   ├── NotifyNodeConfig.cs           #   ConnectorType binding, recipient map, message template
│   │   ├── DataNodeConfig.cs             #   ConnectorType binding, operation, in/out mapping
│   │   ├── RouteNodeConfig.cs            #   per-output-port condition + default path
│   │   ├── TransformNodeConfig.cs        #   input→output field mapping
│   │   ├── ApprovalNodeConfig.cs         #   approver, prompt shown, decision options
│   │   └── TriggerNodeConfig.cs          #   formalizes existing {initialDataDescription}
│   ├── RealizationProposal.cs            # NEW one not-yet-accepted candidate config for a node
│   ├── NodeRealizationStatus.cs          # NEW enum: Draft|Proposed|Realized|Blocked|NeedsInput|OutOfDate
│   ├── WorkflowReadinessReport.cs        # NEW aggregate + per-node readiness
│   └── WorkflowSettings.cs               # EXTEND: add RealizationProvenance (nodeId→intent hash)
├── Interfaces/
│   ├── IWorkflowRealizationService.cs    # NEW propose-per-node + accept
│   └── IWorkflowReadinessService.cs      # NEW async readiness evaluation (incl. health check)
└── Validation/
    └── WorkflowValidator.cs              # EXTEND: VAL-004..VAL-007 per-node realized/valid rules

src/DBAIAzure.Web/Services/
├── WorkflowRealizationService.cs         # NEW uses IStructuredCompletionService to propose configs
└── WorkflowReadinessService.cs           # NEW validator + per-type config check + connector health

src/DBAIAzure.Web/Components/WorkflowBuilder/
├── WorkflowRealizationPanel.razor        # NEW per-node proposal review (accept/edit/reject/regenerate)
├── WorkflowToolbar.razor                 # EXTEND: "Make it real" action + readiness indicator
├── WorkflowNodeRenderer.razor            # EXTEND: per-node realization status badge
└── WorkflowCanvas.razor / Pages/WorkflowBuilder.razor  # EXTEND: wire realization + run gating

src/DBAIAzure.Processes/Pipeline/
├── FunctionNotifyStep.cs                 # UPGRADE: consume NotifyNodeConfig → real connector send
├── FunctionDataStep.cs                   # UPGRADE: consume DataNodeConfig → real read/write
├── FunctionRouteStep.cs                  # UPGRADE: consume RouteNodeConfig conditions
├── FunctionTransformStep.cs              # UPGRADE: consume TransformNodeConfig mapping
└── AgenticNodeStep.cs                    # EXTEND: honor AgentNodeConfig (model/output shape/tools)

tests/DBAIAzure.Tests/
├── WorkflowRealizationServiceTests.cs    # NEW mocked IStructuredCompletionService
├── WorkflowReadinessServiceTests.cs      # NEW per-type validity + blocked/out-of-date rules
└── NodeConfigSerializationTests.cs       # NEW round-trip per-type config ↔ FunctionConfig

tests/DBAIAzure.E2ETests/Tests/
└── NodeRealizationTests.cs               # NEW Playwright: make-it-real → review → ready → run
```

**Structure Decision**: Existing web-app layout retained. Domain types (config records, statuses,
report) and interfaces live in `DBAIAzure.Core`; the LLM-driven realization and readiness services
live in `DBAIAzure.Web/Services` (alongside `WorkflowDesignSkillService`, which they mirror);
runtime consumption upgrades live in `DBAIAzure.Processes/Pipeline`. No new project is introduced.

## Key Risks & Decisions (carried into research.md)

1. **Runtime stubs are real scope, not a footnote.** Function-node steps currently ignore
   `FunctionConfig`. "Executes for real" (SC-6) requires upgrading them. Phasing: AgenticReason +
   Notify + Route first (demoable triage flow), then Data + Transform.
2. **No new node model fields.** Realized config reuses `FunctionConfig`; provenance (intent hash
   for out-of-date detection) reuses `WorkflowSettings`. Avoids a schema migration.
3. **Readiness needs async + repo access** (connector health), which the sync `IWorkflowValidator`
   can't provide → separate `IWorkflowReadinessService`; the structural validator stays sync and
   is composed into it.
4. **Secrets boundary**: proposals reference a `ConnectorType`, never credentials.
