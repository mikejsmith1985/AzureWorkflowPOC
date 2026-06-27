using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Integration tests for SqliteRunRepository using an in-memory SQLite database.
/// A SqliteConnection is held open for the lifetime of each test instance so that
/// the in-memory database persists across multiple DbContext operations.
/// </summary>
public sealed class SqliteRunRepositoryTests : IDisposable
{
    private static readonly TicketState BaseTicket = new()
    {
        TicketId    = "INC9999",
        Title       = "Repository integration test ticket",
        Description = "Verifies that SqliteRunRepository persists and retrieves correctly.",
    };

    private readonly SqliteConnection _keepAliveConnection;
    private readonly IDbContextFactory<PipelineDbContext> _factory;
    private readonly SqliteRunRepository _repository;

    public SqliteRunRepositoryTests()
    {
        // Shared in-memory connection — the database lives as long as this connection is open.
        _keepAliveConnection = new SqliteConnection("Data Source=:memory:");
        _keepAliveConnection.Open();

        var options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseSqlite(_keepAliveConnection)
            .Options;

        using var seedContext = new PipelineDbContext(options);
        seedContext.Database.EnsureCreated();

        _factory    = new SharedConnectionFactory(options);
        _repository = new SqliteRunRepository(_factory);
    }

    public void Dispose() => _keepAliveConnection.Dispose();

    // Wraps a pre-built options object so all contexts share the same in-memory database.
    private sealed class SharedConnectionFactory(DbContextOptions<PipelineDbContext> options)
        : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }

    [Fact]
    public async Task UpsertRunAsync_PersistsNewRun()
    {
        await _repository.UpsertRunAsync("run-001", BaseTicket, PipelineRunStatus.Running);

        var detail = await _repository.GetRunAsync("run-001");
        Assert.NotNull(detail);
        Assert.Equal("run-001", detail!.Summary.RunId);
        Assert.Equal(PipelineRunStatus.Running, detail.Summary.Status);
    }

    [Fact]
    public async Task UpsertRunAsync_UpdatesExistingRunStatus()
    {
        await _repository.UpsertRunAsync("run-002", BaseTicket, PipelineRunStatus.Running);
        var completedTicket = BaseTicket with { StoryPoints = 5 };
        await _repository.UpsertRunAsync("run-002", completedTicket, PipelineRunStatus.Complete);

        var detail = await _repository.GetRunAsync("run-002");
        Assert.Equal(PipelineRunStatus.Complete, detail!.Summary.Status);
        Assert.Equal(5, detail.Summary.StoryPoints);
    }

    [Fact]
    public async Task AddSnapshotAsync_PersistsStepSnapshot()
    {
        await _repository.UpsertRunAsync("run-003", BaseTicket, PipelineRunStatus.Running);
        var afterTicket = BaseTicket with { ClarifyingQuestions = ["What is the acceptance criteria?"] };
        await _repository.AddSnapshotAsync("run-003", "Validation", BaseTicket, afterTicket);

        var detail = await _repository.GetRunAsync("run-003");
        Assert.NotNull(detail);
        Assert.Single(detail!.Snapshots);
        Assert.Equal("Validation", detail.Snapshots[0].StepName);
    }

    [Fact]
    public async Task ListRunsAsync_ReturnsAllPersistedRuns()
    {
        await _repository.UpsertRunAsync("run-list-a", BaseTicket, PipelineRunStatus.Complete);
        await _repository.UpsertRunAsync("run-list-b",
            BaseTicket with { TicketId = "INC8888" }, PipelineRunStatus.Running);

        var results = await _repository.ListRunsAsync();
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ListRunsAsync_FiltersByStatus()
    {
        await _repository.UpsertRunAsync("run-fa", BaseTicket, PipelineRunStatus.Complete);
        await _repository.UpsertRunAsync("run-fb", BaseTicket, PipelineRunStatus.Running);

        var completedOnly = await _repository.ListRunsAsync(status: PipelineRunStatus.Complete);
        Assert.Single(completedOnly);
        Assert.Equal("run-fa", completedOnly[0].RunId);
    }

    [Fact]
    public async Task GetRunAsync_ReturnsNullForUnknownRunId()
    {
        var detail = await _repository.GetRunAsync("non-existent-run-id");
        Assert.Null(detail);
    }

    [Fact]
    public async Task AddSnapshotAsync_MultipleCalls_AllPersist()
    {
        await _repository.UpsertRunAsync("run-multi", BaseTicket, PipelineRunStatus.Running);
        await _repository.AddSnapshotAsync("run-multi", "Intake", BaseTicket, BaseTicket);
        await _repository.AddSnapshotAsync("run-multi", "Validation", BaseTicket, BaseTicket);

        var detail = await _repository.GetRunAsync("run-multi");
        Assert.Equal(2, detail!.Snapshots.Count);
    }
}
