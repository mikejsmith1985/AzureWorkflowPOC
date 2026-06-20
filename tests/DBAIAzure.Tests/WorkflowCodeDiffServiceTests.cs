// Tests for WorkflowCodeDiffService (Core.Services):
// - Added line tagged DiffLineType.Added
// - Identical inputs produce HasChanges=false with zero counts
// - ContextLines (3) of unchanged lines appear around a changed line
// - Removed line tagged DiffLineType.Removed
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Services;
using Xunit;

namespace DBAIAzure.Tests;

public class WorkflowCodeDiffServiceTests
{
    private readonly WorkflowCodeDiffService _service = new();

    // ── Tests ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ComputeDiff_ReturnsHasChangesFalse_WhenInputsAreIdentical()
    {
        const string code = "line one\nline two\nline three";

        var result = _service.ComputeDiff(code, code);

        Assert.False(result.HasChanges);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(0, result.RemovedCount);
    }

    [Fact]
    public void ComputeDiff_TagsNewLine_AsAdded()
    {
        const string before = "line one\nline two";
        const string after  = "line one\nline two\nline three";

        var result = _service.ComputeDiff(before, after);

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.AddedCount);
        Assert.Contains(result.Lines, line => line.Type == DiffLineType.Added && line.Content == "line three");
    }

    [Fact]
    public void ComputeDiff_TagsDeletedLine_AsRemoved()
    {
        const string before = "keep\ndelete me\nkeep";
        const string after  = "keep\nkeep";

        var result = _service.ComputeDiff(before, after);

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.RemovedCount);
        Assert.Contains(result.Lines, line => line.Type == DiffLineType.Removed && line.Content == "delete me");
    }

    [Fact]
    public void ComputeDiff_IncludesContextLines_AroundChange()
    {
        // Arrange: 10 lines — change is on line 8; expect ContextLines (3) on each side.
        var beforeLines = Enumerable.Range(1, 10).Select(lineIndex => $"line {lineIndex}").ToArray();
        var afterLines  = beforeLines.ToArray();
        afterLines[7] = "changed line"; // line 8 (0-indexed: 7)

        var before = string.Join("\n", beforeLines);
        var after  = string.Join("\n", afterLines);

        var result = _service.ComputeDiff(before, after);

        // At least 3 context lines before and after the change must appear.
        var contextCount = result.Lines.Count(line => line.IsContext);
        Assert.True(contextCount >= 3, $"Expected >= 3 context lines, got {contextCount}");
    }

    [Fact]
    public void ComputeDiff_ReturnsEmptyResult_WhenBothInputsAreNull()
    {
        var result = _service.ComputeDiff(null, null);

        Assert.False(result.HasChanges);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void ComputeDiff_ReturnsEmptyResult_WhenBothInputsAreEmpty()
    {
        var result = _service.ComputeDiff(string.Empty, string.Empty);

        Assert.False(result.HasChanges);
    }

    [Fact]
    public void ComputeDiff_TreatsNullAndEmptyString_AsSameContent()
    {
        var resultA = _service.ComputeDiff(null, string.Empty);
        var resultB = _service.ComputeDiff(string.Empty, null);

        Assert.False(resultA.HasChanges);
        Assert.False(resultB.HasChanges);
    }
}
