# Contract: Node config-panel persistence (US1)

**Scope**: `WorkflowNodeConfigPanel.razor` (the right-side editor) and its host
`WorkflowBuilder.razor`. No new public interface — this is a behavioral contract over the existing
edit→commit→save chain, correcting the dual-source-of-truth defect identified in research.md
Decision 1.

## Invariants the implementation MUST uphold

| # | Invariant | Maps to |
|---|-----------|---------|
| 1 | After a node-text field is edited and the edit is **committed**, the workflow model (`_workflow`) reflects the new value for that field. | FR-001, FR-003 |
| 2 | The control that commits a node edit is reachable within/adjacent to the editor panel — the user is not required to travel to the top toolbar to capture a side-panel edit. | FR-002, SC-002 |
| 3 | An in-progress (uncommitted) edit is not discarded or reset by unrelated re-renders of the canvas or panel; only an explicit commit or explicit abandon changes the stored value. | FR-003 |
| 4 | All editable fields propagate — including the Trigger's "What information is available at the start?" — not only the Goal→Label preview. | FR-001 |
| 5 | A persist initiated from **any** path (in-panel Save, toolbar Save, auto-save) captures the open panel's committed edits — no path serializes stale node text. | FR-001, SC-001 |
| 6 | After commit, the new text is visible both on the canvas node and in the editor panel for that node (no reversion to prior/default). | FR-004 |
| 7 | If a persist fails, the user is notified (toast/banner); a success state is never shown over lost data. | FR-012 |
| 8 | Required-field rules (Goal required for Trigger/AgenticReason) still hold — an empty required field is refused with the existing amber banner, not silently saved. | existing behavior preserved |

## Acceptance walkthrough (the failing-first scenario)

1. Open a workflow; double-click the Trigger node; the editor panel opens.
2. Change "What starts this workflow?" and "What information is available at the start?".
3. Commit via the in-panel Save (adjacent to the fields).
4. Fully reload the workflow from storage (fresh navigation / reload).
5. **Expected:** both edited values are present on the node and in the panel — not the prior values,
   not defaults. (Today this fails when the user used the toolbar Save without "Done"; the fix makes
   every path persist the committed edit.)

## Tests
- **Unit/component** (`WorkflowNodeConfigPanelTests`): committing a field edit produces an updated
  `WorkflowNode` whose fields match the inputs; an unrelated re-render does not reset in-progress
  text; the Trigger initial-data field round-trips through `FunctionConfig`.
- **Integration**: edit → commit → `WorkflowBuilderService.SaveAsync` → `LoadAsync` returns the
  edited text (real SQLite repository).
- **E2E (Playwright)**: `NodeEditPersistenceTests` — edit node text in the panel, reload the page,
  assert the edited text is shown; assert the in-panel Save control exists next to the fields.
