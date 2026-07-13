// Tests for EfCheckpointStore (spec-019 T030/T025): the store round-trips checkpoints, and a run paused
// at a HITL gate resumes in place from a fresh store over the same database — the restart mechanic (SC-003).
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Checkpointing;
using DBAIAzure.Tests.Parity;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests.Checkpointing;

/// <summary>
/// Verifies the EF-backed checkpoint store: a create/retrieve/index round-trip, and — end to end — that a
/// phase-handler run paused at its approval gate can be resumed from its checkpoint by a brand-new store
/// and workflow instance over the same database, re-emitting the outstanding request (restart recovery).
/// </summary>
public sealed class EfCheckpointStoreTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;

    public EfCheckpointStoreTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using var seed = new PipelineDbContext(_options);
        seed.Database.EnsureCreated();
    }

    public void Dispose() => _keepAlive.Dispose();

    // A fresh store instance each time, all over the same in-memory database (simulates a process restart).
    private EfCheckpointStore NewStore() => new(new SharedFactory(_options));

    private static PhaseHandlerState SampleState => new()
    {
        RunId = "chk-run-1",
        FeatureKey = "001-sample-feature",
        FeatureDirectory = "specs/001-sample-feature",
        Phase = SpecKitPhase.Specify,
    };

    [Fact]
    public async Task CreateThenRetrieve_RoundTripsPayloadAndIndex()
    {
        var store = NewStore();
        var value = JsonSerializer.SerializeToElement(new { hello = "world" });

        var info = await store.CreateCheckpointAsync("sess", value, parent: null);
        var restored = await store.RetrieveCheckpointAsync("sess", info);
        var index = await store.RetrieveIndexAsync("sess", withParent: null);

        Assert.Equal("world", restored.GetProperty("hello").GetString());
        Assert.Contains(index, checkpoint => checkpoint.CheckpointId == info.CheckpointId);
    }

    [Fact]
    public async Task ChildCheckpoint_IsIndexedUnderItsParent()
    {
        var store = NewStore();
        var root = await store.CreateCheckpointAsync("sess", JsonSerializer.SerializeToElement(new { n = 1 }), null);
        var child = await store.CreateCheckpointAsync("sess", JsonSerializer.SerializeToElement(new { n = 2 }), root);

        var childrenOfRoot = await store.RetrieveIndexAsync("sess", withParent: root);

        Assert.Contains(childrenOfRoot, checkpoint => checkpoint.CheckpointId == child.CheckpointId);
        Assert.DoesNotContain(childrenOfRoot, checkpoint => checkpoint.CheckpointId == root.CheckpointId);
    }

    [Fact]
    public async Task PausedRun_ResumesFromCheckpoint_WithAFreshStore()
    {
        var chatClient = new RecordedChatClient(
            RecordedTurn.With("{\"summary\":\"The spec is clear.\",\"gaps\":[]}", 50, 20));
        // Drives only to the approval suspension, so no board-write deps are needed (writerDeps: default).
        Microsoft.Agents.AI.Workflows.Workflow BuildWorkflow() => MafPhaseHandlerWorkflowFactory.Build(
            chatClient, new FakeReader(), progressSink: null, bindingKeyMinter: null, default);
        const string session = "phase-chk-1";

        // Run #1: execute with EF checkpointing until it suspends at the approval gate; capture the checkpoint.
        var manager1 = CheckpointManager.CreateJson(NewStore(), JsonOptions);
        var run1 = await InProcessExecution.RunStreamingAsync(
            BuildWorkflow(), SampleState, manager1, session, default);

        CheckpointInfo? pausedCheckpoint = null;
        await foreach (var workflowEvent in run1.WatchStreamAsync(default))
        {
            if (workflowEvent is RequestInfoEvent)
            {
                pausedCheckpoint = run1.LastCheckpoint;
                break;
            }
        }
        Assert.NotNull(pausedCheckpoint);
        await run1.DisposeAsync();

        // Restart: a brand-new store + manager + workflow over the same database resume from the checkpoint
        // and re-emit the outstanding approval request — the run recovered its pause point (SC-003).
        var manager2 = CheckpointManager.CreateJson(NewStore(), JsonOptions);
        var run2 = await InProcessExecution.ResumeStreamingAsync(
            BuildWorkflow(), pausedCheckpoint!, manager2, default);

        ExternalRequest? recoveredRequest = null;
        await foreach (var workflowEvent in run2.WatchStreamAsync(default))
        {
            if (workflowEvent is RequestInfoEvent request)
            {
                recoveredRequest = request.Request;
                break;
            }
        }

        Assert.NotNull(recoveredRequest); // the outstanding approval request survived the "restart"
    }

    private sealed class FakeReader : IArtifactReader
    {
        public Task<IReadOnlyList<PhaseArtifact>> ReadArtifactsAsync(string featureDirectory, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PhaseArtifact>>(new[] { new PhaseArtifact { FileName = "spec.md", Content = "A sample spec." } });
    }

    private sealed class SharedFactory(DbContextOptions<PipelineDbContext> options) : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }
}
