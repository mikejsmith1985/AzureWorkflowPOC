using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Tests for ReadArtifactsStep behaviour, exercised through the real SK process so the step's
/// event routing is genuinely covered: artifacts present → the run advances and pauses for
/// approval; missing/empty artifacts → the run is recorded Failed with a reason (FR-003).
/// </summary>
public class ReadArtifactsStepTests
{
    private static PhaseHandlerState State() => new()
    {
        RunId = Guid.NewGuid().ToString("N")[..8],
        FeatureKey = "001-feature",
        FeatureDirectory = "specs/001-feature",
        Phase = SpecKitPhase.Specify,
    };

    private static PhaseHandlerOrchestrator BuildOrchestrator(IArtifactReader reader, FakeBoardsClient boards)
    {
        var validation = new PhaseValidationResult { Summary = "s", Gaps = [] };
        Func<IPhaseProgressSink, Kernel> factory = sink =>
        {
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(reader);
            builder.Services.AddSingleton<IStructuredCompletionService>(new FakeStructuredCompletionService(validation));
            builder.Services.AddSingleton<IBoardsClient>(boards);
            builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IWorkTrackerAdapter>(WorkTrackerAdapters.AdoAdapterFor(boards));
            builder.Services.AddSingleton(sink);
            return builder.Build();
        };
        return new PhaseHandlerOrchestrator(factory);
    }

    private static async Task<PhaseHandlerRun> WaitForAsync(
        PhaseHandlerOrchestrator orchestrator, string runId, Func<PhaseHandlerRun, bool> predicate)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var run = orchestrator.GetRun(runId)!;
            if (predicate(run)) return run;
            await Task.Delay(10);
        }
        throw new TimeoutException("Condition not met.");
    }

    [Fact]
    public async Task ArtifactsPresent_AdvancesPastRead()
    {
        var boards = new FakeBoardsClient();
        var reader = new FakeArtifactReader([new PhaseArtifact { FileName = "spec.md", Content = "x" }]);
        var orchestrator = BuildOrchestrator(reader, boards);

        var runId = orchestrator.StartRun(State());
        var run = await WaitForAsync(orchestrator, runId, r => r.IsAwaitingApproval);

        Assert.Equal(PhaseRunStatus.AwaitingApproval, run.State.Status);
        Assert.Single(run.State.Artifacts);
    }

    [Fact]
    public async Task MissingArtifacts_RecordsFailedWithReason()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = BuildOrchestrator(new FakeArtifactReader([]), boards);

        var runId = orchestrator.StartRun(State());
        var run = await WaitForAsync(orchestrator, runId, r => r.State.Status == PhaseRunStatus.Failed);

        Assert.Equal(PhaseRunStatus.Failed, run.State.Status);
        Assert.False(string.IsNullOrWhiteSpace(run.State.FailureReason));
        Assert.Empty(boards.Creates);
    }
}
