# Contract: IWorkflowCodeGenerator

**Feature**: 003-visual-workflow-builder | **Layer**: `DBAIAzure.Core.Interfaces`
**Implementation**: `DBAIAzure.Web.Services.WorkflowCodeGenerator`

---

## Responsibility

Generates complete, compilable SK Process Framework source code from a `WorkflowDefinition`
and the accumulated chat conversation. Accepts natural-language follow-up instructions and
produces a diff view when updating previously generated code.

---

## Interface Definition

```csharp
/// <summary>
/// Generates SK Process Framework source code from a workflow topology and chat conversation.
/// Streams tokens progressively so the UI can render output incrementally.
/// </summary>
public interface IWorkflowCodeGenerator
{
    /// <summary>
    /// Generates complete workflow code from <paramref name="workflow"/> and
    /// <paramref name="chatHistory"/>. Streams tokens via <paramref name="onToken"/>.
    /// Returns the full generated code string when streaming completes.
    /// </summary>
    Task<string> GenerateAsync(
        WorkflowDefinition workflow,
        IReadOnlyList<WorkflowChatMessage> chatHistory,
        Action<string> onToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refines <paramref name="previousCode"/> based on <paramref name="instruction"/>.
    /// Streams tokens via <paramref name="onToken"/>. Returns the updated code and a
    /// <see cref="CodeDiff"/> listing only the lines that changed.
    /// </summary>
    Task<(string UpdatedCode, CodeDiff Diff)> RefineAsync(
        string previousCode,
        string instruction,
        WorkflowDefinition workflow,
        Action<string> onToken,
        CancellationToken cancellationToken = default);
}
```

---

## CodeDiff (value type)

```csharp
/// <summary>
/// Represents the changed lines between two versions of generated code.
/// Used to drive the diff-style view in the chat panel.
/// </summary>
public sealed record CodeDiff(
    IReadOnlyList<DiffLine> Lines
);

public sealed record DiffLine(
    int LineNumber,
    DiffLineKind Kind,    // Unchanged | Added | Removed
    string Content
);
```

---

## Prompt Strategy

`GenerateAsync` constructs a system prompt that includes:
1. A topology summary: each node's label, type, goal/constraints, and port names.
2. The edge routing: each connection expressed as "Node A [output: Result] → Node B [input: Input]".
3. The workflow settings: timeout, any design-skill answers.
4. An instruction to produce exactly one `.cs` file containing:
   - A `WorkflowEvents` static class with `string` constants for each named event.
   - One `KernelProcessStep` subclass per agentic node (goal as system prompt).
   - A `ProcessBuilder`-based static factory class mirroring `IntakePipelineBuilder`.

`RefineAsync` appends the previous code as context and the user's instruction as a new turn.

---

## Contracts and Invariants

1. **Streaming**: Both methods must stream tokens progressively via `onToken` — the full
   response must not be buffered before the first token is emitted.
2. **LLM unavailability**: If the LLM is unreachable, both methods throw
   `LlmUnavailableException`. The caller (chat panel) surfaces a plain-language message.
3. **Code completeness**: `GenerateAsync` must produce a self-contained `.cs` file — no
   `// TODO` markers, no undefined references to types not in the output.
4. **Diff accuracy**: `RefineAsync` diff must reflect only lines that differ between
   `previousCode` and the updated output. Unchanged lines are `DiffLineKind.Unchanged`.

---

## Test Obligations

- Unit tests mock `IChatCompletionService` to return a deterministic code string.
- Verify topology-to-prompt serialization includes all node labels and connections.
- Verify `RefineAsync` diff correctly identifies added and removed lines.
- Verify `LlmUnavailableException` is thrown when the service mock returns a failure.
