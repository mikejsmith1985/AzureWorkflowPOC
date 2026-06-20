# Interface Contract: IWorkflowThumbnailGenerator

**Assembly**: `DBAIAzure.Core`
**Namespace**: `DBAIAzure.Core.Interfaces`
**Consumer**: `WorkflowBuilderService.SaveAsync`

---

## Purpose

Generates a compact SVG schematic thumbnail from a `WorkflowDefinition`. The thumbnail
represents the workflow's node layout at a glance — coloured rectangles (one per node) connected
by directional lines (one per edge) — and is stored in `WorkflowDefinition.ThumbnailSvg` for
display on gallery cards.

---

## Interface Definition

```csharp
/// <summary>
/// Generates a compact SVG schematic thumbnail from the node layout of a workflow.
/// The thumbnail is stored on the <see cref="WorkflowDefinition"/> and displayed on
/// gallery cards to give users a visual preview without opening the builder.
/// </summary>
public interface IWorkflowThumbnailGenerator
{
    /// <summary>
    /// Produces an inline SVG string that schematically represents the workflow's node layout.
    /// Returns <see langword="null"/> when generation is not possible (e.g., zero nodes);
    /// callers must treat <see langword="null"/> as "no thumbnail available" and proceed
    /// without surfacing an error.
    /// </summary>
    /// <param name="workflow">The workflow definition whose nodes and edges are rendered.</param>
    /// <returns>
    /// An inline SVG string fitting a 200 × 100 viewBox, or <see langword="null"/>
    /// if the workflow has no nodes or generation fails.
    /// </returns>
    string? GenerateSvg(WorkflowDefinition workflow);
}
```

---

## Behaviour Contract

### Inputs

| Condition | Expected behaviour |
|-----------|-------------------|
| `workflow.Nodes` is empty | Return `null` (no thumbnail) |
| All nodes share the same position | Render overlapping rectangles (no special handling) |
| Any node has negative coordinates | Normalise via bounding-box calculation; coordinates are relative |
| `workflow.Edges` is empty | Render nodes only; no connection lines |

### Output SVG

| Requirement | Detail |
|-------------|--------|
| ViewBox | `0 0 200 100` — fixed; caller renders at any size |
| Nodes | `<rect>` elements, 18 × 10 px in normalised space, 3 px border-radius |
| Node colours | `WorkflowNodeType.Trigger` → `#10b981` (emerald); `AgenticReason` → `#f59e0b` (amber); `HumanApproval` → `#a855f7` (purple); function types → `#06b6d4` (cyan) |
| Edges | `<line>` elements from source node centre to target node centre; `stroke="#6b7280"`, `stroke-width="1"`, `marker-end` arrowhead |
| Node labels | Omitted — thumbnail is schematic only |
| Background | Transparent (`no` fill on root `<svg>`) |

### Error Handling

- Any exception during generation must be caught internally; `null` is returned, not re-thrown
- Callers (specifically `WorkflowBuilderService`) must proceed with the save without a thumbnail
- No logging of thumbnail failures is required (per clarification: silent fail)

---

## Integration Point

`WorkflowBuilderService.SaveAsync` calls `GenerateSvg` after the workflow is saved:

```csharp
var thumbnailSvg = _thumbnailGenerator.GenerateSvg(workflow);
if (thumbnailSvg is not null)
    workflow = workflow with { ThumbnailSvg = thumbnailSvg };
// Second save with thumbnail (or first save if null — no extra round-trip needed if
// the repository supports upsert with the merged record)
```

No new repository method is needed — `IWorkflowRepository.SaveAsync` already accepts
the full `WorkflowDefinition` including the `ThumbnailSvg` field.
