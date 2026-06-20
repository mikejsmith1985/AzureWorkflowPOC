// Defines the data model for representing a code diff, used to drive diff-highlight rendering in the chat panel.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Classifies a single line in a diff as added, removed, or unchanged —
/// so the chat panel renderer knows which colour to apply to each line.
/// </summary>
public enum DiffLineType
{
    /// <summary>The line was introduced in the after version — render as an addition.</summary>
    Added,

    /// <summary>The line was present in the before version but is absent from the after version — render as a deletion.</summary>
    Removed,

    /// <summary>The line exists in both versions and is included as surrounding context only.</summary>
    Unchanged,
}

/// <summary>
/// Represents a single line in a code diff, pairing its text content with its change
/// classification and a flag indicating whether it is a context line or a changed line.
/// Context lines are unchanged neighbours shown for readability; changed lines carry the
/// actual additions or removals.
/// </summary>
/// <param name="Content">The raw text content of the line, without a trailing newline.</param>
/// <param name="Type">Whether this line is added, removed, or unchanged.</param>
/// <param name="IsContext">
/// True when this line is included only as context (unchanged, within 3 lines of a change);
/// false when the line itself is the change (<see cref="DiffLineType.Added"/> or
/// <see cref="DiffLineType.Removed"/>) or when all lines are included without windowing.
/// </param>
public sealed record DiffLine(string Content, DiffLineType Type, bool IsContext);

/// <summary>
/// Holds the complete set of annotated lines that make up a compact code diff.
/// This record is the contract between the diff-computation service and the
/// chat panel UI component: the panel iterates <see cref="Lines"/> in order
/// and applies per-line highlight colours based on each line's <see cref="DiffLineType"/>.
/// </summary>
/// <param name="Lines">
/// An ordered, read-only list of <see cref="DiffLine"/> values representing every line
/// in the compact diff (changed lines plus up to 3 context lines around each hunk).
/// Empty when <see cref="HasChanges"/> is false.
/// </param>
/// <param name="HasChanges">True when at least one line differs between the two inputs.</param>
/// <param name="AddedCount">Number of lines present in the updated version but absent from the original.</param>
/// <param name="RemovedCount">Number of lines present in the original but absent from the updated version.</param>
public sealed record DiffResult(
    IReadOnlyList<DiffLine> Lines,
    bool HasChanges,
    int AddedCount,
    int RemovedCount);
