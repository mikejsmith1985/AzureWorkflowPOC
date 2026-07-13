// spec-019 T022 (visual orchestrator): with the MAF runtime flag on, a visual workflow runs on MAF
// Workflows end to end through the orchestrator and reaches Completed — behaviour-equivalent to the SK path.
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Parity;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Drives the visual <see cref="WorkflowExecutionOrchestrator"/> on the MAF runtime: a Trigger → Agentic →
/// Notify workflow runs to completion. The model call is pinned by a <see cref="RecordedChatClient"/>.
/// </summary>
public sealed class VisualOrchestratorMafTests
{
    [Fact]
    public async Task MafRuntime_TriggerAgenticNotify_RunsToCompletion()
    {
        var trigger = WorkflowNode.CreateNew(WorkflowNodeType.Trigger, "Start");
        var agentic = WorkflowNode.CreateNew(WorkflowNodeType.AgenticReason, "Reason") with { GoalPrompt = "Summarise.", IsConfigured = true };
        var notify = WorkflowNode.CreateNew(WorkflowNodeType.FunctionNotify, "Notify") with { IsConfigured = true };

        var edges = new[]
        {
            WorkflowEdge.CreateNew(trigger.Id, trigger.OutputPorts[0].Id, agentic.Id, "in", "e1"),
            WorkflowEdge.CreateNew(agentic.Id, "out", notify.Id, "in", "e2"),
        };

        var definition = new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            Name = "Visual MAF Run",
            OwnerId = "owner1",
            Nodes = new[] { trigger, agentic, notify }.ToList().AsReadOnly(),
            Edges = edges.ToList().AsReadOnly(),
            Settings = new WorkflowSettings(),
            ChatHistory = Array.Empty<WorkflowChatMessage>(),
            CreatedAt = DateTimeOffset.UtcNow,
            LastModifiedAt = DateTimeOffset.UtcNow,
        };

        var chatClient = new RecordedChatClient(RecordedTurn.With("a concise summary", inputTokens: 20, outputTokens: 6));
        var orchestrator = new WorkflowExecutionOrchestrator(
            chatClient);

        var runId = await orchestrator.StartRunAsync(definition, "process the input");

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline && orchestrator.GetRun(runId)?.Status is not (WorkflowRunStatus.Completed or WorkflowRunStatus.Failed))
        {
            await Task.Delay(25);
        }

        var run = orchestrator.GetRun(runId);
        Assert.NotNull(run);
        Assert.Equal(WorkflowRunStatus.Completed, run!.Status);
    }
}
