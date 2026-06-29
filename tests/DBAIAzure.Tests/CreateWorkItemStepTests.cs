using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Tests for CreateWorkItemStep via the real SK process: an approved Specify run creates exactly one
/// Epic populated from the validation summary, and nothing is ever written without an approval (FR-006).
/// </summary>
public class CreateWorkItemStepTests
{
    private static PhaseHandlerOrchestrator Build(
        FakeBoardsClient boards,
        IReadOnlyList<PhaseArtifact>? artifacts = null,
        IPhaseRunRepository? repository = null)
    {
        var validation = new PhaseValidationResult
        {
            Summary = "Epic body summary.",
            Gaps = [new PhaseValidationGap { Label = "Gap1", Description = "Detail1" }],
        };
        var canned = artifacts ?? [new PhaseArtifact { FileName = "spec.md", Content = "x" }];
        Func<IPhaseProgressSink, Kernel> factory = sink =>
        {
            var builder = Kernel.CreateBuilder();
            builder.Services.AddSingleton<IArtifactReader>(new FakeArtifactReader(canned));
            builder.Services.AddSingleton<IStructuredCompletionService>(new FakeStructuredCompletionService(validation));
            builder.Services.AddSingleton<IBoardsClient>(boards);
            builder.Services.AddSingleton<DBAIAzure.Core.Interfaces.IWorkTrackerAdapter>(WorkTrackerAdapters.AdoAdapterFor(boards));
            builder.Services.AddSingleton(repository ?? (IPhaseRunRepository)new FakePhaseRunRepository());
            builder.Services.AddSingleton(sink);
            return builder.Build();
        };
        return new PhaseHandlerOrchestrator(factory, repository);
    }

    private static PhaseHandlerState State(SpecKitPhase phase = SpecKitPhase.Specify) => new()
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

    [Fact]
    public async Task ApprovedSpecify_CreatesExactlyOneEpic_FromSummary()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = Build(boards);

        var runId = orchestrator.StartRun(State());
        await WaitAsync(orchestrator, runId, r => r.IsAwaitingApproval);

        orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true });
        await WaitAsync(orchestrator, runId, r => r.State.Status == PhaseRunStatus.Completed);

        Assert.Single(boards.Creates);
        Assert.Equal(PhaseWorkItemMap.EpicType, boards.Creates[0].Type);
        Assert.Null(boards.Creates[0].ParentId); // Specify Epic has no parent
        Assert.Contains("Epic body summary.", boards.Creates[0].Description);
    }

    [Fact]
    public async Task Rejected_WritesNothing()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = Build(boards);

        var runId = orchestrator.StartRun(State());
        await WaitAsync(orchestrator, runId, r => r.IsAwaitingApproval);

        orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = false });
        await WaitAsync(orchestrator, runId, r => r.State.Status == PhaseRunStatus.Rejected);

        Assert.Empty(boards.Creates);
        Assert.Empty(boards.Upserts);
    }

    [Fact]
    public async Task ApprovedPlan_CreatesOneTaskPerPlannedItem()
    {
        var boards = new FakeBoardsClient();
        var tasksMd = """
            - [ ] T001 First planned unit
            - [ ] T002 Second planned unit
            """;
        var orchestrator = Build(boards, [new PhaseArtifact { FileName = "tasks.md", Content = tasksMd }]);

        var runId = orchestrator.StartRun(State(SpecKitPhase.Plan));
        await WaitAsync(orchestrator, runId, r => r.IsAwaitingApproval);

        orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true });
        await WaitAsync(orchestrator, runId, r => r.State.Status == PhaseRunStatus.Completed);

        // One Task per planned unit of work (FR-008). An Epic is auto-created first because the
        // feature has none yet, so the Tasks are never orphaned (FR-012) — but it is not a Task.
        var tasks = boards.Creates.Where(create => create.Type == PhaseWorkItemMap.TaskType).ToList();
        Assert.Equal(2, tasks.Count);
        Assert.Contains(tasks, task => task.Title.Contains("First planned unit"));
        Assert.Contains(tasks, task => task.Title.Contains("Second planned unit"));
        // The created work items recorded in state are exactly the two Tasks.
        Assert.Equal(2, orchestrator.GetRun(runId)!.State.CreatedWorkItems.Count);
    }

    [Fact]
    public async Task ApprovedImplement_CreatesExactlyOneBug()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = Build(boards, [new PhaseArtifact { FileName = "spec.md", Content = "done" }]);

        var runId = orchestrator.StartRun(State(SpecKitPhase.Implement));
        await WaitAsync(orchestrator, runId, r => r.IsAwaitingApproval);

        orchestrator.SubmitApproval(runId, new ApprovalDecision { IsApproved = true });
        await WaitAsync(orchestrator, runId, r => r.State.Status == PhaseRunStatus.Completed);

        // Exactly one Bug/completion record (FR-007), linked under an auto-created Epic (FR-012).
        var bugs = boards.Creates.Where(create => create.Type == PhaseWorkItemMap.BugType).ToList();
        Assert.Single(bugs);
        Assert.NotNull(bugs[0].ParentId); // linked under the feature's Epic
        Assert.Single(orchestrator.GetRun(runId)!.State.CreatedWorkItems);
    }

    [Fact]
    public void UnsupportedPhase_CreatesNothing_AndIsRecorded()
    {
        var boards = new FakeBoardsClient();
        var orchestrator = Build(boards);

        var runId = orchestrator.StartRun(State(SpecKitPhase.Unsupported));

        // Unsupported short-circuits synchronously in StartRun — no board write, terminal status.
        Assert.Equal(PhaseRunStatus.Unsupported, orchestrator.GetRun(runId)!.State.Status);
        Assert.Empty(boards.Creates);
        Assert.Empty(boards.Upserts);
    }
}
