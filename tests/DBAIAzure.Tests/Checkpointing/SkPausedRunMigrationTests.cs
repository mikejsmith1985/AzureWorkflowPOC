// Tests for the SK-paused-run migration (spec-019 T033 / SC-009): a representative paused clarification
// run is converted to a MAF checkpoint, the migration is idempotent, and the migrated run resumes from its
// pause point — the reviewer's answer drives it through the real validation loop to completion.
using System.Text.Json;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Checkpointing;
using DBAIAzure.Tests.Parity;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Checkpointing;

/// <summary>
/// Verifies the one-time SK-paused-run migration: a paused clarification ticket is converted to a durable
/// MAF checkpoint, re-running the migration skips it (idempotent), and resuming from the checkpoint recovers
/// the outstanding clarification request and — on the answer — completes through the real intake loop.
/// </summary>
public sealed class SkPausedRunMigrationTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;
    private readonly CheckpointManager _checkpointManager;

    public SkPausedRunMigrationTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using var seed = new PipelineDbContext(_options);
        seed.Database.EnsureCreated();
        _checkpointManager = CheckpointManager.CreateJson(
            new EfCheckpointStore(new SharedFactory(_options)), new JsonSerializerOptions());
    }

    public void Dispose() => _keepAlive.Dispose();

    // A ticket paused for clarification under SK: it already carries its questions and its round count.
    private static TicketState PausedTicket => new()
    {
        TicketId = "INC0009",
        Title = "Sample",
        Description = "Sample description.",
        ClarifyingQuestions = new[] { "What is the target environment?" },
        ClarificationRound = 0,
    };

    // On resume, validation re-runs (now ready) and estimation runs; migration itself makes no model call.
    private static RecordedChatClient ResumeChatClient() => new(new[]
    {
        RecordedTurn.With("{\"is_ready\":true,\"missing_fields\":[],\"reasoning\":\"now clear\"}", 30, 8),
        RecordedTurn.With("{\"points\":5,\"reasoning\":\"comparable to the CRUD anchor\"}", 25, 8),
    }, repeatLast: true);

    [Fact]
    public async Task Migrate_ConvertsIdempotently_AndResumesToCompletion()
    {
        const string sessionId = "paused-run-1";
        var chatClient = ResumeChatClient();
        var migration = new SkPausedRunMigration(
            new SharedFactory(_options), _checkpointManager, NullLogger<SkPausedRunMigration>.Instance);

        // 1) Migrate the paused run → a checkpoint at the clarification gate is written (no model call).
        var first = await migration.MigrateAsync(
            sessionId, MafIntakeWorkflowFactory.BuildResumeWorkflow(chatClient), PausedTicket);
        Assert.Equal(SkPausedRunMigrationOutcome.Migrated, first.Outcome);
        Assert.NotNull(first.Checkpoint);

        // 2) Idempotent: re-running the migration for the same run skips it.
        var second = await migration.MigrateAsync(
            sessionId, MafIntakeWorkflowFactory.BuildResumeWorkflow(chatClient), PausedTicket);
        Assert.Equal(SkPausedRunMigrationOutcome.AlreadyMigrated, second.Outcome);

        // 3) Resume from the checkpoint (as a restart would): the outstanding clarification request recovers.
        var session = await MafWorkflowSession<TicketState>.ResumeAsync(
            MafIntakeWorkflowFactory.BuildResumeWorkflow(chatClient), first.Checkpoint!, _checkpointManager, default);
        var suspended = await session.DriveAsync(default);
        Assert.True(suspended.Suspended);
        Assert.True(suspended.PendingRequest!.Request.TryGetDataAs<TicketState>(out var recovered) && recovered is not null);
        Assert.NotEmpty(recovered!.ClarifyingQuestions); // the questions the reviewer must answer survived

        // 4) The reviewer answers → the run re-validates (now ready) and completes through the real loop.
        var answered = recovered with
        {
            HumanAnswer = "Azure production",
            ClarificationRound = recovered.ClarificationRound + 1,
            ClarifyingQuestions = Array.Empty<string>(),
        };
        await session.RespondAsync(suspended.PendingRequest.Request, answered, default);

        var completed = await session.DriveAsync(default);
        Assert.False(completed.Suspended);
        Assert.NotNull(completed.Output);
        Assert.False(string.IsNullOrEmpty(completed.Output!.JiraIssueUrl)); // reached the terminal Action executor
    }

    private sealed class SharedFactory(DbContextOptions<PipelineDbContext> options) : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }
}
