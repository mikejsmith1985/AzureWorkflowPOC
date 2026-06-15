using DBAIAzure.Core.Models;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Integration tests for SqlitePhaseRunRepository using an in-memory SQLite database (same pattern as
/// SqliteRunRepositoryTests). Verifies durable upsert and the (FeatureKey, Phase) idempotency lookup.
/// </summary>
public sealed class SqlitePhaseRunRepositoryTests : IDisposable
{
    private readonly SqliteConnection _keepAliveConnection;
    private readonly SqlitePhaseRunRepository _repository;

    public SqlitePhaseRunRepositoryTests()
    {
        _keepAliveConnection = new SqliteConnection("Data Source=:memory:");
        _keepAliveConnection.Open();

        var options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseSqlite(_keepAliveConnection)
            .Options;

        using var seedContext = new PipelineDbContext(options);
        seedContext.Database.EnsureCreated();

        _repository = new SqlitePhaseRunRepository(new SharedConnectionFactory(options));
    }

    public void Dispose() => _keepAliveConnection.Dispose();

    private sealed class SharedConnectionFactory(DbContextOptions<PipelineDbContext> options)
        : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }

    private static PhaseHandlerState State(string runId, PhaseRunStatus status) => new()
    {
        RunId = runId,
        FeatureKey = "001-feature",
        FeatureDirectory = "specs/001-feature",
        Phase = SpecKitPhase.Specify,
        Status = status,
    };

    [Fact]
    public async Task Upsert_Insert_ThenLookupByRunId()
    {
        await _repository.UpsertRunAsync(State("run-1", PhaseRunStatus.AwaitingApproval));

        var view = await _repository.GetByRunIdAsync("run-1");

        Assert.NotNull(view);
        Assert.Equal("001-feature", view!.FeatureKey);
        Assert.Equal(SpecKitPhase.Specify, view.Phase);
        Assert.Equal(PhaseRunStatus.AwaitingApproval, view.Status);
    }

    [Fact]
    public async Task Upsert_Update_ChangesStatus_AndStoresWorkItems()
    {
        await _repository.UpsertRunAsync(State("run-2", PhaseRunStatus.AwaitingApproval));

        var completed = State("run-2", PhaseRunStatus.Completed) with
        {
            CreatedWorkItems =
            [
                new CreatedWorkItemRef { WorkItemId = 42, WorkItemType = "Epic", Url = "http://x/42" },
            ],
        };
        await _repository.UpsertRunAsync(completed);

        var view = await _repository.GetByFeaturePhaseAsync("001-feature", SpecKitPhase.Specify);

        Assert.NotNull(view);
        Assert.Equal(PhaseRunStatus.Completed, view!.Status);
        Assert.Single(view.CreatedWorkItems);
        Assert.Equal(42, view.CreatedWorkItems[0].WorkItemId);
    }

    [Fact]
    public async Task GetByFeaturePhase_ReturnsNull_WhenAbsent()
    {
        var view = await _repository.GetByFeaturePhaseAsync("nope", SpecKitPhase.Plan);
        Assert.Null(view);
    }

    [Fact]
    public async Task RepeatSignal_DifferentRunId_UpdatesSingleRecord_NoUniqueIndexViolation()
    {
        // First signal completes with a work item under run-A.
        var first = State("run-A", PhaseRunStatus.Completed) with
        {
            CreatedWorkItems =
            [
                new CreatedWorkItemRef { WorkItemId = 7, WorkItemType = "Epic", Url = "http://x/7" },
            ],
        };
        await _repository.UpsertRunAsync(first);

        // A repeat signal arrives under a NEW RunId — this must update the existing (feature, phase)
        // row rather than insert a second one (which would violate the unique index).
        await _repository.UpsertRunAsync(State("run-B", PhaseRunStatus.AwaitingApproval));

        // The prior work item id is preserved while the repeat run is still in progress (no items yet).
        var view = await _repository.GetByFeaturePhaseAsync("001-feature", SpecKitPhase.Specify);
        Assert.NotNull(view);
        Assert.Single(view!.CreatedWorkItems);
        Assert.Equal(7, view.CreatedWorkItems[0].WorkItemId);
    }

    [Fact]
    public async Task RepeatSignal_DifferentRunId_IsResolvableByNewRunId()
    {
        // First signal runs under run-A and completes.
        await _repository.UpsertRunAsync(State("run-A", PhaseRunStatus.Completed));

        // Repeat signal arrives under run-B — the portal URL uses run-B, so GetByRunIdAsync("run-B")
        // must resolve. If only the old RunId row survives, the detail page returns 404.
        await _repository.UpsertRunAsync(State("run-B", PhaseRunStatus.AwaitingApproval));

        var viewByNewId = await _repository.GetByRunIdAsync("run-B");
        Assert.NotNull(viewByNewId);
        Assert.Equal(PhaseRunStatus.AwaitingApproval, viewByNewId!.Status);
    }
}
