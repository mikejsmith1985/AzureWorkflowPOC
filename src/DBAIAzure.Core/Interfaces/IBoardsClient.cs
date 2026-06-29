// The narrow seam between the SK pipeline and Azure DevOps Boards, isolating the SDK from the steps.
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Creates and updates Azure DevOps Boards work items for the Spec Kit phase handler.
/// Implementations isolate the Azure DevOps client SDK so pipeline steps stay testable;
/// the steps depend only on this interface and a fake is used in unit tests.
/// </summary>
public interface IBoardsClient
{
    /// <summary>
    /// Creates a work item of the given type (Epic/Task/Bug) and returns its id + url.
    /// When <paramref name="parentId"/> is not null, the new item is linked as a child of that
    /// parent (the feature's Epic) so the board reflects the feature hierarchy (FR-012).
    /// </summary>
    Task<CreatedWorkItemRef> CreateWorkItemAsync(
        string workItemType, string title, string description, int? parentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes an existing work item's fields and APPENDS a discussion comment
    /// (System.History) — never overwrites prior comments (non-destructive, FR-013/FR-018).
    /// Returns the updated reference.
    /// </summary>
    Task<CreatedWorkItemRef> UpsertWorkItemAsync(
        int workItemId, string title, string description, string appendComment,
        CancellationToken cancellationToken = default);

    /// <summary>Appends a discussion comment to an existing work item (append-only).</summary>
    Task AppendDiscussionCommentAsync(
        int workItemId, string comment, CancellationToken cancellationToken = default);

    /// <summary>
    /// Patches arbitrary fields on an existing work item, keyed by ADO reference name
    /// (e.g. <c>Custom.AIInputTokens</c>). <c>System.Tags</c> is merged non-destructively — existing
    /// tags are preserved. Used by telemetry write-back to set the AI telemetry fields. Other fields
    /// are replaced with the supplied value.
    /// </summary>
    Task UpdateFieldsAsync(
        int workItemId, IReadOnlyDictionary<string, object?> fields,
        CancellationToken cancellationToken = default);
}
