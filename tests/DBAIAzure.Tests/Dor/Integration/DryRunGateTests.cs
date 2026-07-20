// Integration test for the global dry-run gate (spec-021 T075 / FR-032): with dry-run enabled the workflow runs
// its review + conversation and records the resolution, but performs NO external write — no ticket transition,
// no field update, no chat message.
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Dor.Integration;

public sealed class DryRunGateTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task DryRun_ResolvesConversation_ButPerformsNoExternalWrites()
    {
        using var fixture = new DorStoreFixture();
        var adapter = new RecordingDorAdapter();
        var messaging = new CountingMessaging();

        var orchestrator = new DorWorkflowOrchestrator(
            FixedReview.Fail("acceptance_criteria"),
            new QueueConversationSvc(new ReplyEvaluation(true, Array.Empty<string>(),
                new Dictionary<string, string> { ["acceptance_criteria"] = "AC" }, "thanks")),
            adapter,
            new FakeDoc("DOR"),
            new FixedConfig(DorTestConfig.Standard(dryRun: true)),
            messaging,
            fixture.Store,
            NullLogger<DorWorkflowOrchestrator>.Instance);

        var run = await orchestrator.StartAsync("SBRO-1");
        await run!.WaitSuspendedAsync().WaitAsync(Timeout);
        orchestrator.SubmitReply(run.RunId, "here are the acceptance criteria");
        await run.Completion.WaitAsync(Timeout);

        var instance = await fixture.LoadAsync("SBRO-1");
        Assert.Equal(DorOutcome.ResolvedAuto, instance.Outcome);   // the logic ran and resolved
        Assert.Empty(adapter.Transitions);                          // …but no transition
        Assert.Empty(adapter.WrittenFields);                        // …no field write
        Assert.Equal(0, messaging.Count);                           // …no chat message (outreach gated)
    }
}
