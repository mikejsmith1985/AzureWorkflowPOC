using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
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
}
