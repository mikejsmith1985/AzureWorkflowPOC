// Read-side model for the work-tracker adapter (spec-021): a work item's watched fields normalized into plain
// text for the AI review payload, plus the failure raised when a status transition cannot be applied.
namespace DBAIAzure.Core.Models.WorkTracker;

/// <summary>
/// A work item read into a review payload: its key, browse URL, and the requested watched fields flattened to
/// plain strings (rich values such as ADF descriptions and option objects rendered to text). Absent fields are
/// simply missing from <see cref="Fields"/>.
/// </summary>
public sealed record WorkItemFields(
    string Key,
    string Url,
    IReadOnlyDictionary<string, string?> Fields);

/// <summary>
/// Raised when a work-item status transition fails hard (invalid/again transition id, insufficient permission).
/// Best-effort callers catch this and degrade to a manual exit rather than leaving a partial write.
/// </summary>
public sealed class WorkTrackerTransitionException : Exception
{
    public WorkTrackerTransitionException(string message) : base(message) { }
    public WorkTrackerTransitionException(string message, Exception inner) : base(message, inner) { }
}
