using DBAIAzure.Storage.Entities;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Structural tests for StepSnapshotRecord — the EF entity that persists per-step state diffs.
/// </summary>
public class StepSnapshotRecordTests
{
    [Fact]
    public void StepSnapshotRecord_CanSetAllProperties()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var record = new StepSnapshotRecord
        {
            Id          = 42,
            RunId       = "abc12345",
            StepName    = "Validation",
            InputJson   = "{\"TicketId\":\"INC0001\"}",
            OutputJson  = "{\"TicketId\":\"INC0001\",\"ClarifyingQuestions\":[\"What is done?\"]}",
            Timestamp   = timestamp,
        };

        Assert.Equal(42, record.Id);
        Assert.Equal("abc12345", record.RunId);
        Assert.Equal("Validation", record.StepName);
        Assert.Equal(timestamp, record.Timestamp);
        Assert.Contains("ClarifyingQuestions", record.OutputJson);
    }
}
