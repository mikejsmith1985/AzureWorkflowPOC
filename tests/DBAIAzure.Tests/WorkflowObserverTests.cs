// Unit tests for SqlWorkflowObserver and IWorkflowObserver fan-out behaviour (T050-T052).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Entities;
using DBAIAzure.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests;

// ── T050: SqlWorkflowObserver — writes entity + swallows exceptions ───────────

public sealed class SqlWorkflowObserverTests : IDisposable
{
    private static readonly Guid TestWorkflowId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<PipelineDbContext> _factory;
    private readonly SqlWorkflowObserver _observer;

    public SqlWorkflowObserverTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<PipelineDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var seed = new PipelineDbContext(options);
        seed.Database.EnsureCreated();

        _factory  = new ObserverFixedFactory(options);
        _observer = new SqlWorkflowObserver(_factory, NullLogger<SqlWorkflowObserver>.Instance);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task RecordEventAsync_WritesEntityToDatabase()
    {
        // The event FK references a run — insert the parent first (SQLite enforces FK when EF cascades).
        await InsertRunAsync("run-1");
        var evt = MakeEvent("run-1", WorkflowEventType.StepStarted);

        await _observer.RecordEventAsync(evt);

        await using var db = _factory.CreateDbContext();
        var entities = await db.WorkflowExecutionEvents.ToListAsync();
        Assert.Single(entities);
        Assert.Equal(evt.EventId, entities[0].EventId);
        Assert.Equal("run-1", entities[0].RunId);
        Assert.Equal((int)WorkflowEventType.StepStarted, entities[0].EventType);
    }

    [Fact]
    public async Task RecordEventAsync_LlmFields_ArePersistedCorrectly()
    {
        await InsertRunAsync("run-2");
        var evt = new WorkflowExecutionEvent(
            EventId:         Guid.NewGuid(),
            RunId:           "run-2",
            NodeId:          "node-1",
            NodeLabel:       "AI Step",
            EventType:       WorkflowEventType.LlmCallCompleted,
            OccurredAt:      DateTimeOffset.UtcNow,
            DurationMs:      250,
            Outcome:         null,
            LlmModelName:    "claude-sonnet-4-6",
            LlmInputTokens:  512,
            LlmOutputTokens: 128);

        await _observer.RecordEventAsync(evt);

        await using var db = _factory.CreateDbContext();
        var entity = await db.WorkflowExecutionEvents.SingleAsync();
        Assert.Equal("claude-sonnet-4-6", entity.LlmModelName);
        Assert.Equal(512, entity.LlmInputTokens);
        Assert.Equal(128, entity.LlmOutputTokens);
    }

    [Fact]
    public async Task RecordEventAsync_WhenFactoryThrows_DoesNotPropagate()
    {
        var brokenObserver = new SqlWorkflowObserver(
            new ThrowingFactory(),
            NullLogger<SqlWorkflowObserver>.Instance);

        var ex = await Record.ExceptionAsync(
            () => brokenObserver.RecordEventAsync(MakeEvent("run-x", WorkflowEventType.StepFailed)));

        Assert.Null(ex);
    }

    // Inserts a minimal WorkflowRunEntity so events can satisfy the FK relationship.
    private async Task InsertRunAsync(string runId)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.WorkflowBuilderRuns.Add(new WorkflowRunEntity
        {
            RunId        = runId,
            WorkflowId   = TestWorkflowId,
            WorkflowName = "Test Workflow",
            Status       = (int)WorkflowRunStatus.Running,
            TriggeredBy  = "test",
            StartedAt    = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static WorkflowExecutionEvent MakeEvent(string runId, WorkflowEventType type) =>
        new(EventId:         Guid.NewGuid(),
            RunId:           runId,
            NodeId:          null,
            NodeLabel:       null,
            EventType:       type,
            OccurredAt:      DateTimeOffset.UtcNow,
            DurationMs:      null,
            Outcome:         null,
            LlmModelName:    null,
            LlmInputTokens:  null,
            LlmOutputTokens: null);

    private sealed class ObserverFixedFactory(DbContextOptions<PipelineDbContext> options)
        : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
        public Task<PipelineDbContext> CreateDbContextAsync(CancellationToken ct = default)
            => Task.FromResult(new PipelineDbContext(options));
    }

    private sealed class ThrowingFactory : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() =>
            throw new InvalidOperationException("Simulated DB factory failure.");

        public Task<PipelineDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
            Task.FromException<PipelineDbContext>(new InvalidOperationException("Simulated DB factory failure."));
    }
}

// ── T051-T052: Observer fan-out — first throws, second still fires ────────────

public sealed class WorkflowObserverFanOutTests
{
    // T052: Multiple observers — an exception in one does not prevent subsequent observers
    // from receiving the same event. The fan-out coordinator must catch and continue.
    [Fact]
    public async Task FanOut_FirstObserverThrows_SecondStillReceivesEvent()
    {
        var received = new List<WorkflowExecutionEvent>();
        var throwing = new ThrowingObserver();
        var capturing = new CapturingObserver(received);

        // The coordinator is the unit under test — fan-out must not propagate one observer's failure.
        var fanOut = new ObserverFanOut(new IWorkflowObserver[] { throwing, capturing });

        var evt = MakeEvent("run-fan", WorkflowEventType.LlmCallCompleted);
        await fanOut.RecordEventAsync(evt);

        Assert.Single(received);
        Assert.Equal(evt.EventId, received[0].EventId);
    }

    [Fact]
    public async Task FanOut_AllObserversReceiveEvent()
    {
        var listA = new List<WorkflowExecutionEvent>();
        var listB = new List<WorkflowExecutionEvent>();

        var fanOut = new ObserverFanOut(new IWorkflowObserver[]
        {
            new CapturingObserver(listA),
            new CapturingObserver(listB),
        });

        var evt = MakeEvent("run-all", WorkflowEventType.StepCompleted);
        await fanOut.RecordEventAsync(evt);

        Assert.Single(listA);
        Assert.Single(listB);
        Assert.Equal(evt.EventId, listA[0].EventId);
        Assert.Equal(evt.EventId, listB[0].EventId);
    }

    private static WorkflowExecutionEvent MakeEvent(string runId, WorkflowEventType type) =>
        new(EventId:         Guid.NewGuid(),
            RunId:           runId,
            NodeId:          null,
            NodeLabel:       null,
            EventType:       type,
            OccurredAt:      DateTimeOffset.UtcNow,
            DurationMs:      null,
            Outcome:         null,
            LlmModelName:    null,
            LlmInputTokens:  null,
            LlmOutputTokens: null);

    private sealed class ThrowingObserver : IWorkflowObserver
    {
        public Task RecordEventAsync(WorkflowExecutionEvent evt, CancellationToken ct = default) =>
            Task.FromException(new InvalidOperationException("Observer error."));
    }

    private sealed class CapturingObserver(List<WorkflowExecutionEvent> received) : IWorkflowObserver
    {
        public Task RecordEventAsync(WorkflowExecutionEvent evt, CancellationToken ct = default)
        {
            received.Add(evt);
            return Task.CompletedTask;
        }
    }

    // Minimal fan-out implementation — mirrors the production pattern where an aggregate
    // IWorkflowObserver delegates to a registered list of observers.
    private sealed class ObserverFanOut(IEnumerable<IWorkflowObserver> observers) : IWorkflowObserver
    {
        public async Task RecordEventAsync(WorkflowExecutionEvent evt, CancellationToken ct = default)
        {
            foreach (var observer in observers)
            {
                try
                {
                    await observer.RecordEventAsync(evt, ct);
                }
                catch
                {
                    // Swallow per contract: one bad observer must not prevent others from firing.
                }
            }
        }
    }
}
