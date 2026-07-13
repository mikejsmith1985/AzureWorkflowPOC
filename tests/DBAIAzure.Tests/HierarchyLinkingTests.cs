// Tests for US3 hierarchy linking: Plan/Implement work items are linked under the feature's Epic,
// and the Epic is auto-created first when the feature has none, so nothing is ever orphaned (FR-012).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Verifies that Plan and Implement work items are associated with the feature's Epic, and that the
/// Epic is auto-created first when the feature's Specify phase was never handled (FR-012, no orphans).
/// Uses the real SK process via the orchestrator with a fake board client and a shared fake repository.
/// </summary>
public class HierarchyLinkingTests
{
    private static PhaseHandlerOrchestrator Build(
        FakeBoardsClient boards, IPhaseRunRepository repository, IReadOnlyList<PhaseArtifact> artifacts)
    {
        var validation = new PhaseValidationResult { Summary = "Summary.", Gaps = [] };
        var repo = repository;
        var writerDeps = new PhaseWorkItemWriterDeps(
            Tracker: WorkTrackerAdapters.AdoAdapterFor(boards), Repository: repo);
        return new PhaseHandlerOrchestrator(
            PhaseValidationChat.Returning(validation), new FakeArtifactReader(artifacts), writerDeps, repo);
    }

    private static PhaseHandlerState State(SpecKitPhase phase) => new()
    {
        RunId = Guid.NewGuid().ToString("N")[..8],
        FeatureKey = "001-feature",
        FeatureDirectory = "specs/001-feature",
        Phase = phase,
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

    private static async Task<string> RunApprovedAsync(PhaseHandlerOrchestrator orchestrator, SpecKitPhase phase)
    {
        var runId = orchestrator.StartRun(State(phase));
        await WaitAsync(orchestrator, runId, r => r.IsAwaitingApproval);
        orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true });
        await WaitAsync(orchestrator, runId, r => r.State.Status == PhaseRunStatus.Completed);
        return runId;
    }

    [Fact]
    public async Task ImplementWithExistingEpic_LinksUnderThatEpic_NoNewEpic()
    {
        var boards = new FakeBoardsClient();
        var repository = new FakePhaseRunRepository();

        // First handle Specify so a real Epic exists for the feature.
        var specifyArtifacts = new List<PhaseArtifact> { new() { FileName = "spec.md", Content = "spec" } };
        var specifyOrchestrator = Build(boards, repository, specifyArtifacts);
        await RunApprovedAsync(specifyOrchestrator, SpecKitPhase.Specify);

        // The Specify Epic's id is recorded in the run state; that id is what children must link to.
        var specifyRun = await repository.GetByFeaturePhaseAsync("001-feature", SpecKitPhase.Specify);
        var epicId = specifyRun!.CreatedWorkItems.Single().WorkItemId;

        // Now handle Implement: it must link under the existing Epic and create no second Epic.
        var implementArtifacts = new List<PhaseArtifact> { new() { FileName = "spec.md", Content = "done" } };
        var implementOrchestrator = Build(boards, repository, implementArtifacts);
        await RunApprovedAsync(implementOrchestrator, SpecKitPhase.Implement);

        var epics = boards.Creates.Where(c => c.Type == PhaseWorkItemMap.EpicType).ToList();
        Assert.Single(epics); // only the original Specify Epic — none auto-created
        var bug = boards.Creates.Single(c => c.Type == PhaseWorkItemMap.BugType);
        Assert.True(epicId.TryAsInt(out var epicNumericId));
        Assert.Equal(epicNumericId, bug.ParentId); // Bug linked under the existing Epic
    }

    [Fact]
    public async Task PlanWithNoEpic_AutoCreatesEpicFirst_ThenLinksTasksUnderIt()
    {
        var boards = new FakeBoardsClient();
        var repository = new FakePhaseRunRepository();
        var tasksMd = """
            - [ ] T001 First unit
            - [ ] T002 Second unit
            """;
        var orchestrator = Build(boards, repository, [new PhaseArtifact { FileName = "tasks.md", Content = tasksMd }]);

        await RunApprovedAsync(orchestrator, SpecKitPhase.Plan);

        // An Epic was auto-created (the feature had none) so the Tasks are never orphaned.
        var epics = boards.Creates.Where(c => c.Type == PhaseWorkItemMap.EpicType).ToList();
        Assert.Single(epics);

        // Every Task links under that auto-created Epic.
        var tasks = boards.Creates.Where(c => c.Type == PhaseWorkItemMap.TaskType).ToList();
        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, task => Assert.NotNull(task.ParentId));
    }
}
