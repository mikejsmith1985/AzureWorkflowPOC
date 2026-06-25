// Unit tests for AppMonitoringService: snapshot-driven detection + close-the-loop dedup (feature 013, US3).
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Monitoring;
using DBAIAzure.Storage.Repositories;
using Xunit;

namespace DBAIAzure.Tests.Apps;

/// <summary>
/// Verifies the monitoring cycle runs the linked workflow on a detected problem, raises exactly one
/// run per ongoing issue (dedup, FR-012), passes a snapshot-derived input (FR-018), and never crashes
/// on a missing/deleted link (FR-017) or when there is no problem.
/// </summary>
public sealed class AppMonitoringServiceTests
{
    private const string Owner = "demo";

    private static MonitoredApp FailedApp(WorkflowDefinition? linkTo) => new()
    {
        Name = "svc", OwnerId = Owner, RepoLocalPath = "/x", RunCommand = "run",
        Status = AppStatus.Ready,
        LinkedWorkflowId = linkTo?.Id.ToString(),
        LastRunResult = new AppRunResult(RunOutcome.Failed, "exit 1", "boom", DateTimeOffset.UtcNow)
    };

    [Fact]
    public async Task RunCycle_DetectedProblem_StartsOneRun_WithSnapshotInput()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var workflow = WorkflowDefinition.CreateNew("monitor", Owner);
            var orchestrator = new FakeWorkflowOrchestrator();
            var sut = new AppMonitoringService(orchestrator, new FakeWorkflowRepository(workflow), new SqliteAppHeartbeatStore(factory));

            var raised = await sut.RunCycleAsync(FailedApp(workflow));

            Assert.Single(raised);
            Assert.Single(orchestrator.Started);
            Assert.Contains("Monitoring detected an issue", orchestrator.Started[0].input);
        }
    }

    [Fact]
    public async Task RunCycle_RecurringProblem_RaisedOnce()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var workflow = WorkflowDefinition.CreateNew("monitor", Owner);
            var orchestrator = new FakeWorkflowOrchestrator();
            var sut = new AppMonitoringService(orchestrator, new FakeWorkflowRepository(workflow), new SqliteAppHeartbeatStore(factory));
            var app = FailedApp(workflow);

            await sut.RunCycleAsync(app);
            var second = await sut.RunCycleAsync(app);

            Assert.Empty(second);
            Assert.Single(orchestrator.Started); // not raised again for the same ongoing issue
        }
    }

    [Fact]
    public async Task RunCycle_NotLinked_ReturnsEmpty()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var orchestrator = new FakeWorkflowOrchestrator();
            var sut = new AppMonitoringService(orchestrator, new FakeWorkflowRepository(null), new SqliteAppHeartbeatStore(factory));

            var raised = await sut.RunCycleAsync(FailedApp(linkTo: null));

            Assert.Empty(raised);
            Assert.Empty(orchestrator.Started);
        }
    }

    [Fact]
    public async Task RunCycle_LinkedButWorkflowDeleted_ReturnsEmpty_NoCrash()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var orchestrator = new FakeWorkflowOrchestrator();
            // App links to a workflow id the repository no longer has (returns null).
            var app = FailedApp(WorkflowDefinition.CreateNew("gone", Owner));
            var sut = new AppMonitoringService(orchestrator, new FakeWorkflowRepository(null), new SqliteAppHeartbeatStore(factory));

            var raised = await sut.RunCycleAsync(app);

            Assert.Empty(raised);
            Assert.Empty(orchestrator.Started);
        }
    }

    [Fact]
    public async Task RunCycle_NoProblem_ReturnsEmpty()
    {
        var (factory, connection) = AppTestDb.Create();
        await using (connection)
        {
            var workflow = WorkflowDefinition.CreateNew("monitor", Owner);
            var orchestrator = new FakeWorkflowOrchestrator();
            var healthy = FailedApp(workflow) with
            {
                LastRunResult = new AppRunResult(RunOutcome.Succeeded, "exit 0", "ok", DateTimeOffset.UtcNow)
            };
            var sut = new AppMonitoringService(orchestrator, new FakeWorkflowRepository(workflow), new SqliteAppHeartbeatStore(factory));

            var raised = await sut.RunCycleAsync(healthy);

            Assert.Empty(raised);
            Assert.Empty(orchestrator.Started);
        }
    }
}
