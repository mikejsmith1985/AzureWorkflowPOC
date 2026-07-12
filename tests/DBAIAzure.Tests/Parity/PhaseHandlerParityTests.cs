// Parity test for the phase-handler pipeline migrated onto MAF Workflows (spec-019 T015). Asserts the
// migrated workflow reproduces the SK PhaseHandlerPipelineBuilder shape: read → validate → approval
// gate, then create on the approved decision. FAILING first (Red): the factory is not yet built.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DBAIAzure.Tests.Parity;

/// <summary>
/// Framework-equivalence test for the phase-handler pipeline. Baseline (SK) behaviour: ReadArtifacts →
/// PhaseValidation → an approval pause; on resume with the decision, CreateWorkItem. The migrated MAF
/// workflow must reproduce that executor sequence and suspend at the approval <c>RequestPort</c>
/// (FR-015 / SC-001). Model output is pinned by <see cref="RecordedChatClient"/>.
/// </summary>
[Trait("Category", "US1Parity")]
public sealed class PhaseHandlerParityTests
{
    private static PhaseHandlerState SampleState => new()
    {
        RunId = "phase0001",
        FeatureKey = "001-sample-feature",
        FeatureDirectory = "specs/001-sample-feature",
        Phase = SpecKitPhase.Specify,
    };

    // A reader that returns one artifact so the read step proceeds to validation (no file system).
    private sealed class FakeArtifactReader : IArtifactReader
    {
        public Task<IReadOnlyList<PhaseArtifact>> ReadArtifactsAsync(string featureDirectory, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PhaseArtifact>>(new[]
            {
                new PhaseArtifact { FileName = "spec.md", Content = "A sample spec." },
            });
    }

    [Fact]
    public async Task PhaseSignal_RunsRead_Validate_ThenSuspendsAtApprovalGate()
    {
        // Pinned model turn: the schema-bound validation result the structured service will bind.
        var chatClient = new RecordedChatClient(
            RecordedTurn.With("{\"summary\":\"The spec is clear.\",\"gaps\":[]}", inputTokens: 50, outputTokens: 20));

        var services = new ServiceCollection()
            .AddSingleton<IArtifactReader>(new FakeArtifactReader())
            .BuildServiceProvider();

        var workflow = MafPhaseHandlerWorkflowFactory.Build(chatClient, services);
        var observation = await MafWorkflowRunner.RunAsync(workflow, SampleState);

        Assert.Equal(
            new[]
            {
                MafExecutorIds.ReadArtifacts,
                MafExecutorIds.PhaseValidation,
                MafExecutorIds.ApprovalHitl,
            },
            observation.ExecutorSequence);
        Assert.NotNull(observation.PendingRequest); // parked at the reviewer approval gate
    }

    // Regression guard: WatchStreamAsync does not reliably complete at RunStatus.PendingRequests on a
    // background thread, so MafWorkflowExecution must break the stream when a run suspends. Running it via
    // Task.Run (as the orchestrators do) must still return Suspended within a few seconds — not hang.
    [Fact]
    public async Task MafWorkflowExecution_OnBackgroundThread_ReturnsSuspended()
    {
        var chatClient = new RecordedChatClient(
            RecordedTurn.With("{\"summary\":\"The spec is clear.\",\"gaps\":[]}", 50, 20));
        var services = new MafExecutorServices().Add<IArtifactReader>(new FakeArtifactReader());
        var workflow = MafPhaseHandlerWorkflowFactory.Build(chatClient, services);

        var outcome = await Task.Run(() => MafWorkflowExecution.RunAsync<PhaseHandlerState, PhaseHandlerState>(
            workflow, SampleState, "bg-run", default))
            .WaitAsync(TimeSpan.FromSeconds(15));

        Assert.True(outcome.Suspended, $"Expected suspended; output status={outcome.Output?.Status.ToString() ?? "null"}");
        Assert.NotNull(outcome.PendingRequest);
    }
}
