// Defines the contract for computing a compact line-level diff between two code versions.

using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

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
