# Interface Contract: IBoardsClient

**Feature**: `specs/001-speckit-phase-handler`

The narrow seam between the SK pipeline and Azure DevOps Boards. The pipeline step depends only on
this interface; the concrete `AzureDevOpsBoardsClient` holds the `WorkItemTrackingHttpClient` and
builds the `JsonPatchDocument`s. This keeps SDK types out of the steps and gives unit tests a fake.

```csharp
/// <summary>
/// Creates and updates Azure DevOps Boards work items for the Spec Kit phase handler.
/// Implementations isolate the Azure DevOps client SDK so pipeline steps stay testable.
/// </summary>
public interface IBoardsClient
{
    /// Creates a work item of the given type (Epic/Task/Bug) and returns its id + url.
    Task<CreatedWorkItemRef> CreateWorkItemAsync(
        string workItemType, string title, string description, int? parentId,
        CancellationToken cancellationToken = default);

    /// Refreshes an existing work item's fields and APPENDS a discussion comment
    /// (System.History) — never overwrites prior comments. Returns the updated ref.
    Task<CreatedWorkItemRef> UpsertWorkItemAsync(
        int workItemId, string title, string description, string appendComment,
        CancellationToken cancellationToken = default);

    /// Appends a discussion comment to an existing work item (append-only).
    Task AppendDiscussionCommentAsync(
        int workItemId, string comment, CancellationToken cancellationToken = default);
}
```

**Contract notes**
- `workItemType` is passed straight to `CreateWorkItemAsync(patch, project, type)` — never set as a
  `System.WorkItemType` field patch.
- `parentId` (when not null) is linked via a `/relations/-` `System.LinkTypes.Hierarchy-Reverse`
  relation pointing at the Epic.
- `appendComment` is written to `System.History`, which appends to the Discussion and creates a new
  immutable revision (non-destructive — backs FR-013 / FR-018).
- All methods are `async` and flow a `CancellationToken` (Article IV).
- Failures surface as exceptions; the calling step records them as `Failed` with a reason (FR-015) —
  the client does not swallow them.

**Idempotency** is owned by the **step**, not the client: the step looks up the stored
`CreatedWorkItemRef` for `(featureKey, phase)` in `PhaseRunRecord` and chooses create vs upsert. The
client stays stateless.
