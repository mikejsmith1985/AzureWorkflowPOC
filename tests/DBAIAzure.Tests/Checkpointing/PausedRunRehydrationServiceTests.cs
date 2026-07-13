// Test for the startup rehydration service (spec-019 T032): after a restart it finds the persisted
// awaiting-human intake run, resumes it from its checkpoint via the orchestrator, and it completes on answer.
using System.Text.Json;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Checkpointing;
using DBAIAzure.Storage.Repositories;
using DBAIAzure.Tests.Parity;
using DBAIAzure.Web.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using Xunit;

namespace DBAIAzure.Tests.Checkpointing;

/// <summary>
/// Verifies the boot-time rehydration wiring end to end: one orchestrator runs a ticket to the clarification
/// gate (persisting an awaiting-human run + checkpoint) and "crashes"; the startup service — with a fresh
/// orchestrator over the same database — enumerates the paused run, rehydrates it from its checkpoint, and it
/// completes once the PO answers.
/// </summary>
public sealed class PausedRunRehydrationServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;
    private readonly SqliteRunRepository _repository;
    private readonly EfCheckpointStore _store;
    private readonly CheckpointManager _checkpointManager;

    public PausedRunRehydrationServiceTests()
    {
        // A shared-cache named in-memory database: each DbContext opens its OWN connection to the same data,
        // which supports the concurrent access from the orchestrators' background tasks (a single shared
        // connection object cannot). _keepAlive holds the database alive for the test's lifetime.
        var connectionString = $"Data Source=boot-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(connectionString).Options;
        using var seed = new PipelineDbContext(_options);
        seed.Database.EnsureCreated();

        var factory = new SharedFactory(_options);
        _repository = new SqliteRunRepository(factory);
        _store = new EfCheckpointStore(factory);
        _checkpointManager = CheckpointManager.CreateJson(_store, new JsonSerializerOptions());
    }

    public void Dispose() => _keepAlive.Dispose();

    private static Kernel StubKernel(IProgressReporter _) => Kernel.CreateBuilder().Build();

    private static TicketState SampleTicket => new() { TicketId = "INC0001", Title = "Sample", Description = "Sample description." };

    private PipelineOrchestrator NewOrchestrator(RecordedChatClient chatClient) =>
        new(StubKernel, repository: _repository, chatClient: chatClient, useMafRuntime: true, checkpointManager: _checkpointManager);

    [Fact]
    public async Task StartupService_RehydratesPausedRun_ThatCompletesOnAnswer()
    {
        var chatClient1 = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"title\":\"Sample\",\"description\":\"Sample description.\"}", 40, 12),
            RecordedTurn.With("{\"is_ready\":false,\"missing_fields\":[\"target environment\"],\"reasoning\":\"missing env\"}", 30, 10),
            RecordedTurn.With("[\"What is the target environment?\"]", 35, 8),
        }, repeatLast: true);
        var chatClient2 = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"is_ready\":true,\"missing_fields\":[],\"reasoning\":\"now clear\"}", 30, 8),
            RecordedTurn.With("{\"points\":5,\"reasoning\":\"comparable to the CRUD anchor\"}", 25, 8),
        }, repeatLast: true);

        // Process #1: run to the clarification gate (persists an AwaitingHuman run + checkpoint), then "crash".
        var runId = NewOrchestrator(chatClient1).StartRun(SampleTicket);
        await WaitForPersistedStatusAsync(runId, PipelineRunStatus.AwaitingHuman);

        // Process #2 (restart): the startup service rehydrates the paused run onto a fresh orchestrator.
        var orchestrator2 = NewOrchestrator(chatClient2);
        var service = new PausedRunRehydrationService(
            _repository, orchestrator2, _store, MafEnabledConfig(), NullLogger<PausedRunRehydrationService>.Instance);

        var rehydratedCount = await service.RehydrateAllPausedAsync();
        Assert.Equal(1, rehydratedCount);

        // Once resumed, the run recovers its awaiting-human state and completes on the PO's answer.
        await WaitForInMemoryStatusAsync(orchestrator2, runId, PipelineRunStatus.AwaitingHuman);
        orchestrator2.SubmitHitlAnswer(runId, "Azure production");
        var completed = await WaitForInMemoryStatusAsync(orchestrator2, runId, PipelineRunStatus.Complete);

        Assert.Equal(PipelineRunStatus.Complete, completed.Status);
        Assert.False(string.IsNullOrEmpty(completed.CurrentTicket?.JiraIssueUrl));
    }

    private async Task WaitForPersistedStatusAsync(string runId, PipelineRunStatus target)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var runs = await _repository.ListRunsAsync(status: target);
            if (runs.Any(r => r.RunId == runId))
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException($"Run '{runId}' was not persisted as {target} in time.");
    }

    private static async Task<PipelineRun> WaitForInMemoryStatusAsync(
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

    private static IConfiguration MafEnabledConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Maf:Enabled"] = "true" }).Build();

    private sealed class SharedFactory(DbContextOptions<PipelineDbContext> options) : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }
}
