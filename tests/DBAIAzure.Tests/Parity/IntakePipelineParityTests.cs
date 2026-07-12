// Parity test for the intake pipeline migrated onto MAF Workflows (spec-019 T014). Asserts the migrated
// workflow runs the SAME step sequence as the retired SK IntakePipelineBuilder for both branches, driven
// by a pinned RecordedChatClient. FAILING first (Red): MafIntakeWorkflowFactory.Build is not yet built.
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Xunit;

namespace DBAIAzure.Tests.Parity;

/// <summary>
/// Framework-equivalence tests for the intake pipeline. The baseline (SK) behaviour is: Intake →
/// Validation, then on the ready path Estimation → Action, and on the not-ready path GapAnalysis →
/// a human clarification gate. The migrated MAF workflow must reproduce that exact executor sequence
/// (FR-015 / SC-001). Model output is pinned by <see cref="RecordedChatClient"/> so routing is
/// deterministic — no live LLM (finding U1).
/// </summary>
[Trait("Category", "US1Parity")]
public sealed class IntakePipelineParityTests
{
    private static TicketState SampleTicket => new()
    {
        TicketId = "INC0001",
        Title = "Sample",
        Description = "Sample description.",
    };

    [Fact]
    public async Task ReadyTicket_RunsIntake_Validation_Estimation_Action()
    {
        // Pinned model turns (the real JSON each executor parses): normalise, decide READY, estimate.
        var chatClient = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"title\":\"Sample\",\"description\":\"Sample description.\"}", inputTokens: 40, outputTokens: 12),
            RecordedTurn.With("{\"is_ready\":true,\"missing_fields\":[],\"reasoning\":\"clear\"}", inputTokens: 30, outputTokens: 8),
            RecordedTurn.With("{\"points\":5,\"reasoning\":\"comparable to the CRUD anchor\"}", inputTokens: 25, outputTokens: 8),
        }, repeatLast: true);

        var workflow = MafIntakeWorkflowFactory.Build(chatClient);
        var observation = await MafWorkflowRunner.RunAsync(workflow, SampleTicket);

        Assert.Equal(
            new[]
            {
                MafExecutorIds.Intake,
                MafExecutorIds.Validation,
                MafExecutorIds.Estimation,
                MafExecutorIds.Action,
            },
            observation.ExecutorSequence);
        Assert.Null(observation.PendingRequest); // the ready path never pauses for a human
    }

    [Fact]
    public async Task NotReadyTicket_RoutesToGapAnalysis_AndSuspendsForHuman()
    {
        // Pinned model turns (real JSON): normalise, decide NOT ready, then a JSON array of questions.
        var chatClient = new RecordedChatClient(new[]
        {
            RecordedTurn.With("{\"title\":\"Sample\",\"description\":\"Sample description.\"}", inputTokens: 40, outputTokens: 12),
            RecordedTurn.With("{\"is_ready\":false,\"missing_fields\":[\"target environment\"],\"reasoning\":\"missing env\"}", inputTokens: 30, outputTokens: 10),
            RecordedTurn.With("[\"What is the target environment?\"]", inputTokens: 35, outputTokens: 8),
        }, repeatLast: true);

        var workflow = MafIntakeWorkflowFactory.Build(chatClient);
        var observation = await MafWorkflowRunner.RunAsync(workflow, SampleTicket);

        Assert.Equal(
            new[]
            {
                MafExecutorIds.Intake,
                MafExecutorIds.Validation,
                MafExecutorIds.GapAnalysis,
                MafExecutorIds.IntakeHitl,
            },
            observation.ExecutorSequence);
        Assert.NotNull(observation.PendingRequest); // suspended at the HITL RequestPort
    }
}
