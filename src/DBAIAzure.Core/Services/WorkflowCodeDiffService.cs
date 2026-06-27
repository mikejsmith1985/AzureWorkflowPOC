// Computes a compact line-level diff between two versions of generated workflow code,
// using DiffPlex for the underlying LCS algorithm and applying a ±3 context-line window.

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace DBAIAzure.Core.Services;

/// <summary>
/// Implements <see cref="IWorkflowCodeDiffService"/> using DiffPlex's inline diff builder.
/// After computing the full line diff, a second pass windows the result so only changed
/// lines and the 3 unchanged lines immediately before and after each hunk are included.
/// Adjacent hunks separated by 6 or fewer unchanged lines are merged into a single hunk.
/// </summary>
public sealed class WorkflowCodeDiffService : IWorkflowCodeDiffService
{
    /// <summary>Number of unchanged context lines shown before and after each change hunk.</summary>
    private const int ContextLines = 3;

    /// <inheritdoc/>
    public DiffResult ComputeDiff(string? previousCode, string? updatedCode)
    {
        var before = previousCode ?? string.Empty;
        var after  = updatedCode  ?? string.Empty;

        if (before == after)
            return new DiffResult(Array.Empty<DiffLine>(), HasChanges: false, AddedCount: 0, RemovedCount: 0);

        var inlineDiff = InlineDiffBuilder.Diff(before, after);

        // First pass: convert DiffPlex output to our domain type (no windowing yet).
        var allLines = new List<DiffLine>(inlineDiff.Lines.Count);
        foreach (var diffPieceLine in inlineDiff.Lines)
        {
            var type = diffPieceLine.Type switch
            {
                ChangeType.Inserted => DiffLineType.Added,
                ChangeType.Deleted  => DiffLineType.Removed,
                _                   => DiffLineType.Unchanged,
            };
            allLines.Add(new DiffLine(diffPieceLine.Text, type, IsContext: type == DiffLineType.Unchanged));
        }

        // Second pass: mark which unchanged lines fall within ContextLines of a changed line.
        var isNearChange = new bool[allLines.Count];
        for (var lineIndex = 0; lineIndex < allLines.Count; lineIndex++)
        {
            if (allLines[lineIndex].Type == DiffLineType.Unchanged)
                continue;

            // Fan out ContextLines positions in each direction from this changed line.
            var start = Math.Max(0, lineIndex - ContextLines);
            var end   = Math.Min(allLines.Count - 1, lineIndex + ContextLines);
            for (var contextIndex = start; contextIndex <= end; contextIndex++)
                isNearChange[contextIndex] = true;
        }

        // Third pass: collect only lines that are changed OR near a change; apply hunk merging.
        var compactLines   = new List<DiffLine>();
        var addedCount     = 0;
        var removedCount   = 0;

        for (var lineIndex = 0; lineIndex < allLines.Count; lineIndex++)
        {
            if (!isNearChange[lineIndex])
                continue;

            var line = allLines[lineIndex];
            compactLines.Add(line);

            if (line.Type == DiffLineType.Added)
                addedCount++;
            else if (line.Type == DiffLineType.Removed)
                removedCount++;
        }

        return new DiffResult(
            compactLines.AsReadOnly(),
            HasChanges: addedCount > 0 || removedCount > 0,
            AddedCount: addedCount,
            RemovedCount: removedCount);
    }
}
