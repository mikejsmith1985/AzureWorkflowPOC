// Verifies that when a checkpoint manager is wired into the orchestrator (spec-019 T032), a MAF run
// persists checkpoints to the store as it executes — so a run paused at a HITL gate is recoverable.
using System.Text.Json;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Checkpointing;
using DBAIAzure.Tests.Parity;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests.Checkpointing;

/// <summary>
/// End-to-end check that the intake orchestrator checkpoints its MAF run when a
/// <see cref="CheckpointManager"/> is supplied: a not-ready ticket suspends at the clarification gate and
/// leaves durable checkpoints in the store for its run/session, ready to be resumed after a restart.
/// </summary>
public sealed class OrchestratorCheckpointingTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;

    public OrchestratorCheckpointingTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using var seed = new PipelineDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _keepAlive.Dispose();
    private static TicketState SampleTicket => new()
    {
        TicketId = "INC0001",
        Title = "Sample",
        Description = "Sample description.",
    };

    [Fact]
    public async Task PausedMafRun_PersistsCheckpointsForItsSession()
    {
        var chatClient = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"title\":\"Sample\",\"description\":\"Sample description.\"}", 40, 12),
            RecordedTurn.With("{\"is_ready\":false,\"missing_fields\":[\"target environment\"],\"reasoning\":\"missing env\"}", 30, 10),
            RecordedTurn.With("[\"What is the target environment?\"]", 35, 8),
        }, repeatLast: true);

        var checkpointManager = CheckpointManager.CreateJson(
            new EfCheckpointStore(new SharedFactory(_options)), new JsonSerializerOptions());

        var orchestrator = new PipelineOrchestrator(
            chatClient, checkpointManager: checkpointManager);

        var runId = orchestrator.StartRun(SampleTicket);
        await WaitForStatusAsync(orchestrator, runId, PipelineRunStatus.AwaitingHuman);

        await using var db = new PipelineDbContext(_options);
        var checkpointCount = await db.WorkflowCheckpoints.CountAsync(c => c.SessionId == runId);

        Assert.True(checkpointCount > 0, "the paused MAF run should have persisted at least one checkpoint");
    }

    private static async Task WaitForStatusAsync(
        PipelineOrchestrator orchestrator, string runId, PipelineRunStatus target)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (orchestrator.GetRun(runId)?.Status == target)
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException($"Run '{runId}' did not reach {target} in time.");
    }

    private sealed class SharedFactory(DbContextOptions<PipelineDbContext> options) : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }
}
