using DBAIAzure.Storage;
using DBAIAzure.Storage.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Verifies PipelineDbContext schema and basic EF model wiring against in-memory SQLite.
/// </summary>
public sealed class PipelineDbContextTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PipelineDbContext> _options;

    public PipelineDbContextTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task EnsureCreatedAsync_CreatesSchemaWithoutError()
    {
        using var context = new PipelineDbContext(_options);
        var wasCreated = await context.Database.EnsureCreatedAsync();
        Assert.True(wasCreated);
    }

    [Fact]
    public async Task Runs_DbSet_IsAccessible_WhenSchemaCreated()
    {
        using var context = new PipelineDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        Assert.Equal(0, await context.Runs.CountAsync());
    }

    [Fact]
    public async Task Snapshots_DbSet_IsAccessible_WhenSchemaCreated()
    {
        using var context = new PipelineDbContext(_options);
        await context.Database.EnsureCreatedAsync();
        Assert.Equal(0, await context.Snapshots.CountAsync());
    }

    [Fact]
    public async Task RunRecord_CanBeInsertedAndRetrieved()
    {
        using var context = new PipelineDbContext(_options);
        await context.Database.EnsureCreatedAsync();

        var run = new RunRecord
        {
            RunId     = "test-run-01",
            TicketId  = "INC0001",
            Title     = "Test ticket",
            Source    = "manual",
            Status    = "Running",
            StartedAt = DateTimeOffset.UtcNow,
        };
        context.Runs.Add(run);
        await context.SaveChangesAsync();

        var retrieved = await context.Runs.FindAsync("test-run-01");
        Assert.NotNull(retrieved);
        Assert.Equal("INC0001", retrieved!.TicketId);
    }

    [Fact]
    public async Task SnapshotCascadeDelete_RemovesSnapshotsWithRun()
    {
        using var context = new PipelineDbContext(_options);
        await context.Database.EnsureCreatedAsync();

        var run = new RunRecord
        {
            RunId     = "cascade-run",
            TicketId  = "INC0002",
            Title     = "Cascade test",
            Source    = "manual",
            Status    = "Complete",
            StartedAt = DateTimeOffset.UtcNow,
            Snapshots = [new StepSnapshotRecord
            {
                RunId      = "cascade-run",
                StepName   = "Validation",
                InputJson  = "{}",
                OutputJson = "{}",
                Timestamp  = DateTimeOffset.UtcNow,
            }],
        };
        context.Runs.Add(run);
        await context.SaveChangesAsync();

        context.Runs.Remove(run);
        await context.SaveChangesAsync();

        Assert.Equal(0, await context.Snapshots.CountAsync());
    }
}
