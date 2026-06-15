using DBAIAzure.Storage.Entities;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Structural tests for RunRecord — the EF Core entity that persists pipeline runs.
/// Full persistence behaviour is covered by SqliteRunRepositoryTests.
/// </summary>
public class RunRecordTests
{
    [Fact]
    public void RunRecord_DefaultConstruction_HasExpectedDefaults()
    {
        var record = new RunRecord();

        Assert.Equal(string.Empty, record.RunId);
        Assert.Equal(string.Empty, record.TicketId);
        Assert.Equal(string.Empty, record.Title);
        Assert.Equal("manual", record.Source);   // default source for manually submitted tickets
        Assert.NotNull(record.Snapshots);
        Assert.Empty(record.Snapshots);
    }

    [Fact]
    public void RunRecord_CanSetAllProperties()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var record = new RunRecord
        {
            RunId        = "abc12345",
            TicketId     = "INC0001234",
            Title        = "Test ticket title",
            Source       = "servicenow",
            SnowNumber   = "INC0001234",
            SnowPriority = "2",
            Status       = "Running",
            StoryPoints  = 8,
            StartedAt    = timestamp,
            CompletedAt  = timestamp.AddMinutes(5),
        };

        Assert.Equal("abc12345", record.RunId);
        Assert.Equal("INC0001234", record.TicketId);
        Assert.Equal("servicenow", record.Source);
        Assert.Equal(8, record.StoryPoints);
        Assert.Equal(timestamp, record.StartedAt);
    }
}
