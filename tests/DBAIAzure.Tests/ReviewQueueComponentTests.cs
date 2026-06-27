// bUnit tests for ReviewQueue.razor: row rendering (T044) and Approve button wiring (T045).
using Bunit;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Pages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// bUnit component tests for the Review Queue page. Verifies that the page renders one row
/// per paused run returned by the repository (T044) and that clicking Approve routes the
/// decision to the orchestrator (T045).
/// </summary>
public sealed class ReviewQueueComponentTests : TestContext
{
    // ── T044: row count matches paused runs returned by repository ───────────

    [Fact]
    public async Task ReviewQueue_RendersOneRowPerPausedRun()
    {
        var run1 = MakePausedRecord("run-1", "Workflow Alpha");
        var run2 = MakePausedRecord("run-2", "Workflow Beta");

        var repo         = new FakeRunRepository([run1, run2]);
        var orchestrator = new RecordingOrchestrator();
        RegisterServices(repo, orchestrator);

        var cut = RenderComponent<ReviewQueue>();
        await cut.InvokeAsync(() => Task.CompletedTask); // let OnInitializedAsync complete

        // Two rows should be visible — one per paused run (one Approve button each). Select by the
        // button's text rather than its colour class, which is now a semantic token (spec-014 restyle).
        var rows = cut.FindAll("button").Where(b => b.TextContent.Trim() == "Approve").ToList();
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task ReviewQueue_WhenNoPausedRuns_ShowsEmptyMessage()
    {
        var repo         = new FakeRunRepository([]);
        var orchestrator = new RecordingOrchestrator();
        RegisterServices(repo, orchestrator);

        var cut = RenderComponent<ReviewQueue>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("No runs are currently awaiting approval", cut.Markup);
    }

    // ── T045: clicking Approve calls SubmitApproval(runId, true) ────────────

    [Fact]
    public async Task ReviewQueue_ClickingApprove_CallsSubmitApprovalWithTrue()
    {
        const string targetRunId = "run-approve";
        var run = MakePausedRecord(targetRunId, "Workflow Alpha");

        var repo         = new FakeRunRepository([run]);
        var orchestrator = new RecordingOrchestrator();
        RegisterServices(repo, orchestrator);

        var cut = RenderComponent<ReviewQueue>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        // Find the Approve button by its text (colour is now a semantic token) and click it.
        var approveButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Approve");
        approveButton.Click();

        Assert.Equal(1, orchestrator.SubmitApprovalCallCount);
        Assert.Equal((targetRunId, true), orchestrator.LastSubmit);
    }

    [Fact]
    public async Task ReviewQueue_ClickingReject_CallsSubmitApprovalWithFalse()
    {
        const string targetRunId = "run-reject";
        var run = MakePausedRecord(targetRunId, "Workflow Beta");

        var repo         = new FakeRunRepository([run]);
        var orchestrator = new RecordingOrchestrator();
        RegisterServices(repo, orchestrator);

        var cut = RenderComponent<ReviewQueue>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        // Find the Reject button by its text (colour is now a semantic token) and click it.
        var rejectButton = cut.FindAll("button").First(b => b.TextContent.Trim() == "Reject");
        rejectButton.Click();

        Assert.Equal(1, orchestrator.SubmitApprovalCallCount);
        Assert.Equal((targetRunId, false), orchestrator.LastSubmit);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void RegisterServices(FakeRunRepository repo, RecordingOrchestrator orchestrator)
    {
        Services.AddSingleton<IWorkflowRunRepository>(repo);
        Services.AddSingleton<IWorkflowExecutionOrchestrator>(orchestrator);
    }

    private static WorkflowRunRecord MakePausedRecord(string runId, string workflowName) =>
        new WorkflowRunRecord(
            RunId:         runId,
            WorkflowId:    Guid.NewGuid(),
            WorkflowName:  workflowName,
            Status:        WorkflowRunStatus.Paused,
            TriggeredBy:   "tester@example.com",
            StartedAt:     DateTimeOffset.UtcNow.AddMinutes(-5),
            SuspendedAt:   DateTimeOffset.UtcNow.AddMinutes(-2),
            ResumedAt:     null,
            CompletedAt:   null,
            FailureReason: null);

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeRunRepository : IWorkflowRunRepository
    {
        private readonly IReadOnlyList<WorkflowRunRecord> _records;

        public FakeRunRepository(IReadOnlyList<WorkflowRunRecord> records) => _records = records;

        public Task<IReadOnlyList<WorkflowRunRecord>> ListByStatusAsync(
            WorkflowRunStatus status, CancellationToken ct = default) =>
            Task.FromResult(_records);

        public Task<WorkflowRunRecord?> GetAsync(string runId, CancellationToken ct = default) =>
            Task.FromResult<WorkflowRunRecord?>(null);
        public Task CreateAsync(WorkflowRunRecord record, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task UpdateAsync(WorkflowRunRecord record, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<WorkflowRunRecord>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(_records);
        public Task PurgeTerminalRunsOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingOrchestrator : IWorkflowExecutionOrchestrator
    {
        public int SubmitApprovalCallCount { get; private set; }
        public (string RunId, bool Approved)? LastSubmit { get; private set; }

        public void SubmitApproval(string runId, bool approved)
        {
            SubmitApprovalCallCount++;
            LastSubmit = (runId, approved);
        }

        public event Action<string>? RunUpdated { add { } remove { } }
        public WorkflowExecutionRun? GetRun(string runId) => null;
        public Task<string> StartRunAsync(WorkflowDefinition workflow, string inputDescription, CancellationToken ct = default) =>
            Task.FromResult(string.Empty);
        public void RequestStop(string runId) { }
        public void RehydratePausedRun(WorkflowRunRecord record) { }
    }
}
