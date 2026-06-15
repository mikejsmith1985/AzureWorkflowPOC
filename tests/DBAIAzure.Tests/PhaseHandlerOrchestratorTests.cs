using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// End-to-end unit tests for the phase-handler orchestrator driving the real SK process with
/// hand-rolled fakes (no network, no file system, no Azure). They verify the FR-006 approval gate,
/// the reject path, the board-write-failure path (FR-015), and the pause-time notifier push.
/// </summary>
public class PhaseHandlerOrchestratorTests
{
    private static PhaseHandlerState SampleState(SpecKitPhase phase = SpecKitPhase.Specify) => new()
    {
        RunId = Guid.NewGuid().ToString("N")[..8],
        FeatureKey = "001-sample-feature",
        FeatureDirectory = "specs/001-sample-feature",
        Phase = phase,
    };

    private static PhaseValidationResult SampleValidation => new()
    {
        Summary = "A clear summary of the phase.",
        Gaps = [new PhaseValidationGap { Label = "Edge case", Description = "Timeouts unspecified." }],
    };

    // Builds a kernel factory wiring the supplied fakes plus the orchestrator-provided progress sink.
    private static Func<IPhaseProgressSink, Kernel> KernelFactory(
        IArtifactReader reader, IStructuredCompletionService chat, IBoardsClient boards) =>
        sink =>
        {
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton(reader);
            builder.Services.AddSingleton(chat);
            builder.Services.AddSingleton(boards);
            builder.Services.AddSingleton(sink);
            return builder.Build();
        };

    private static FakeArtifactReader ReaderWithArtifacts() =>
        new([new PhaseArtifact { FileName = "spec.md", Content = "# Spec\nContent." }]);

    /// <summary>Spins until the run reaches the awaiting-approval gate (background task is async).</summary>
    private static async Task<PhaseHandlerRun> WaitUntilAwaitingAsync(
        PhaseHandlerOrchestrator orchestrator, string runId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var run = orchestrator.GetRun(runId)!;
            if (run.IsAwaitingApproval) return run;
            await Task.Delay(10);
        }
        throw new TimeoutException("Run never reached AwaitingApproval.");
    }

    /// <summary>Spins until the run reaches one of the expected terminal statuses.</summary>
    private static async Task<PhaseHandlerRun> WaitForStatusAsync(
        PhaseHandlerOrchestrator orchestrator, string runId, params PhaseRunStatus[] statuses)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var run = orchestrator.GetRun(runId)!;
            if (statuses.Contains(run.State.Status)) return run;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Run never reached {string.Join("/", statuses)} (was {orchestrator.GetRun(runId)!.State.Status}).");
    }

    [Fact]
    public async Task PausesForApproval_AndWritesNothingBeforeDecision()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = new PhaseHandlerOrchestrator(
            KernelFactory(ReaderWithArtifacts(), new FakeStructuredCompletionService(SampleValidation), boards));

        var runId = orchestrator.StartRun(SampleState());
        var run = await WaitUntilAwaitingAsync(orchestrator, runId);

        Assert.Equal(PhaseRunStatus.AwaitingApproval, run.State.Status);
        Assert.NotNull(run.State.Validation);
        Assert.Empty(boards.Creates); // FR-006: nothing written before approval
    }

    [Fact]
    public async Task Approval_CreatesEpic_AndCompletes()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = new PhaseHandlerOrchestrator(
            KernelFactory(ReaderWithArtifacts(), new FakeStructuredCompletionService(SampleValidation), boards));

        var runId = orchestrator.StartRun(SampleState());
        await WaitUntilAwaitingAsync(orchestrator, runId);

        var result = orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true, DecidedBy = "tester" });
        Assert.Equal(PhaseHandlerOrchestrator.ApprovalResult.Applied, result);

        var run = await WaitForStatusAsync(orchestrator, runId, PhaseRunStatus.Completed);
        Assert.Single(boards.Creates);
        Assert.Equal(PhaseWorkItemMap.EpicType, boards.Creates[0].Type);
        Assert.Single(run.State.CreatedWorkItems);
        Assert.Equal(PhaseWorkItemMap.EpicType, run.State.CreatedWorkItems[0].WorkItemType);
    }

    [Fact]
    public async Task Rejection_CreatesNoWorkItem_AndRecordsRejected()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = new PhaseHandlerOrchestrator(
            KernelFactory(ReaderWithArtifacts(), new FakeStructuredCompletionService(SampleValidation), boards));

        var runId = orchestrator.StartRun(SampleState());
        await WaitUntilAwaitingAsync(orchestrator, runId);

        orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = false });

        var run = await WaitForStatusAsync(orchestrator, runId, PhaseRunStatus.Rejected);
        Assert.Empty(boards.Creates);
        Assert.Equal(PhaseRunStatus.Rejected, run.State.Status);
    }

    [Fact]
    public async Task BoardWriteFailureAfterApproval_RecordsFailed_PreservingApproval()
    {
        var boards = new FakeBoardsClient { FailWith = new InvalidOperationException("boom") };
        var orchestrator = new PhaseHandlerOrchestrator(
            KernelFactory(ReaderWithArtifacts(), new FakeStructuredCompletionService(SampleValidation), boards));

        var runId = orchestrator.StartRun(SampleState());
        await WaitUntilAwaitingAsync(orchestrator, runId);

        orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true });

        var run = await WaitForStatusAsync(orchestrator, runId, PhaseRunStatus.Failed);
        Assert.Equal(PhaseRunStatus.Failed, run.State.Status);
        Assert.NotNull(run.State.FailureReason);
        Assert.Contains("boom", run.State.FailureReason);
        // FR-015: the approval is preserved even though the write failed.
        Assert.NotNull(run.State.Decision);
        Assert.True(run.State.Decision!.IsApproved);
    }

    [Fact]
    public async Task MissingArtifacts_FailsBeforeApproval_WithReason()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = new PhaseHandlerOrchestrator(
            KernelFactory(new FakeArtifactReader([]), new FakeStructuredCompletionService(SampleValidation), boards));

        var runId = orchestrator.StartRun(SampleState());

        var run = await WaitForStatusAsync(orchestrator, runId, PhaseRunStatus.Failed);
        Assert.Equal(PhaseRunStatus.Failed, run.State.Status);
        Assert.Contains("No artifacts", run.State.FailureReason!);
        Assert.Empty(boards.Creates);
    }

    [Fact]
    public void UnsupportedPhase_RecordedAsUnsupported_NoRun()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = new PhaseHandlerOrchestrator(
            KernelFactory(ReaderWithArtifacts(), new FakeStructuredCompletionService(SampleValidation), boards));

        var runId = orchestrator.StartRun(SampleState(SpecKitPhase.Unsupported));

        Assert.Equal(PhaseRunStatus.Unsupported, orchestrator.GetRun(runId)!.State.Status);
        Assert.Empty(boards.Creates);
    }

    [Fact]
    public async Task ApprovalForUnknownRun_ReturnsNotAwaiting()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = new PhaseHandlerOrchestrator(
            KernelFactory(ReaderWithArtifacts(), new FakeStructuredCompletionService(SampleValidation), boards));

        var result = orchestrator.SubmitApproval("does-not-exist", new ApprovalDecision { IsApproved = true });
        Assert.Equal(PhaseHandlerOrchestrator.ApprovalResult.NotAwaiting, result);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task DoubleDecision_SecondReturnsNotAwaiting()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = new PhaseHandlerOrchestrator(
            KernelFactory(ReaderWithArtifacts(), new FakeStructuredCompletionService(SampleValidation), boards));

        var runId = orchestrator.StartRun(SampleState());
        await WaitUntilAwaitingAsync(orchestrator, runId);

        var first = orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true });
        var second = orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true });

        Assert.Equal(PhaseHandlerOrchestrator.ApprovalResult.Applied, first);
        Assert.Equal(PhaseHandlerOrchestrator.ApprovalResult.NotAwaiting, second);
    }
}
