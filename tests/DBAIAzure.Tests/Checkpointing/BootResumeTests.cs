// Boot-resume test (spec-019 T032): a run paused before a restart is rehydrated by a brand-new orchestrator
// from its checkpoint, and a PO answer submitted after the "restart" drives it through to completion.
using System.Text.Json;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Checkpointing;
using DBAIAzure.Tests.Parity;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Xunit;

namespace DBAIAzure.Tests.Checkpointing;

/// <summary>
/// Simulates an application restart: one orchestrator runs a ticket to the clarification gate (leaving a
/// checkpoint), then a fresh orchestrator instance — with empty in-memory state, over the same database —
/// rehydrates the paused run from its checkpoint and completes it once the PO answers.
/// </summary>
public sealed class BootResumeTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;
    private readonly EfCheckpointStore _store;
    private readonly CheckpointManager _checkpointManager;

    public BootResumeTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using var seed = new PipelineDbContext(_options);
        seed.Database.EnsureCreated();
        _store = new EfCheckpointStore(new SharedFactory(_options));
        _checkpointManager = CheckpointManager.CreateJson(_store, new JsonSerializerOptions());
    }

    public void Dispose() => _keepAlive.Dispose();

    private static Kernel StubKernel(IProgressReporter _) => Kernel.CreateBuilder().Build();

    private static TicketState SampleTicket => new()
    {
        TicketId = "INC0001",
        Title = "Sample",
        Description = "Sample description.",
    };

    private PipelineOrchestrator NewOrchestrator(RecordedChatClient chatClient) =>
        new(StubKernel, chatClient: chatClient, useMafRuntime: true, checkpointManager: _checkpointManager);

    [Fact]
    public async Task PausedRun_RehydratesFromCheckpoint_AndCompletesOnAnswer()
    {
        // Process #1's model calls: normalise, decide not-ready, generate questions (then it pauses).
        var chatClient1 = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"title\":\"Sample\",\"description\":\"Sample description.\"}", 40, 12),      // intake
            RecordedTurn.With("{\"is_ready\":false,\"missing_fields\":[\"target environment\"],\"reasoning\":\"missing env\"}", 30, 10),
            RecordedTurn.With("[\"What is the target environment?\"]", 35, 8),                                // gap analysis
        }, repeatLast: true);

        // Process #2 resumes at the pause, so its FIRST model call is the re-validation after the answer.
        var chatClient2 = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"is_ready\":true,\"missing_fields\":[],\"reasoning\":\"now clear\"}", 30, 8), // validate after answer
            RecordedTurn.With("{\"points\":5,\"reasoning\":\"comparable to the CRUD anchor\"}", 25, 8),        // estimation
        }, repeatLast: true);

        // ── Process #1: run to the clarification gate, then "crash". ──
        var orchestrator1 = NewOrchestrator(chatClient1);
        var runId = orchestrator1.StartRun(SampleTicket);
        var paused = await WaitForStatusAsync(orchestrator1, runId, PipelineRunStatus.AwaitingHuman);
        var pausedTicket = paused.CurrentTicket!;

        // ── Process #2 (restart): fresh orchestrator, empty memory, same database + checkpoint store. ──
        var orchestrator2 = NewOrchestrator(chatClient2);
        Assert.Null(orchestrator2.GetRun(runId)); // nothing in memory after the "restart"

        var checkpoint = await _store.GetLatestCheckpointAsync(runId);
        Assert.NotNull(checkpoint);

        orchestrator2.RehydratePausedRun(pausedTicket, checkpoint!);

        // The rehydrated run recovers its awaiting-human state, then the answer drives it to completion.
        await WaitForStatusAsync(orchestrator2, runId, PipelineRunStatus.AwaitingHuman);
        orchestrator2.SubmitHitlAnswer(runId, "Azure production");

        var deadline = DateTime.UtcNow.AddSeconds(30);
        PipelineRun run2 = orchestrator2.GetRun(runId)!;
        while (DateTime.UtcNow < deadline)
        {
            run2 = orchestrator2.GetRun(runId)!;
            if (run2.Status is PipelineRunStatus.Complete or PipelineRunStatus.Failed)
            {
                break;
            }
            await Task.Delay(25);
        }

        Assert.True(run2.Status == PipelineRunStatus.Complete,
            $"Expected Complete; status={run2.Status}, questions={run2.CurrentTicket?.ClarifyingQuestions.Count}, url={run2.CurrentTicket?.JiraIssueUrl}");
        Assert.False(string.IsNullOrEmpty(run2.CurrentTicket?.JiraIssueUrl));
    }

    private static async Task<PipelineRun> WaitForStatusAsync(
        PipelineOrchestrator orchestrator, string runId, PipelineRunStatus target)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var run = orchestrator.GetRun(runId);
            if (run is not null && run.Status == target)
            {
                return run;
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
