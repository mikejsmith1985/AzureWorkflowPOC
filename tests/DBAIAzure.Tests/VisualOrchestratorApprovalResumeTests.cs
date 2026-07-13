// spec-019 T024/T027 (visual approval resume): with the MAF runtime on, a visual workflow with a
// HumanApproval node suspends as Paused, resumes on the reviewer's decision (SubmitApproval), and drives to
// Completed — the approval-gate half of the visual orchestrator's MAF migration.
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Parity;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Drives the visual <see cref="WorkflowExecutionOrchestrator"/> on the MAF runtime through a HumanApproval
/// gate: Trigger → Agentic → HumanApproval → Notify. The run must pause at the gate, then complete once the
/// reviewer approves (or rejects) via <see cref="WorkflowExecutionOrchestrator.SubmitApproval"/>.
/// </summary>
public sealed class VisualOrchestratorApprovalResumeTests
{
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
            Name = "Visual MAF Approval",
            OwnerId = "owner1",
            Nodes = new[] { trigger, agentic, approval, notify }.ToList().AsReadOnly(),
            Edges = edges.ToList().AsReadOnly(),
            Settings = new WorkflowSettings(),
            ChatHistory = Array.Empty<WorkflowChatMessage>(),
            CreatedAt = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow,
        };
    }

    private static async Task<WorkflowRunStatus> WaitForStatusAsync(
        WorkflowExecutionOrchestrator orchestrator, string runId, WorkflowRunStatus target, int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var status = orchestrator.GetRun(runId)?.Status;
            if (status == target) return status.Value;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Run '{runId}' did not reach {target}; last status={orchestrator.GetRun(runId)?.Status}.");
    }

    [Fact]
    public async Task MafRuntime_ApprovalGate_PausesThenCompletesOnApprove()
    {
        var chatClient = new RecordedChatClient(RecordedTurn.With("a concise summary", 20, 6));
        var orchestrator = new WorkflowExecutionOrchestrator(chatClient);

        var runId = await orchestrator.StartRunAsync(ApprovalWorkflow(), "process the input");

        await WaitForStatusAsync(orchestrator, runId, WorkflowRunStatus.Paused);   // suspended at the gate
        orchestrator.SubmitApproval(runId, approved: true);
        await WaitForStatusAsync(orchestrator, runId, WorkflowRunStatus.Completed); // resumed and finished
    }

    [Fact]
    public async Task MafRuntime_ApprovalGate_CompletesOnReject()
    {
        var chatClient = new RecordedChatClient(RecordedTurn.With("a concise summary", 20, 6));
        var orchestrator = new WorkflowExecutionOrchestrator(chatClient);

        var runId = await orchestrator.StartRunAsync(ApprovalWorkflow(), "process the input");

        await WaitForStatusAsync(orchestrator, runId, WorkflowRunStatus.Paused);
        orchestrator.SubmitApproval(runId, approved: false);
        // Parity with the SK gate: the run continues past the gate carrying the decision and terminates.
        await WaitForStatusAsync(orchestrator, runId, WorkflowRunStatus.Completed);
    }
}
