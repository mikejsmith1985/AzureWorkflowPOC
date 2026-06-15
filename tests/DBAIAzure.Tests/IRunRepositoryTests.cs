using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Tests for IRunRepository contract and the NullRunRepository no-op implementation.
/// SqliteRunRepository integration is covered by SqliteRunRepositoryTests.
/// </summary>
public class IRunRepositoryTests
{
    private static readonly TicketState SampleTicket = new()
    {
        TicketId = "INC0001",
        Title    = "Sample ticket",
        Description = "A test description.",
    };

    [Fact]
    public async Task NullRunRepository_UpsertRunAsync_CompletesWithoutError()
    {
        var repository = NullRunRepository.Instance;
        await repository.UpsertRunAsync("run-001", SampleTicket, PipelineRunStatus.Running);
        // No exception = pass
    }

    [Fact]
    public async Task NullRunRepository_AddSnapshotAsync_CompletesWithoutError()
    {
        var repository = NullRunRepository.Instance;
        await repository.AddSnapshotAsync("run-001", "Validation", SampleTicket, SampleTicket);
    }

    [Fact]
    public async Task NullRunRepository_ListRunsAsync_ReturnsEmptyList()
    {
        var repository = NullRunRepository.Instance;
        var results = await repository.ListRunsAsync();
        Assert.Empty(results);
    }

    [Fact]
    public async Task NullRunRepository_GetRunAsync_ReturnsNull()
    {
        var repository = NullRunRepository.Instance;
        var detail = await repository.GetRunAsync("does-not-exist");
        Assert.Null(detail);
    }

    [Fact]
    public void NullRunRepository_IsSingleton()
    {
        Assert.Same(NullRunRepository.Instance, NullRunRepository.Instance);
    }
}
