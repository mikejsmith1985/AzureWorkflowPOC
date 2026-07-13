// Boot-resume test for the visual workflow builder (spec-019 T032): a run paused at a HumanApproval gate
// before a restart is rehydrated by a brand-new orchestrator from its checkpoint, and a reviewer decision
// submitted after the "restart" drives it through to completion.
using System.Text.Json;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Checkpointing;
using DBAIAzure.Tests.Parity;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using Xunit;

namespace DBAIAzure.Tests.Checkpointing;

/// <summary>
/// Simulates an application restart for the visual builder: one orchestrator runs a Trigger → Agentic →
/// HumanApproval → Notify workflow to the approval gate (leaving a checkpoint), then a fresh orchestrator —
/// with empty in-memory state, over the same database — resumes the paused run from its checkpoint and
/// completes it once the reviewer approves.
/// </summary>
public sealed class VisualBootResumeTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<PipelineDbContext> _options;
    private readonly EfCheckpointStore _store;
    private readonly CheckpointManager _checkpointManager;

    public VisualBootResumeTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        _options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using var seed = new PipelineDbContext(_options);
        seed.Database.EnsureCreated();
        _store = new EfCheckpointStore(new SharedFactory(_options));
        _checkpointManager = CheckpointManager.CreateJson(_store, new JsonSerializerOptions());
    }

    public void Dispose() => _keepAlive.Dispose();

    private static Kernel StubKernel() => Kernel.CreateBuilder().Build();

    private WorkflowExecutionOrchestrator NewOrchestrator(RecordedChatClient chatClient) =>
        new(StubKernel, chatClient: chatClient, useMafRuntime: true, checkpointManager: _checkpointManager);

    private static WorkflowDefinition ApprovalWorkflow()
    {
        var trigger = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start");
        var agentic = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Reason") with { GoalPrompt = "Summarise.", IsConfigured = true };
        var approval = WorkflowNode.CreateNew(WorkflowNodeType.HumanApproval, "Approve") with { IsConfigured = true };
        var notify = WorkflowNode.CreateNew(WorkflowNodeType.FunctionNotify, "Notify") with { IsConfigured = true };

        var edges = new[]
        {
            WorkflowEdge.CreateNew(trigger.Id, trigger.OutputPorts[0].Id, agentic.Id, "in", "e1"),
            WorkflowEdge.CreateNew(agentic.Id, "out", approval.Id, "in", "e2"),
            WorkflowEdge.CreateNew(approval.Id, "out", notify.Id, "in", "e3"),
        };

        return new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Visual MAF Boot Resume",
            OwnerId = "owner1",
            Nodes = new[] { trigger, agentic, approval, notify }.ToList().AsReadOnly(),
            Edges = edges.ToList().AsReadOnly(),
            Settings = new WorkflowSettings(),
            ChatHistory = Array.Empty<WorkflowChatMessage>(),
            CreatedAt = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task PausedApprovalRun_RehydratesFromCheckpoint_AndCompletesOnApprove()
    {
        var definition = ApprovalWorkflow();

        // ── Process #1: run to the approval gate, then "crash" (leaving a checkpoint). ──
        var orchestrator1 = NewOrchestrator(new RecordedChatClient(RecordedTurn.With("a concise summary", 20, 6)));
        var runId = await orchestrator1.StartRunAsync(definition, "process the input");
        await WaitForStatusAsync(orchestrator1, runId, WorkflowRunStatus.Paused);

        // ── Process #2 (restart): fresh orchestrator, empty memory, same database + checkpoint store. ──
        var orchestrator2 = NewOrchestrator(new RecordedChatClient(RecordedTurn.With("a concise summary", 20, 6)));
        Assert.Null(orchestrator2.GetRun(runId)); // nothing in memory after the "restart"

        var checkpoint = await _store.GetLatestCheckpointAsync(runId);
        Assert.NotNull(checkpoint);

        var record = new WorkflowRunRecord(
            RunId:         runId,
            WorkflowId:    definition.Id,
            WorkflowName:  definition.Name,
            Status:        WorkflowRunStatus.Paused,
            TriggeredBy:   "tester@example.com",
            StartedAt:     DateTimeOffset.UtcNow.AddMinutes(-2),
            SuspendedAt:   DateTimeOffset.UtcNow.AddMinutes(-1),
            ResumedAt:     null,
            CompletedAt:   null,
            FailureReason: null);

        orchestrator2.RehydratePausedRun(record, definition, checkpoint!);

        // The rehydrated run recovers its paused state, then the approval drives it to completion.
        await WaitForStatusAsync(orchestrator2, runId, WorkflowRunStatus.Paused);
        orchestrator2.SubmitApproval(runId, approved: true);
        await WaitForStatusAsync(orchestrator2, runId, WorkflowRunStatus.Completed);
    }

    private static async Task WaitForStatusAsync(
        WorkflowExecutionOrchestrator orchestrator, string runId, WorkflowRunStatus target)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (orchestrator.GetRun(runId)?.Status == target) return;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Run '{runId}' did not reach {target}; last status={orchestrator.GetRun(runId)?.Status}.");
    }

    private sealed class SharedFactory(DbContextOptions<PipelineDbContext> options) : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }
}
