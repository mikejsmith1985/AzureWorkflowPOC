// Integration tests for graceful degradation (spec-021 US? / T074 / FR-030): when an external dependency fails
// (Jira read, DoR document, AI review), the workflow ends in a clean manual exit rather than a partial write.
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Dor.Integration;

public sealed class DorResilienceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task JiraReadFailure_EndsInManualExit_NoPartialWrite()
    {
        using var fixture = new DorStoreFixture();
        var adapter = new RecordingDorAdapter { ThrowOnRead = true };
        var orchestrator = Build(fixture, adapter, FixedReview.Fail("acceptance_criteria"), new FakeDoc("DOR"));

        var run = await orchestrator.StartAsync("SBRO-1");
        await run!.Completion.WaitAsync(Timeout);

        var instance = await fixture.LoadAsync("SBRO-1");
        Assert.Equal(DorState.Done, instance.State);
        Assert.Equal(DorOutcome.ManualRequired, instance.Outcome);
        Assert.Empty(adapter.Transitions);
    }

    [Fact]
    public async Task DorDocumentUnavailable_EndsInManualExit()
    {
        using var fixture = new DorStoreFixture();
        var adapter = new RecordingDorAdapter();
        var orchestrator = Build(fixture, adapter, FixedReview.Fail("x"), new FakeDoc(text: null)); // doc load throws

        var run = await orchestrator.StartAsync("SBRO-1");
        await run!.Completion.WaitAsync(Timeout);

        var instance = await fixture.LoadAsync("SBRO-1");
        Assert.Equal(DorOutcome.ManualRequired, instance.Outcome);
    }

    [Fact]
    public async Task AiReviewFailure_AfterRetry_EndsInManualExit()
    {
        using var fixture = new DorStoreFixture();
        var adapter = new RecordingDorAdapter();
        var orchestrator = Build(fixture, adapter, new FixedReview(result: null), new FakeDoc("DOR")); // review always throws

        var run = await orchestrator.StartAsync("SBRO-1");
        await run!.Completion.WaitAsync(Timeout);

        var instance = await fixture.LoadAsync("SBRO-1");
        Assert.Equal(DorOutcome.ManualRequired, instance.Outcome);
        Assert.Empty(adapter.Transitions);
    }

    private static DorWorkflowOrchestrator Build(
        DorStoreFixture fixture, RecordingDorAdapter adapter, FixedReview review, FakeDoc doc) =>
        new(
            review, new QueueConversationSvc(), adapter, doc, new FixedConfig(DorTestConfig.Standard()),
            new CountingMessaging(), fixture.Store, NullLogger<DorWorkflowOrchestrator>.Instance);
}
