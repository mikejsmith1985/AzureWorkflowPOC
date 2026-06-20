# Interface Contract: IWorkflowCodeDiffService

**Assembly**: `DBAIAzure.Core`
**Namespace**: `DBAIAzure.Core.Interfaces`
**Consumer**: `WorkflowChatPanel.razor` (after code regeneration when canvas has changed)

---

## Purpose

Computes a compact line-level diff between a previous version of generated workflow code and an
updated version. The result is a structured list of diff lines that the chat panel renders as a
colour-coded compact view (green for additions, red for removals, grey for context) with a
"Show full code" toggle.

---

## Interface Definition

```csharp
/// <summary>
/// Computes a compact line-level diff between two versions of generated workflow code.
/// Used by the chat assistant panel to show only what changed between consecutive
/// code-generation results, making it easy for non-technical users to see the impact
/// of their workflow modifications.
/// </summary>
public interface IWorkflowCodeDiffService
{
    /// <summary>
    /// Computes a compact diff showing lines that changed between
    /// <paramref name="previousCode"/> and <paramref name="updatedCode"/>,
    /// with up to 3 lines of unchanged context around each change hunk.
    /// </summary>
    /// <param name="previousCode">
    /// The code generated before the canvas modification. Null or empty is treated as empty string.
    /// </param>
    /// <param name="updatedCode">
    /// The code generated after the canvas modification. Null or empty is treated as empty string.
    /// </param>
    /// <returns>
    /// A <see cref="DiffResult"/> containing the structured compact diff.
    /// <see cref="DiffResult.HasChanges"/> is false when both inputs are identical.
    /// </returns>
    DiffResult ComputeDiff(string? previousCode, string? updatedCode);
}
```

---

## Domain Types

### `DiffLineType` (enum)

```csharp
public enum DiffLineType { Added, Removed, Unchanged }
```

### `DiffLine` (record)

```csharp
/// <summary>A single line in a compact diff, including its type and context status.</summary>
public sealed record DiffLine(string Content, DiffLineType Type, bool IsContext);
```

### `DiffResult` (record)

```csharp
/// <summary>
/// The structured output of a compact diff computation — contains only the changed lines
/// plus a bounded window of context around each hunk.
/// </summary>
public sealed record DiffResult(
    IReadOnlyList<DiffLine> Lines,
    bool HasChanges,
    int AddedCount,
    int RemovedCount);
```

---

## Behaviour Contract

### Context Window

- Exactly **3 unchanged lines** are included before and after each contiguous block of changed lines
- When two changed hunks are separated by ≤ 6 unchanged lines, they are merged into a single hunk
- Consecutive changed lines of the same type are always in the same hunk

### Special Cases

| Condition | Behaviour |
|-----------|-----------|
| `previousCode` == `updatedCode` (identical) | `HasChanges = false`; `Lines` is empty |
| `previousCode` is null/empty, `updatedCode` is non-empty | All lines in `updatedCode` are `Added`; `HasChanges = true` |
| `previousCode` is non-empty, `updatedCode` is null/empty | All lines in `previousCode` are `Removed`; `HasChanges = true` |
| Both null/empty | `HasChanges = false`; `Lines` is empty |

### Implementation Note

The implementation uses `DiffPlex.DiffBuilder.InlineDiffBuilder.Diff(previous, updated)` and
post-processes the result into `DiffLine` records. The 3-line context window is applied as a
second pass over the `InlineDiffResult.Lines` output.

---

## Chat Panel Rendering Contract

The `WorkflowChatPanel` renders a `DiffResult` as follows:

| `DiffLine.Type` | `DiffLine.IsContext` | CSS class | Prefix |
|-----------------|----------------------|-----------|--------|
| `Added` | false | `.diff-add` (green bg) | `+` |
| `Removed` | false | `.diff-remove` (red bg) | `-` |
| `Unchanged` | true | `.diff-context` (grey) | ` ` (space) |

A "Show full code" link beneath the diff block toggles to the complete `updatedCode` in a
standard syntax-highlighted block. The diff block and the full-code block are mutually
exclusive (one visible at a time).
