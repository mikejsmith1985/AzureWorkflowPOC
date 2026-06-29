// Unit tests for WorkItemRef — numeric (ADO) and string-key (Jira) identities round-trip correctly.
using DBAIAzure.Core.Models.WorkTracker;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

public sealed class WorkItemRefTests
{
    [Fact]
    public void From_Int_RoundTrips()
    {
        var reference = WorkItemRef.From(4242);
        Assert.Equal("4242", reference.Value);
        Assert.True(reference.TryAsInt(out var id));
        Assert.Equal(4242, id);
    }

    [Fact]
    public void StringKey_IsNotNumeric()
    {
        var reference = new WorkItemRef("PROJ-123");
        Assert.False(reference.TryAsInt(out _));
        Assert.Equal("PROJ-123", reference.Value);
    }
}
