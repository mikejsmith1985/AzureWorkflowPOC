using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Smoke tests for PipelineEvent record equality and PipelineRunStatus enum values.
/// </summary>
public class PipelineEventTests
{
    [Fact]
    public void PipelineEvent_RecordEquality_ByValue()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var eventA = new PipelineEvent("Intake", "Normalized", ReportLevel.Success, timestamp);
        var eventB = new PipelineEvent("Intake", "Normalized", ReportLevel.Success, timestamp);

        Assert.Equal(eventA, eventB);
    }

    [Fact]
    public void PipelineRunStatus_AllValuesAreDefined()
    {
        var values = Enum.GetValues<PipelineRunStatus>();

        Assert.Contains(PipelineRunStatus.Running, values);
        Assert.Contains(PipelineRunStatus.AwaitingHuman, values);
        Assert.Contains(PipelineRunStatus.Complete, values);
        Assert.Contains(PipelineRunStatus.Blocked, values);
        Assert.Contains(PipelineRunStatus.Failed, values);
    }
}
