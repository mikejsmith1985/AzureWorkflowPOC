using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Unit tests for PipelineRun state transitions and HITL task completion.
/// All tests are pure in-memory — no SK process runtime required.
/// </summary>
public class PipelineRunTests
{
    private static TicketState SampleTicket => new()
    {
        TicketId = "INC0001",
        Title = "Sample",
        Description = "Sample description.",
    };

    // ── Initial state ──────────────────────────────────────────────────────────

    [Fact]
    public void PipelineRun_InitialStatus_IsRunning()
    {
        var run = new PipelineRun("abc12345", SampleTicket);

        Assert.Equal(PipelineRunStatus.Running, run.Status);
        Assert.Empty(run.Events);
        Assert.Null(run.ErrorMessage);
    }

    // ── AddEvent ──────────────────────────────────────────────────────────────

    [Fact]
    public void AddEvent_EnqueuesEvent()
    {
        var run = new PipelineRun("abc12345", SampleTicket);
        var evt = new PipelineEvent("Intake", "Hello", ReportLevel.Info, DateTimeOffset.UtcNow);

        run.AddEvent(evt);

        Assert.Single(run.Events);
    }

    // ── SetAwaitingHuman / ProvideHitlInput ────────────────────────────────────

    [Fact]
    public async Task SetAwaitingHuman_Then_ProvideInput_UnblocksWaiterAsync()
    {
        var run = new PipelineRun("abc12345", SampleTicket);
        var pausedTicket = SampleTicket with { ClarifyingQuestions = ["What is the AC?"] };

        run.SetAwaitingHuman(pausedTicket);
        Assert.Equal(PipelineRunStatus.AwaitingHuman, run.Status);
        Assert.Single(run.HitlQuestions);

        var waitTask = run.WaitForHitlInputAsync();
        Assert.False(waitTask.IsCompleted);

        run.ProvideHitlInput("The acceptance criteria is X.");
        var answer = await waitTask;

        Assert.Equal("The acceptance criteria is X.", answer);
    }

    // ── SetComplete ────────────────────────────────────────────────────────────

    [Fact]
    public void SetComplete_WithStoryPoints_IsComplete()
    {
        var run = new PipelineRun("abc12345", SampleTicket);
        var finalTicket = SampleTicket with { StoryPoints = 5 };

        run.SetComplete(finalTicket);

        Assert.Equal(PipelineRunStatus.Complete, run.Status);
        Assert.Equal(finalTicket, run.CurrentTicket);
    }

    [Fact]
    public void SetComplete_WithoutStoryPoints_IsBlocked()
    {
        var run = new PipelineRun("abc12345", SampleTicket);

        run.SetComplete(SampleTicket); // StoryPoints is null

        Assert.Equal(PipelineRunStatus.Blocked, run.Status);
    }

    // ── SetFailed ─────────────────────────────────────────────────────────────

    [Fact]
    public void SetFailed_SetsStatusAndMessage()
    {
        var run = new PipelineRun("abc12345", SampleTicket);

        run.SetFailed("Something went wrong.");

        Assert.Equal(PipelineRunStatus.Failed, run.Status);
        Assert.Equal("Something went wrong.", run.ErrorMessage);
    }
}
