# Phase 1 Data Model: Per-Workflow Graph View & Trustworthy Node Editing

**No persisted schema changes.** This feature reuses the existing model unchanged and adds one
in-memory, non-persisted view type. The `WorkflowDefinitions` SQLite table and all DTOs are
untouched.

---

## Existing entities used (unchanged)

### WorkflowDefinition (`src/DBAIAzure.Core/Models/WorkflowDefinition.cs`)
The saved workflow. Read by the Graph view and the seeder; written (via the panel fix) by the
builder. Relevant fields: `Id` (Guid), `Name`, `OwnerId`, `Nodes`, `Edges`, `Settings`,
`CreatedAt`, `LastModifiedAt`, `ThumbnailSvg`.
- **Uniqueness rule (existing):** `(OwnerId, Name)` is unique — the seeder relies on this to detect
  an already-seeded "Intake Pipeline".

### WorkflowNode (`src/DBAIAzure.Core/Models/WorkflowNode.cs`)
A step. The Graph view reads `Label`, `NodeType`, and the edit panel persists `Label`, `GoalPrompt`,
`InputLabel`, `OutputLabel`, `FunctionConfig` (Trigger's `initialDataDescription`), `IsConfigured`.
- **Label fallback rule:** an empty `Label` MUST render via the same fallback the canvas uses (by
  node type), never as a blank box (FR-011) — applied in the Mermaid generator and in the seeded
  node labels.

### WorkflowEdge (`src/DBAIAzure.Core/Models/WorkflowEdge.cs`)
A directed, labelled connection: `SourceNodeId`, `SourcePortId`, `TargetNodeId`, `TargetPortId`,
`Label`. The Graph view renders one arrow per edge, labelled with `Label` (plain arrow when empty).

### WorkflowNodeType (`src/DBAIAzure.Core/Models/WorkflowNodeType.cs`)
`Trigger`, `AgenticReason`, `FunctionRoute`, `FunctionTransform`, `FunctionNotify`, `FunctionData`,
`HumanApproval`. Drives node-shape selection in the Mermaid generator and the seeded topology mapping
(see research.md Decision 3).

---

## New type (in-memory only, not persisted)

### WorkflowMermaidDiagram (or a plain `string` return)
The output of `IWorkflowMermaidGenerator`. Simplest form: a `string` containing a Mermaid
`flowchart` definition ready for `window.mermaidRender`. If a wrapper record is warranted for the
empty-state signal, it carries:

| Field        | Type     | Meaning                                                        |
|--------------|----------|---------------------------------------------------------------|
| `Definition` | `string` | Mermaid `flowchart` text (empty string when no nodes).        |
| `IsEmpty`    | `bool`   | True when the workflow has no nodes → page shows empty-state.  |

- **Validation rules:** deterministic and pure (no I/O); every node id maps to a Mermaid-safe id;
  all labels are Mermaid-escaped; disconnected nodes are still emitted; output is stable for a given
  input (so it is unit-assertable).

---

## State transitions

### Node-edit persistence (US1) — corrected flow
```
User edits field in panel
  → panel writes value through to node model on change/blur (debounced)      [NEW]
  → workflow model (_workflow) now reflects the edit                          [single source of truth]
  → in-panel Save (adjacent to fields) commits + persists immediately         [NEW]
        OR toolbar Save flushes the open panel, then persists                 [NEW guard]
        OR 60s auto-save persists (signature now reflects the edit)           [existing]
  → on persist failure: user is notified; no success shown over lost data     [FR-012]
  → reload workflow from storage → edited text present                        [FR-001 / SC-001]
```

### Seeded workflow lifecycle (US3)
```
App startup (post-Build scope)
  → seeder checks owner 'demo' for a workflow named "Intake Pipeline"
        present  → no-op (never overwrites a user-edited copy)                [FR-010 idempotency]
        absent   → build topology + repository.SaveAsync                      [creates real row]
  → appears in Workflows gallery like any user workflow; Graph view renders it from real data
```

### Graph view request (US2)
```
Gallery card "Graph" → /workflow-graph/{id}
  → load workflow by (id, owner)
        found    → generate Mermaid from real nodes/edges → mermaidRender
        empty    → empty-state message (no render)                            [FR-011]
        not found→ not-found state (no crash, no blank diagram)               [FR-011 / edge case]
  → "Open in builder" link round-trips to /workflow-builder/{id}             [FR-009]
```
