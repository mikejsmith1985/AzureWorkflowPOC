# Contract: IWorkflowMermaidGenerator

**Type**: Internal service interface (`DBAIAzure.Core/Interfaces`), implemented in
`DBAIAzure.Web/Services/WorkflowMermaidGenerator.cs`. Registered as a singleton in `Program.cs`.

**Purpose**: Convert a saved `WorkflowDefinition` into a Mermaid `flowchart` definition that the
read-only per-workflow Graph view renders via the existing `window.mermaidRender`. This is the one
custom component justified under Article VII (no existing component produces a labelled,
auto-laid-out flowchart from a workflow).

## Surface

```csharp
public interface IWorkflowMermaidGenerator
{
    /// <summary>
    /// Builds a Mermaid `flowchart` definition from the workflow's real nodes and edges.
    /// Pure and deterministic — no I/O. Returns an empty string when the workflow has no nodes
    /// (the caller shows an empty-state instead of rendering).
    /// </summary>
    string Generate(WorkflowDefinition workflow);
}
```

## Behavioral contract

| # | Given | Then |
|---|-------|------|
| 1 | A workflow with N nodes and M edges | Output begins with `flowchart` and contains exactly N node declarations and M edge lines. |
| 2 | A node with a non-empty `Label` | The node is declared with that label, Mermaid-escaped. |
| 3 | A node with an empty/whitespace `Label` | The node is declared with the canvas's by-type fallback label, never blank (FR-011). |
| 4 | An edge with a non-empty `Label` | The arrow carries that label (`-->|"label"|`), escaped. |
| 5 | An edge with an empty `Label` | A plain arrow (`-->`) is emitted. |
| 6 | A node with no connected edges (disconnected) | The node is still declared (not dropped) (FR-011). |
| 7 | A workflow with zero nodes | Returns empty string; `IsEmpty` path triggers the page empty-state. |
| 8 | Labels containing `"`, `[`, `]`, `|`, `{`, `}`, or newlines | Escaped/sanitized so the definition is valid Mermaid (no broken render). |
| 9 | The same workflow passed twice | Byte-identical output (deterministic — unit-assertable). |
| 10 | Node ids (8-hex strings) | Mapped to Mermaid-safe node identifiers (stable per id within one call). |

## Notes
- Node **shape** MAY vary by `WorkflowNodeType` (e.g. stadium for `Trigger`, rhombus for
  `FunctionRoute`) for readability; shape choice is cosmetic and not asserted beyond "valid Mermaid".
- The generator does NOT read storage, the network, or the clock — it is a pure function of its input.

## Tests (Article V, unit — milliseconds, mocked input)
- Emits correct node/edge counts for a multi-node workflow.
- Falls back for empty labels; plain arrow for empty edge labels.
- Keeps disconnected nodes.
- Empty workflow → empty string.
- Escapes reserved characters without producing invalid Mermaid.
- Deterministic output across repeated calls.
