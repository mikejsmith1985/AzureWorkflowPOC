// Tests for US3 idempotent re-signalling: a repeat (FeatureKey, Phase) signal upserts the existing
// work item (fields refreshed + summary appended as a comment) and creates ZERO duplicates (FR-013).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Verifies idempotent upsert (FR-013 / FR-018): re-sending an already-handled, approved phase signal
/// updates the existing work item via <c>UpsertWorkItemAsync</c> (fields refreshed, latest summary
/// appended as a comment) and never creates a duplicate. Shares one fake repository across both runs
/// so the second run finds the first run's stored work item id.
/// </summary>
public class IdempotentUpsertTests
{
    private static PhaseHandlerOrchestrator Build(
        FakeBoardsClient boards, IPhaseRunRepository repository, string summary)
    {
        var validation = new PhaseValidationResult { Summary = summary, Gaps = [] };
        var repo = repository;
        var writerDeps = new PhaseWorkItemWriterDeps(
            Tracker: WorkTrackerAdapters.AdoAdapterFor(boards), Repository: repo);
        return new PhaseHandlerOrchestrator(
            PhaseValidationChat.Returning(validation), new FakeArtifactReader([new PhaseArtifact { FileName = "spec.md", Content = "x" }]), writerDeps, repo);
    }

    private static PhaseHandlerState State() => new()
    {
        RunId = Guid.NewGuid().ToString("N")[..8],
        FeatureKey = "001-feature",
        FeatureDirectory = "specs/001-feature",
        Phase = SpecKitPhase.Specify,
    };

    private static async Task WaitAsync(PhaseHandlerOrchestrator orchestrator, string runId, Func<PhaseHandlerRun, bool> done)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (done(orchestrator.GetRun(runId)!)) return;
            await Task.Delay(10);
        }
        throw new TimeoutException("Condition not met.");
    }

    private static async Task RunApprovedAsync(PhaseHandlerOrchestrator orchestrator)
    {
        var runId = orchestrator.StartRun(State());
        await WaitAsync(orchestrator, runId, r => r.IsAwaitingApproval);
        orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true });
        await WaitAsync(orchestrator, runId, r => r.State.Status == PhaseRunStatus.Completed);
    }

    [Fact]
    public async Task RepeatSignal_UpsertsExistingItem_AndCreatesNoDuplicate()
    {
        var boards = new FakeBoardsClient();
        var repository = new FakePhaseRunRepository();

        // First signal: creates the Epic.
        await RunApprovedAsync(Build(boards, repository, "First summary."));
        Assert.Single(boards.Creates);
        Assert.Empty(boards.Upserts);

        // Second identical signal for the same (feature, phase): must upsert, not duplicate.
        await RunApprovedAsync(Build(boards, repository, "Second summary."));

        Assert.Single(boards.Creates);              // still exactly one create — zero duplicates
        Assert.Single(boards.Upserts);              // the repeat went through the upsert path
    }

    [Fact]
    public async Task RepeatSignal_RefreshesFields_AndAppendsSummaryAsComment()
    {
        var boards = new FakeBoardsClient();
        var repository = new FakePhaseRunRepository();

        await RunApprovedAsync(Build(boards, repository, "Original summary."));
        await RunApprovedAsync(Build(boards, repository, "Updated summary."));

        var upsert = Assert.Single(boards.Upserts);
        Assert.Equal(boards.Creates[0].Title, upsert.Title);  // fields refreshed to current title
        Assert.Contains("Updated summary.", upsert.Description); // refreshed description
        Assert.Contains("Updated summary.", upsert.Comment);     // latest summary appended as a comment
    }

    [Fact]
    public async Task RepeatSignal_MarksCreatedRefAsUpdated()
    {
        var boards = new FakeBoardsClient();
        var repository = new FakePhaseRunRepository();

        await RunApprovedAsync(Build(boards, repository, "First."));

        var secondOrchestrator = Build(boards, repository, "Second.");
        var runId = secondOrchestrator.StartRun(State());
        await WaitAsync(secondOrchestrator, runId, r => r.IsAwaitingApproval);
        secondOrchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true });
        await WaitAsync(secondOrchestrator, runId, r => r.State.Status == PhaseRunStatus.Completed);

        var created = Assert.Single(secondOrchestrator.GetRun(runId)!.State.CreatedWorkItems);
        Assert.True(created.WasUpdated); // upserted, not newly created (FR-013)
    }
}
