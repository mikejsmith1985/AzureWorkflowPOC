using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Parity;
using Microsoft.SemanticKernel;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Unit tests for PipelineOrchestrator public API — uses a no-op kernel factory
/// so no LLM calls are made. Tests cover run registration and HITL answer routing.
/// </summary>
public class PipelineOrchestratorTests
{
    private static Kernel StubKernel(IProgressReporter _) =>
        Kernel.CreateBuilder().Build();

    private static TicketState SampleTicket => new()
    {
        TicketId = "INC0001",
        Title = "Sample",
        Description = "Sample description.",
    };

    [Fact]
    public void GetRun_ReturnsNull_ForUnknownId()
    {
        var orchestrator = new PipelineOrchestrator(StubKernel);

        var result = orchestrator.GetRun("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void GetAllRuns_ReturnsEmpty_WhenNoRunsStarted()
    {
        var orchestrator = new PipelineOrchestrator(StubKernel);

        var runs = orchestrator.GetAllRuns();

        Assert.Empty(runs);
    }

    [Fact]
    public void SubmitHitlAnswer_DoesNotThrow_ForUnknownRunId()
    {
        var orchestrator = new PipelineOrchestrator(StubKernel);

        // Graceful no-op — should not throw even if runId doesn't exist
        var exception = Record.Exception(() =>
            orchestrator.SubmitHitlAnswer("nonexistent", "answer"));

        Assert.Null(exception);
    }

    // spec-019 T022: with the MAF runtime flag on, a ready ticket runs the intake pipeline on MAF
    // Workflows end-to-end and completes — behaviour-equivalent to the SK path, driven by a pinned client.
    [Fact]
    public async Task MafRuntime_ReadyTicket_RunsPipelineAndCompletes()
    {
        var chatClient = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"title\":\"Sample\",\"description\":\"Sample description.\"}", 40, 12),
            RecordedTurn.With("{\"is_ready\":true,\"missing_fields\":[],\"reasoning\":\"clear\"}", 30, 8),
            RecordedTurn.With("{\"points\":5,\"reasoning\":\"comparable to the CRUD anchor\"}", 25, 8),
        }, repeatLast: true);

        var orchestrator = new PipelineOrchestrator(StubKernel, chatClient: chatClient, useMafRuntime: true);

        var runId = orchestrator.StartRun(SampleTicket);
        var run = await WaitForCompletionAsync(orchestrator, runId);

        Assert.Equal(PipelineRunStatus.Complete, run.Status);
        Assert.False(string.IsNullOrEmpty(run.CurrentTicket?.JiraIssueUrl)); // Action executor ran
    }

    // spec-019 T022: a not-ready ticket runs intake→validation→gap-analysis on MAF and suspends at the
    // clarification RequestPort. This exercises the suspend path (which hangs without the stream-break fix).
    [Fact]
    public async Task MafRuntime_NotReadyTicket_SuspendsForClarification()
    {
        var chatClient = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"title\":\"Sample\",\"description\":\"Sample description.\"}", 40, 12),
            RecordedTurn.With("{\"is_ready\":false,\"missing_fields\":[\"target environment\"],\"reasoning\":\"missing env\"}", 30, 10),
            RecordedTurn.With("[\"What is the target environment?\"]", 35, 8),
        }, repeatLast: true);

        var orchestrator = new PipelineOrchestrator(StubKernel, chatClient: chatClient, useMafRuntime: true);

        var runId = orchestrator.StartRun(SampleTicket);
        var run = await WaitForCompletionAsync(orchestrator, runId);

        Assert.Equal(PipelineRunStatus.AwaitingHuman, run.Status); // parked at the clarification gate
    }

    // spec-019 US2: a not-ready ticket suspends for clarification; the PO's answer resumes the run through
    // the HITL RequestPort, validation re-runs with the answer, and (now ready) the run completes.
    [Fact]
    public async Task MafRuntime_NotReadyThenAnswered_CompletesAfterClarification()
    {
        var chatClient = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"title\":\"Sample\",\"description\":\"Sample description.\"}", 40, 12),      // intake
            RecordedTurn.With("{\"is_ready\":false,\"missing_fields\":[\"target environment\"],\"reasoning\":\"missing env\"}", 30, 10), // validate r0
            RecordedTurn.With("[\"What is the target environment?\"]", 35, 8),                                // gap analysis
            RecordedTurn.With("{\"is_ready\":true,\"missing_fields\":[],\"reasoning\":\"now clear\"}", 30, 8), // validate r1 (after answer)
            RecordedTurn.With("{\"points\":5,\"reasoning\":\"comparable to the CRUD anchor\"}", 25, 8),        // estimation
        }, repeatLast: true);

        var orchestrator = new PipelineOrchestrator(StubKernel, chatClient: chatClient, useMafRuntime: true);
        var runId = orchestrator.StartRun(SampleTicket);

        var paused = await WaitForStatusAsync(orchestrator, runId, PipelineRunStatus.AwaitingHuman);
        Assert.Equal(PipelineRunStatus.AwaitingHuman, paused.Status);
        Assert.NotEmpty(paused.CurrentTicket!.ClarifyingQuestions); // the questions are surfaced

        orchestrator.SubmitHitlAnswer(runId, "Azure production");

        var completed = await WaitForStatusAsync(orchestrator, runId, PipelineRunStatus.Complete);
        Assert.Equal(PipelineRunStatus.Complete, completed.Status);
        Assert.False(string.IsNullOrEmpty(completed.CurrentTicket?.JiraIssueUrl)); // ran through to Action
    }

    /// <summary>Polls the fire-and-forget run until it reaches the target status, or fails after a timeout.</summary>
    private static async Task<PipelineRun> WaitForStatusAsync(
        PipelineOrchestrator orchestrator, string runId, PipelineRunStatus target)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var run = orchestrator.GetRun(runId);
            if (run is not null && run.Status == target)
            {
                return run;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException($"Run '{runId}' did not reach {target} in time.");
    }

    /// <summary>Polls the fire-and-forget run until it leaves the Running state, or fails after a timeout.</summary>
    private static async Task<PipelineRun> WaitForCompletionAsync(PipelineOrchestrator orchestrator, string runId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var run = orchestrator.GetRun(runId);
            if (run is not null && run.Status != PipelineRunStatus.Running)
            {
                return run;
            }
            await Task.Delay(50);
        }
        throw new TimeoutException($"Run '{runId}' did not complete within the timeout.");
    }
}
