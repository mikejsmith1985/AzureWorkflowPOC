# Contract: IWorkflowValidator (new interface)

**File**: `src/DBAIAzure.Core/Interfaces/IWorkflowValidator.cs` (new file)
**Consumer projects**: `DBAIAzure.Web` (via DI), `DBAIAzure.Tests`

---

## Purpose

Extracts workflow-level structural validation out of `WorkflowDefinition.ThrowIfInvalid()`
into a dedicated interface so that:
1. The Web layer can inject it and display user-friendly messages without catching exceptions.
2. Tests can validate the rules in isolation without constructing a full `WorkflowDefinition`.

---

## Interface Definition

```csharp
/// <summary>
/// Validates the structural invariants of a workflow definition before it is
/// persisted or executed.
/// </summary>
public interface IWorkflowValidator
{
    /// <summary>
    /// Returns a list of plain-language validation messages.
    /// An empty list means the workflow is structurally valid.
    /// Messages are written for non-technical users — no stack traces, no enum names.
    /// </summary>
    /// <param name="definition">The workflow to validate.</param>
    IReadOnlyList<string> Validate(WorkflowDefinition definition);
}
```

---

## Default Implementation: `WorkflowValidator`

**File**: `src/DBAIAzure.Core/Validation/WorkflowValidator.cs` (new file)

**Rules enforced**:

| Rule ID | Spec Ref | Message (exact) |
|---------|----------|-----------------|
| VAL-001 | FR-09.3 | `"Add a starting trigger to run this workflow."` (zero triggers) |
| VAL-002 | FR-09.3 | `"A workflow may contain only one starting trigger. Remove the extra trigger before saving."` (two+ triggers) |
| VAL-003 | spec-003 FR-08.3 | `"One or more steps are not connected to anything. Connect all steps before running."` (island node) |

**Note**: VAL-003 is an existing rule from spec 003 FR-08.1/FR-08.3 surfaced here so the
validator is the single source of structural truth. The canvas-level toast messages remain
for immediate user feedback during editing; `IWorkflowValidator` is the authoritative gate
before persist and execute.

---

## Registration

```csharp
// In DBAIAzure.Web / Program.cs DI registration block:
builder.Services.AddSingleton<IWorkflowValidator, WorkflowValidator>();
```

---

## Usage in WorkflowBuilderService

```csharp
// Before calling IWorkflowRepository.SaveAsync:
var messages = _validator.Validate(definition);
if (messages.Any())
    throw new WorkflowValidationException(messages);
```

**`WorkflowValidationException`** (new exception class in `DBAIAzure.Core/Exceptions/`):
- Carries `IReadOnlyList<string> Messages`
- The Web layer catches it and displays each message as an amber banner — no stack trace
  visible to the user
