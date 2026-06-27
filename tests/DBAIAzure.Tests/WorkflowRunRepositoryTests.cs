// Unit and integration tests for EfWorkflowRunRepository (T024-T027).
// Uses an in-memory SQLite connection held open for the lifetime of each test class
// to guarantee schema consistency across multiple DbContext operations.

using DBAIAzure.Core.Models;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class WorkflowRunRepositoryTests : IDisposable
{
    private static readonly Guid WorkflowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly EfWorkflowRunRepository _repo;

    public WorkflowRunRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var seed = new PipelineDbContext(options);
        seed.Database.EnsureCreated();

        _repo = new EfWorkflowRunRepository(new FixedFactory(options));
    }

    public void Dispose() => _connection.Dispose();

    // ── T024: CreateAsync / GetAsync round-trip ──────────────────────────────────

    [Fact]
    public async Task CreateAsync_ThenGetAsync_ReturnsMatchingRecord()
    {
        var run = MakeRun("run-001", WorkflowRunStatus.Running);
        await _repo.CreateAsync(run);

        var retrieved = await _repo.GetAsync("run-001");

        Assert.NotNull(retrieved);
        Assert.Equal("run-001", retrieved!.RunId);
        Assert.Equal(WorkflowId, retrieved.WorkflowId);
        Assert.Equal("Test Workflow", retrieved.WorkflowName);
        Assert.Equal(WorkflowRunStatus.Running, retrieved.Status);
        Assert.Equal("system", retrieved.TriggeredBy);
    }

    [Fact]
    public async Task GetAsync_UnknownId_ReturnsNull()
    {
        var result = await _repo.GetAsync("does-not-exist");
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_ExistingRun_UpdatesMutableFields()
    {
        var run = MakeRun("run-002", WorkflowRunStatus.Running);
        await _repo.CreateAsync(run);

        var updated = run with
        {
            Status = WorkflowRunStatus.Completed,
            CompletedAt = BaseTime.AddMinutes(5),
        };
        await _repo.UpdateAsync(updated);

        var retrieved = await _repo.GetAsync("run-002");
        Assert.Equal(WorkflowRunStatus.Completed, retrieved!.Status);
        Assert.NotNull(retrieved.CompletedAt);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_DoesNotThrow()
    {
        var ghost = MakeRun("ghost-run", WorkflowRunStatus.Completed);
        var ex = await Record.ExceptionAsync(() => _repo.UpdateAsync(ghost));
        Assert.Null(ex);
    }

    // ── T025: ListByStatusAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task ListByStatusAsync_Paused_ReturnsOnlyPausedRuns()
    {
        await _repo.CreateAsync(MakeRun("r-running", WorkflowRunStatus.Running));
        await _repo.CreateAsync(MakeRun("r-paused-1", WorkflowRunStatus.Paused));
        await _repo.CreateAsync(MakeRun("r-paused-2", WorkflowRunStatus.Paused,
            startedAt: BaseTime.AddSeconds(1)));
        await _repo.CreateAsync(MakeRun("r-completed", WorkflowRunStatus.Completed));

        var paused = await _repo.ListByStatusAsync(WorkflowRunStatus.Paused);

        Assert.Equal(2, paused.Count);
        Assert.All(paused, r => Assert.Equal(WorkflowRunStatus.Paused, r.Status));
    }

    [Fact]
    public async Task ListByStatusAsync_OrdersByStartedAtDescending()
    {
        await _repo.CreateAsync(MakeRun("r-old", WorkflowRunStatus.Paused, startedAt: BaseTime));
        await _repo.CreateAsync(MakeRun("r-new", WorkflowRunStatus.Paused,
            startedAt: BaseTime.AddHours(1)));

        var paused = await _repo.ListByStatusAsync(WorkflowRunStatus.Paused);

        Assert.Equal("r-new", paused[0].RunId);
        Assert.Equal("r-old",  paused[1].RunId);
    }

    [Fact]
    public async Task ListByStatusAsync_NoMatchingRuns_ReturnsEmptyList()
    {
        await _repo.CreateAsync(MakeRun("r-running", WorkflowRunStatus.Running));

        var paused = await _repo.ListByStatusAsync(WorkflowRunStatus.Paused);

        Assert.Empty(paused);
    }

    // ── T026: PurgeTerminalRunsOlderThanAsync ─────────────────────────────────────

    [Fact]
    public async Task PurgeTerminalRunsOlderThanAsync_RemovesStaleTerminalRuns()
    {
        var cutoff = BaseTime.AddDays(30);

        // Stale terminal runs — should be deleted.
        await _repo.CreateAsync(MakeRun("old-completed", WorkflowRunStatus.Completed,
            completedAt: BaseTime));
        await _repo.CreateAsync(MakeRun("old-failed", WorkflowRunStatus.Failed,
            completedAt: BaseTime));

        // Recent terminal run — should survive.
        await _repo.CreateAsync(MakeRun("new-completed", WorkflowRunStatus.Completed,
            completedAt: BaseTime.AddDays(60)));

        await _repo.PurgeTerminalRunsOlderThanAsync(cutoff);

        var all = await _repo.ListAsync();
        Assert.Single(all);
        Assert.Equal("new-completed", all[0].RunId);
    }

    [Fact]
    public async Task PurgeTerminalRunsOlderThanAsync_NeverTouchesPausedOrRunningRuns()
    {
        var cutoff = DateTimeOffset.UtcNow.AddYears(100); // far-future cutoff

        await _repo.CreateAsync(MakeRun("running", WorkflowRunStatus.Running));
        await _repo.CreateAsync(MakeRun("paused", WorkflowRunStatus.Paused));

        await _repo.PurgeTerminalRunsOlderThanAsync(cutoff);

        var all = await _repo.ListAsync();
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task PurgeTerminalRunsOlderThanAsync_AllStatuses_AreEligible()
    {
        var pastCutoff = BaseTime;
        var cutoff     = BaseTime.AddSeconds(1);

        foreach (var status in new[]
        {
            WorkflowRunStatus.Completed,
            WorkflowRunStatus.Failed,
            WorkflowRunStatus.TimedOut,
            WorkflowRunStatus.Cancelled,
        })
        {
            await _repo.CreateAsync(MakeRun($"r-{status}", status, completedAt: pastCutoff));
        }

        await _repo.PurgeTerminalRunsOlderThanAsync(cutoff);

        var remaining = await _repo.ListAsync();
        Assert.Empty(remaining);
    }

    // ── T027: Full round-trip (create → dispose context → re-create → assert) ────

    [Fact]
    public async Task RoundTrip_CreatePauseRetrieve_AfterContextRecreation()
    {
        var options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseSqlite(_connection)
            .Options;

        // Create with first factory instance.
        var factory1 = new FixedFactory(options);
        var repo1    = new EfWorkflowRunRepository(factory1);
        await repo1.CreateAsync(MakeRun("rt-001", WorkflowRunStatus.Running));

        // Transition to Paused via second factory instance.
        var factory2 = new FixedFactory(options);
        var repo2    = new EfWorkflowRunRepository(factory2);
        await repo2.UpdateAsync(MakeRun("rt-001", WorkflowRunStatus.Paused,
            suspendedAt: BaseTime.AddMinutes(1)));

        // Read back with third factory.
        var factory3 = new FixedFactory(options);
        var repo3    = new EfWorkflowRunRepository(factory3);
        var retrieved = await repo3.GetAsync("rt-001");

        Assert.NotNull(retrieved);
        Assert.Equal(WorkflowRunStatus.Paused, retrieved!.Status);
        Assert.NotNull(retrieved.SuspendedAt);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static WorkflowRunRecord MakeRun(
        string runId,
        WorkflowRunStatus status,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? suspendedAt = null) =>
        new(
            RunId:         runId,
            WorkflowId:    WorkflowId,
            WorkflowName:  "Test Workflow",
            Status:        status,
            TriggeredBy:   "system",
            StartedAt:     startedAt ?? BaseTime,
            SuspendedAt:   suspendedAt,
            ResumedAt:     null,
            CompletedAt:   completedAt,
            FailureReason: status == WorkflowRunStatus.Failed ? "test error" : null
        );

    private sealed class FixedFactory(DbContextOptions<PipelineDbContext> options)
        : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
        public Task<PipelineDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new PipelineDbContext(options));
    }
}
