using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Unit tests for BoundProgressReporter — verifies that step events reach
/// the PipelineRun queue and that the notify callback fires.
/// </summary>
public class BoundProgressReporterTests
{
    private static TicketState SampleTicket => new()
    {
        TicketId = "INC0001",
        Title = "Sample",
        Description = "Sample description.",
    };

    [Fact]
    public void ReportStep_AddsEventToRun_AndFiresCallback()
    {
        var run = new PipelineRun("abc12345", SampleTicket);
        var callbackCount = 0;
        var reporter = new BoundProgressReporter(run, () => callbackCount++);

        reporter.ReportStep("Validation", "DoR check passed", ReportLevel.Success);

        Assert.Single(run.Events);
        Assert.Equal(1, callbackCount);
        var evt = run.Events.TryPeek(out var peeked) ? peeked : null;
        Assert.NotNull(evt);
        Assert.Equal("Validation", evt!.StepName);
        Assert.Equal(ReportLevel.Success, evt.Level);
    }

    [Fact]
    public void ReportComplete_StoresFinalTicket_AndAddsSuccessEvent()
    {
        var run = new PipelineRun("abc12345", SampleTicket);
        var reporter = new BoundProgressReporter(run, () => { });
        var finalTicket = SampleTicket with
        {
            StoryPoints = 3,
            JiraIssueUrl = "https://jira.example.com/browse/SBRO-123",
        };

        reporter.ReportComplete(finalTicket);

        Assert.Equal(finalTicket, reporter.FinalTicket);
        Assert.Single(run.Events);
        Assert.Equal(ReportLevel.Success, run.Events.TryPeek(out var evt) ? evt!.Level : ReportLevel.Info);
    }

    [Fact]
    public void ReportStep_MultipleEvents_AllEnqueued()
    {
        var run = new PipelineRun("abc12345", SampleTicket);
        var reporter = new BoundProgressReporter(run, () => { });

        reporter.ReportStep("Intake", "Normalizing", ReportLevel.Info);
        reporter.ReportStep("Intake", "Normalized", ReportLevel.Success);
        reporter.ReportStep("Validation", "Running DoR check");

        Assert.Equal(3, run.Events.Count);
    }
}
