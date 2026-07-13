// Test for the startup rehydration service (spec-019 T032): after a restart it finds the persisted
// awaiting-human intake run, resumes it from its checkpoint via the orchestrator, and it completes on answer.
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Checkpointing;
using DBAIAzure.Storage.Repositories;
using DBAIAzure.Tests.Fakes;
using DBAIAzure.Tests.Parity;
using DBAIAzure.Web.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly SqlitePhaseRunRepository _phaseRepository;
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
        _phaseRepository = new SqlitePhaseRunRepository(factory);
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
            _repository, orchestrator2, _phaseRepository, StubPhaseOrchestrator(), _store,
            MafEnabledConfig(), NullLogger<PausedRunRehydrationService>.Instance);

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

    // ── Phase-handler rehydration wiring (spec-019 T032) ─────────────────────────────

    private const string PhaseValidationJson = "{\"summary\":\"Epic body summary.\",\"gaps\":[]}";

    private static PhaseHandlerState SamplePhaseState() => new()
    {
        RunId = Guid.NewGuid().ToString("N")[..8],
        FeatureKey = "001-feature",
        FeatureDirectory = "specs/001-feature",
        Phase = SpecKitPhase.Specify,
    };

    /// <summary>A minimal phase orchestrator for the intake-only test (its phase repository is empty).</summary>
    private static PhaseHandlerOrchestrator StubPhaseOrchestrator() =>
        new(_ => Kernel.CreateBuilder().Build());

    private PhaseHandlerOrchestrator NewPhaseOrchestrator(FakeBoardsClient boards)
    {
        var artifacts = new[] { new PhaseArtifact { FileName = "spec.md", Content = "x" } };
        Func<IPhaseProgressSink, Kernel> kernelFactory = sink =>
        {
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton<IArtifactReader>(new FakeArtifactReader(artifacts));
            builder.Services.AddSingleton<IWorkTrackerAdapter>(WorkTrackerAdapters.AdoAdapterFor(boards));
            builder.Services.AddSingleton<IPhaseRunRepository>(_phaseRepository);
            builder.Services.AddSingleton(sink);
            return builder.Build();
        };
        var chatClient = new RecordedChatClient(new[] { RecordedTurn.With(PhaseValidationJson, 50, 20) }, repeatLast: true);
        return new PhaseHandlerOrchestrator(
            kernelFactory, _phaseRepository, chatClient: chatClient,
            artifactReader: new FakeArtifactReader(artifacts), useMafRuntime: true, checkpointManager: _checkpointManager);
    }

    [Fact]
    public async Task StartupService_RehydratesPausedPhaseRun_ThatCreatesOnApprove()
    {
        var boards = new FakeBoardsClient();

        // Process #1: run to the approval gate (persists an AwaitingApproval run + checkpoint), then "crash".
        var orchestrator1 = NewPhaseOrchestrator(boards);
        var runId = orchestrator1.StartRun(SamplePhaseState());
        await WaitForPersistedPhaseStatusAsync(runId, PhaseRunStatus.AwaitingApproval);

        // Process #2 (restart): the startup service rehydrates the paused phase run onto a fresh orchestrator.
        var orchestrator2 = NewPhaseOrchestrator(boards);
        var service = new PausedRunRehydrationService(
            _repository, StubOrchestrator(), _phaseRepository, orchestrator2, _store,
            MafEnabledConfig(), NullLogger<PausedRunRehydrationService>.Instance);

        var rehydratedCount = await service.RehydrateAllPausedPhaseRunsAsync();
        Assert.Equal(1, rehydratedCount);

        // Once resumed, the run recovers its awaiting-approval state and writes the board on approval.
        await WaitForInMemoryPhaseStatusAsync(orchestrator2, runId, PhaseRunStatus.AwaitingApproval);
        orchestrator2.SubmitApproval(runId, new ApprovalDecision { IsApproved = true, DecidedBy = "reviewer@example.com" });
        await WaitForInMemoryPhaseStatusAsync(orchestrator2, runId, PhaseRunStatus.Completed);

        Assert.Single(boards.Creates);
        Assert.Contains("Epic body summary.", boards.Creates[0].Description);
    }

    /// <summary>A minimal intake orchestrator for the phase-only test (its intake repository is empty).</summary>
    private PipelineOrchestrator StubOrchestrator() =>
        new(StubKernel, repository: _repository, useMafRuntime: false);

    private async Task WaitForPersistedPhaseStatusAsync(string runId, PhaseRunStatus target)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var runs = await _phaseRepository.ListByStatusAsync(target);
            if (runs.Any(r => r.RunId == runId))
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException($"Phase run '{runId}' was not persisted as {target} in time.");
    }

    private static async Task WaitForInMemoryPhaseStatusAsync(
        PhaseHandlerOrchestrator orchestrator, string runId, PhaseRunStatus target)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var run = orchestrator.GetRun(runId);
            if (run is not null && run.State.Status == target)
            {
                return;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException($"Phase run '{runId}' did not reach {target} in time.");
    }

    private static IConfiguration MafEnabledConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Maf:Enabled"] = "true" }).Build();

    private sealed class SharedFactory(DbContextOptions<PipelineDbContext> options) : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }
}
