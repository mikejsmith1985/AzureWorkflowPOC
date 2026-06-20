# Data Model: Workflow Builder UX Master Review

**Feature**: `005-workflow-ux-redesign` | **Date**: 2026-06-20

No new database entities or migrations are required. `WorkflowDefinition.ThumbnailSvg`
already exists on the domain record and the EF Core entity.

---

## New Domain Types (`DBAIAzure.Core`)

### `DiffLineType` (enum)

```
DiffLineType
├── Added      — line present in new code only (rendered green, "+" prefix)
├── Removed    — line present in old code only (rendered red, "-" prefix)
└── Unchanged  — line present in both (rendered grey, no prefix; context lines only)
```

### `DiffLine` (record)

| Field | Type | Description |
|-------|------|-------------|
| `Content` | `string` | The text of the line (without prefix character) |
| `Type` | `DiffLineType` | Added, Removed, or Unchanged |
| `IsContext` | `bool` | True when this is a context line (≤ 3 lines from a changed line); false when it is a changed line itself |

### `DiffResult` (record)

| Field | Type | Description |
|-------|------|-------------|
| `Lines` | `IReadOnlyList<DiffLine>` | Ordered compact diff lines including context |
| `HasChanges` | `bool` | False when old and new code are identical (no diff rendered) |
| `AddedCount` | `int` | Total lines added across all hunks |
| `RemovedCount` | `int` | Total lines removed across all hunks |

---

## New Service Interfaces (`DBAIAzure.Core.Interfaces`)

### `IWorkflowThumbnailGenerator`

Produces an SVG schematic thumbnail from a `WorkflowDefinition`. Called by
`WorkflowBuilderService.SaveAsync` immediately before persisting.

| Member | Signature | Description |
|--------|-----------|-------------|
| `GenerateSvg` | `string? GenerateSvg(WorkflowDefinition workflow)` | Returns SVG string or null on failure (failure is silent — save proceeds) |

**Invariants**:
- Returned SVG fits a 200 × 100 viewBox
- One `<rect>` per node, coloured by `NodeType`
- One `<line>` per edge (source node centre → target node centre)
- Node labels omitted from thumbnail
- Empty workflow (zero nodes) returns `null`

### `IWorkflowCodeDiffService`

Computes a compact diff between two versions of generated workflow code. Called by
`WorkflowChatPanel` after code regeneration when `_previousGeneratedCode` is not null.

| Member | Signature | Description |
|--------|-----------|-------------|
| `ComputeDiff` | `DiffResult ComputeDiff(string previousCode, string updatedCode)` | Returns compact diff with ± 3 context lines per changed hunk |

**Invariants**:
- Null or empty inputs treated as empty strings (no exception thrown)
- Context window: exactly 3 unchanged lines before and after each changed line block
- Consecutive changed lines are merged into a single hunk (not split)

---

## New Component State (`DBAIAzure.Web`)

### `WorkflowBuilder.razor` — new fields

| Field | Type | Initial Value | Description |
|-------|------|---------------|-------------|
| `_hasUnsavedChanges` | `bool` | `false` | True after any committed change since last save |
| `_savedWorkflowCount` | `int` | `0` | Count from `ListByOwnerAsync` on init; gates entry choice screen |
| `_pendingNavigationUri` | `string?` | `null` | URI stored when navigation is blocked by the unsaved guard |
| `_isUnsavedChangesModalOpen` | `bool` | `false` | True when the unsaved-changes confirmation dialog is visible |
| `_hasCanvasChangedSinceCodeGen` | `bool` | `false` | True when topology changed after last code generation |

### `WorkflowToolbar.razor` — new parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `HasCanvasChangedSinceCodeGen` | `bool` | When true, orange dot rendered on Chat button |
| `OnNameChanged` | `EventCallback<string>` | Raised when user commits a new workflow name inline |
| `IsShortcutsPanelOpen` | `bool` | Controls visibility of the keyboard shortcuts panel |
| `OnShortcutsToggleClicked` | `EventCallback` | Raised when "?" button is clicked |

### `WorkflowCanvas.razor` — new outputs

| Callback | Type | Description |
|----------|------|-------------|
| `OnFirstNodePlaced` | `EventCallback` | Raised once when the canvas transitions from 0 → 1 nodes (clears welcome overlay) |
| `OnNodeSingleClicked` | `EventCallback<string>` | Raised when a node is selected (single click); carries NodeId for tooltip management |

### `WorkflowChatPanel.razor` — new fields

| Field | Type | Description |
|-------|------|-------------|
| `_previousGeneratedCode` | `string?` | Snapshot of code before regeneration; used for diff computation |
| `_feedbackPrePopulatedMessage` | `string?` | Set by `PrePopulateFeedback(NodeExecutionState)` public method; placed into the input box on open |

### `WorkflowNodeRenderer.razor` — new rendering states

| State | Trigger | Visual |
|-------|---------|--------|
| `IsSetUpAffordanceVisible` | `!Node.WorkflowNode.IsConfigured` | Small "Set up →" label beneath amber "!" badge |
| `IsSingleClickTooltipVisible` | Node selected (single click) for < 2 s | "Double-click to configure" callout above node |

---

## Modified Domain Behaviour

### `WorkflowBuilderService.SaveAsync` (existing method)

After the existing save logic, invoke `IWorkflowThumbnailGenerator.GenerateSvg(workflow)`.
If non-null, merge the result into the saved record via `workflow with { ThumbnailSvg = svg }`.
Failure (null return) is silent — the save record is persisted without a thumbnail.

No change to `IWorkflowRepository.SaveAsync` signature or storage schema.

---

## Entity Relationship Summary

```
WorkflowDefinition (existing, unchanged schema)
  ├── ThumbnailSvg : string?     ← populated by WorkflowThumbnailGenerator at save time
  ├── GeneratedCode : string?    ← triggers _hasCanvasChangedSinceCodeGen when non-null on change
  └── Nodes[]
       └── IsConfigured : bool   ← drives "Set up" affordance on WorkflowNodeRenderer
```
