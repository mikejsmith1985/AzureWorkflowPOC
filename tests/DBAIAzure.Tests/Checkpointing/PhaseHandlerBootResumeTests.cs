// Boot-resume test for the phase handler (spec-019 T032): a run paused at the approval gate before a restart
// is rehydrated by a brand-new orchestrator from its checkpoint, and a reviewer decision submitted after the
// "restart" drives it through to the board write.
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Checkpointing;
using DBAIAzure.Tests.Fakes;
using DBAIAzure.Tests.Parity;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DBAIAzure.Tests.Checkpointing;

/// <summary>
/// Simulates an application restart for the phase handler: one orchestrator runs a phase signal to the
/// approval gate (leaving a checkpoint), then a fresh orchestrator instance — with empty in-memory state,
/// over the same database — rehydrates the paused run from its checkpoint and writes the board once the
/// reviewer approves.
/// </summary>
public sealed class PhaseHandlerBootResumeTests : IDisposable
{
    private const string ValidationJson = "{\"summary\":\"Epic body summary.\",\"gaps\":[]}";

    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;
    private readonly EfCheckpointStore _store;
    private readonly CheckpointManager _checkpointManager;

    public PhaseHandlerBootResumeTests()
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

    private static PhaseHandlerState SampleState() => new()
    {
        RunId = Guid.NewGuid().ToString("N")[..8],
        FeatureKey = "001-feature",
        FeatureDirectory = "specs/001-feature",
        Phase = SpecKitPhase.Specify,
    };

    private PhaseHandlerOrchestrator NewOrchestrator(FakeBoardsClient boards, FakePhaseRunRepository repository)
    {
        var artifacts = new[] { new PhaseArtifact { FileName = "spec.md", Content = "x" } };
        var chatClient = new RecordedChatClient(new[] { RecordedTurn.With(ValidationJson, 50, 20) }, repeatLast: true);
        var writerDeps = new PhaseWorkItemWriterDeps(
            Tracker: WorkTrackerAdapters.AdoAdapterFor(boards), Repository: repository);

        return new PhaseHandlerOrchestrator(
            chatClient, new FakeArtifactReader(artifacts), writerDeps,
            repository, checkpointManager: _checkpointManager);
    }

    [Fact]
    public async Task PausedApproval_RehydratesFromCheckpoint_AndCreatesOnApprove()
    {
        var boards = new FakeBoardsClient();
        var repository = new FakePhaseRunRepository();

        // ── Process #1: run to the approval gate, then "crash" (leaving a checkpoint). ──
        var orchestrator1 = NewOrchestrator(boards, repository);
        var state = SampleState();
        var runId = orchestrator1.StartRun(state);
        await WaitAsync(orchestrator1, runId, run => run.State.Status == PhaseRunStatus.AwaitingApproval);

        // ── Process #2 (restart): fresh orchestrator, empty memory, same database + checkpoint store. ──
        var orchestrator2 = NewOrchestrator(boards, repository);
        Assert.Null(orchestrator2.GetRun(runId)); // nothing in memory after the "restart"

        var checkpoint = await _store.GetLatestCheckpointAsync(runId);
        Assert.NotNull(checkpoint);

        // Only the run identity is needed — the full paused state is recovered from the checkpoint.
        var placeholder = state with { Status = PhaseRunStatus.AwaitingApproval };
        orchestrator2.RehydratePausedRun(placeholder, checkpoint!);

        // The rehydrated run recovers its awaiting-approval state, then the approval drives the board write.
        await WaitAsync(orchestrator2, runId, run => run.State.Status == PhaseRunStatus.AwaitingApproval);
        Assert.Empty(boards.Creates); // nothing written before approval (FR-006)

        orchestrator2.SubmitApproval(runId, new ApprovalDecision { IsApproved = true, DecidedBy = "reviewer@example.com" });
        await WaitAsync(orchestrator2, runId, run => run.State.Status == PhaseRunStatus.Completed);

        Assert.Single(boards.Creates);
        Assert.Equal(PhaseWorkItemMap.EpicType, boards.Creates[0].Type);
        Assert.Contains("Epic body summary.", boards.Creates[0].Description);
    }

    private static async Task WaitAsync(PhaseHandlerOrchestrator orchestrator, string runId, Func<PhaseHandlerRun, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var run = orchestrator.GetRun(runId);
            if (run is not null && done(run)) return;
            await Task.Delay(25);
        }
        var last = orchestrator.GetRun(runId);
        throw new TimeoutException($"Condition not met. status={last?.State.Status}, reason={last?.State.FailureReason ?? "none"}");
    }

    private sealed class SharedFactory(DbContextOptions<PipelineDbContext> options) : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }
}
