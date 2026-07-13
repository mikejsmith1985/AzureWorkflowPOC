// spec-019 T024/T027 (phase-handler resume): with the MAF runtime on, an approved phase run suspends at
// the approval RequestPort, resumes on the reviewer's decision, and the CreateWorkItemExecutor writes the
// board — behaviour-equivalent to the SK create-on-approval path, and gated so a rejection writes nothing.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Fakes;
using DBAIAzure.Tests.Parity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// End-to-end tests for the phase-handler pipeline on the MAF runtime: read → validate → suspend at the
/// approval gate → resume on the reviewer's decision → create the work item (only when approved — FR-006).
/// Model output is pinned by <see cref="RecordedChatClient"/>; the board is a <see cref="FakeBoardsClient"/>.
/// </summary>
public sealed class PhaseHandlerMafResumeTests
{
    // The structured validation result the MAF PhaseValidationExecutor binds; its summary flows into the
    // created work item's description, so it is the parity anchor for the board write.
    private const string ValidationJson = "{\"summary\":\"Epic body summary.\",\"gaps\":[]}";

    private static PhaseHandlerState State(SpecKitPhase phase = SpecKitPhase.Specify) => new()
    {
        RunId = Guid.NewGuid().ToString("N")[..8],
        FeatureKey = "001-feature",
        FeatureDirectory = "specs/001-feature",
        Phase = phase,
    };

    private static PhaseHandlerOrchestrator BuildMafOrchestrator(FakeBoardsClient boards)
    {
        var artifacts = new[] { new PhaseArtifact { FileName = "spec.md", Content = "x" } };
        var repository = new FakePhaseRunRepository();

        // The MAF path sources the create executor's board-write dependencies from the SK kernel's container,
        // so register the fake tracker/repository/artifact-reader/sink there exactly as the SK path would.
        Func<IPhaseProgressSink, Kernel> kernelFactory = sink =>
        {
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton<IArtifactReader>(new FakeArtifactReader(artifacts));
            builder.Services.AddSingleton<IWorkTrackerAdapter>(WorkTrackerAdapters.AdoAdapterFor(boards));
            builder.Services.AddSingleton<IPhaseRunRepository>(repository);
            builder.Services.AddSingleton(sink);
            return builder.Build();
        };

        var chatClient = new RecordedChatClient(new[] { RecordedTurn.With(ValidationJson, 50, 20) }, repeatLast: true);

        return new PhaseHandlerOrchestrator(
            kernelFactory,
            repository,
            chatClient: chatClient,
            artifactReader: new FakeArtifactReader(artifacts),
            useMafRuntime: true);
    }

    private static async Task WaitAsync(PhaseHandlerOrchestrator orchestrator, string runId, Func<PhaseHandlerRun, bool> done)
    {
        for (var attempt = 0; attempt < 300; attempt++)
        {
            var run = orchestrator.GetRun(runId);
            if (run is not null && done(run)) return;
            await Task.Delay(20);
        }
        throw new TimeoutException("Condition not met.");
    }

    [Fact]
    public async Task MafRuntime_ApprovedSpecify_SuspendsThenCreatesEpicOnResume()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = BuildMafOrchestrator(boards);

        var runId = orchestrator.StartRun(State());
        await WaitAsync(orchestrator, runId, run => run.State.Status == PhaseRunStatus.AwaitingApproval);
        Assert.Empty(boards.Creates); // nothing written before approval (FR-006)

        orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true, DecidedBy = "reviewer@example.com" });
        await WaitAsync(orchestrator, runId, run => run.State.Status == PhaseRunStatus.Completed);

        Assert.Single(boards.Creates);
        Assert.Equal(PhaseWorkItemMap.EpicType, boards.Creates[0].Type);
        Assert.Null(boards.Creates[0].ParentId);                       // Specify Epic is top-level
        Assert.Contains("Epic body summary.", boards.Creates[0].Description);
    }

    [Fact]
    public async Task MafRuntime_RejectedSpecify_ResumesButWritesNothing()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = BuildMafOrchestrator(boards);

        var runId = orchestrator.StartRun(State());
        await WaitAsync(orchestrator, runId, run => run.State.Status == PhaseRunStatus.AwaitingApproval);

        orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = false, DecidedBy = "reviewer@example.com" });
        await WaitAsync(orchestrator, runId, run => run.State.Status == PhaseRunStatus.Rejected);

        Assert.Empty(boards.Creates); // rejection writes nothing (FR-006)
    }
}
