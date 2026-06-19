// Defines the data model for representing a code diff, used to drive diff-highlight rendering in the chat panel.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Classifies a single line in a diff as unchanged, newly added, or removed —
/// so the chat panel renderer knows which colour to apply to each line.
/// </summary>
public enum DiffLineKind
{
    /// <summary>The line exists in both the before and after versions — no change.</summary>
    Unchanged,

    /// <summary>The line was introduced in the after version — render as an addition.</summary>
    Added,

    /// <summary>The line was present in the before version but is absent from the after version — render as a deletion.</summary>
    Removed,
}

/// <summary>
/// Represents a single line in a code diff, pairing the line number and change
/// classification with the raw text content so the chat panel can render each
/// line with the correct highlight and gutter annotation.
/// </summary>
/// <param name="LineNumber">The 1-based line number within the diff context.</param>
/// <param name="Kind">Whether this line is unchanged, added, or removed.</param>
/// <param name="Content">The raw text content of the line, without a trailing newline.</param>
public sealed record DiffLine(int LineNumber, DiffLineKind Kind, string Content);

/// <summary>
/// Holds the complete set of annotated lines that make up a code diff.
/// This record is the contract between the diff-computation service and the
/// chat panel UI component: the panel iterates <see cref="Lines"/> in order
/// and applies per-line highlight colours based on each line's <see cref="DiffLineKind"/>.
/// </summary>
/// <param name="Lines">
/// An ordered, read-only list of <see cref="DiffLine"/> values representing
/// every line in the diff from top to bottom.
/// </param>
public sealed record CodeDiff(IReadOnlyList<DiffLine> Lines);
