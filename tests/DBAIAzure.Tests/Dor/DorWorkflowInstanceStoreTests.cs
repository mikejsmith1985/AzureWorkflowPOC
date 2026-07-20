// Integration-leaning unit tests for the DoR instance store (spec-021 T014): CRUD, the FR-004 idempotency
// guard (one active instance per ticket), ListActive, and the SLA due-queue — against real in-memory SQLite.
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DorWorkflowInstanceStoreTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly EfDorWorkflowInstanceStore _store;

    public DorWorkflowInstanceStoreTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        var options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using (var seed = new PipelineDbContext(options)) seed.Database.EnsureCreated();
        _store = new EfDorWorkflowInstanceStore(new SharedFactory(options));
    }

    public void Dispose() => _keepAlive.Dispose();

    private static DorWorkflowInstance New(string runId, string ticket, DorState state = DorState.Created) => new()
    {
        RunId = runId,
        TicketKey = ticket,
        State = state,
        StartedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task TryCreate_ThenGet_RoundTrips()
    {
        Assert.True(await _store.TryCreateAsync(New("r1", "SBRO-1")));

        var loaded = await _store.GetAsync("r1");

        Assert.NotNull(loaded);
        Assert.Equal("SBRO-1", loaded!.TicketKey);
        Assert.Equal(DorState.Created, loaded.State);
    }

    [Fact]
    public async Task TryCreate_SecondActiveInstanceForSameTicket_IsRejected()
    {
        Assert.True(await _store.TryCreateAsync(New("r1", "SBRO-1")));

        // A duplicate webhook for the same still-active ticket must not start a second instance (FR-004).
        Assert.False(await _store.TryCreateAsync(New("r2", "SBRO-1")));
    }

    [Fact]
    public async Task TryCreate_AfterPreviousInstanceIsDone_Succeeds()
    {
        await _store.TryCreateAsync(New("r1", "SBRO-1"));
        var done = New("r1", "SBRO-1", DorState.Done) with { CompletedAt = DateTimeOffset.UtcNow, Outcome = DorOutcome.Passed };
        await _store.UpdateAsync(done);

        // The filtered unique index excludes Done, so the ticket can be re-triggered later.
        Assert.True(await _store.TryCreateAsync(New("r2", "SBRO-1")));
    }

    [Fact]
    public async Task Update_PersistsStateAndOutcome()
    {
        await _store.TryCreateAsync(New("r1", "SBRO-1"));

        await _store.UpdateAsync(New("r1", "SBRO-1", DorState.AwaitingResponse) with
        {
            OutstandingGaps = new[] { "acceptance_criteria" },
            PrimaryIterations = 1,
        });

        var loaded = await _store.GetAsync("r1");
        Assert.Equal(DorState.AwaitingResponse, loaded!.State);
        Assert.Equal(new[] { "acceptance_criteria" }, loaded.OutstandingGaps);
        Assert.Equal(1, loaded.PrimaryIterations);
    }

    [Fact]
    public async Task ListActive_ExcludesTerminalInstances()
    {
        await _store.TryCreateAsync(New("r1", "SBRO-1", DorState.AwaitingResponse));
        await _store.TryCreateAsync(New("r2", "SBRO-2", DorState.Done));

        var active = await _store.ListActiveAsync();

        Assert.Single(active);
        Assert.Equal("r1", active[0].RunId);
    }

    [Fact]
    public async Task ListDueSla_ReturnsOnlyAwaitingInstancesPastDeadline()
    {
        var now = DateTimeOffset.UtcNow;
        await _store.TryCreateAsync(New("due", "SBRO-1", DorState.AwaitingResponse) with { SlaDeadlineAt = now.AddMinutes(-1) });
        await _store.TryCreateAsync(New("future", "SBRO-2", DorState.AwaitingResponse) with { SlaDeadlineAt = now.AddHours(1) });
        await _store.TryCreateAsync(New("noclock", "SBRO-3", DorState.Reviewing));

        var due = await _store.ListDueSlaAsync(now);

        Assert.Single(due);
        Assert.Equal("due", due[0].RunId);
    }

    private sealed class SharedFactory : IDbContextFactory<PipelineDbContext>
    {
        private readonly DbContextOptions<PipelineDbContext> _options;
        public SharedFactory(DbContextOptions<PipelineDbContext> options) => _options = options;
        public PipelineDbContext CreateDbContext() => new(_options);
    }
}
