using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Smoke tests for the IProgressReporter contract and ReportLevel enum values.
/// These guard against accidental renames that would break the pipeline steps.
/// </summary>
public class IProgressReporterTests
{
    [Fact]
    public void ReportLevel_AllValuesAreDefined()
    {
        Assert.Equal(0, (int)ReportLevel.Info);
        Assert.Equal(1, (int)ReportLevel.Success);
        Assert.Equal(2, (int)ReportLevel.Warning);
        Assert.Equal(3, (int)ReportLevel.Error);
    }

    [Fact]
    public void NullReporter_PatternDoesNotThrow()
    {
        // Guard clause used in every step: reporter?.ReportStep(...) must not throw when null
        IProgressReporter? reporter = null;
        var exception = Record.Exception(() =>
            reporter?.ReportStep("TestStep", "message", ReportLevel.Info));

        Assert.Null(exception);
    }
}
