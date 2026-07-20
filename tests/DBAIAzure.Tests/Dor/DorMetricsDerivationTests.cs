// Unit tests proving the operational DoR metrics (spec-021 FR-024 / T073) are derivable from the recorded
// instance data alone — no separate metric store is needed.
using DBAIAzure.Core.Models.DorWorkflow;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DorMetricsDerivationTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);

    private static DorWorkflowInstance Terminal(
        DorOutcome outcome, SlaTier tier, int primaryIters, int escalationIters, params string[] outstandingGaps) => new()
    {
        RunId = Guid.NewGuid().ToString("n"),
        TicketKey = "SBRO-1",
        State = DorState.Done,
        Outcome = outcome,
        SlaTier = tier,
        PrimaryIterations = primaryIters,
        EscalationIterations = escalationIters,
        OutstandingGaps = outstandingGaps,
        StartedAt = Start,
        CompletedAt = Start.AddMinutes(10),
    };

    [Fact]
    public void Compute_DerivesAllCoreMetrics_FromInstances()
    {
        var instances = new[]
        {
            Terminal(DorOutcome.Passed, SlaTier.Primary, 0, 0),
            Terminal(DorOutcome.ResolvedAuto, SlaTier.Primary, 2, 0),
            Terminal(DorOutcome.ResolvedAuto, SlaTier.Escalation, 1, 1),
            Terminal(DorOutcome.ManualRequired, SlaTier.Escalation, 3, 2, "acceptance_criteria"),
        };

        var metrics = DorMetrics.Compute(instances);

        Assert.Equal(4, metrics.ReviewedTotal);
        Assert.Equal(1, metrics.PassedCount);
        Assert.Equal(2, metrics.ResolvedAutoCount);
        Assert.Equal(1, metrics.ManualRequiredCount);
        Assert.Equal(2, metrics.EscalatedCount);                          // the two escalation-tier instances
        Assert.Equal(2.0 / 3.0, metrics.AutoResolutionRate, precision: 6); // 2 resolved of 3 not-ready
        Assert.Equal(0.25, metrics.ManualExitRate, precision: 6);          // 1 of 4
        Assert.Equal(0.5, metrics.EscalationRate, precision: 6);           // 2 of 4
        Assert.Equal(10.0, metrics.MeanResolutionMinutes, precision: 6);
        Assert.Equal(new[] { 0, 2, 2, 5 }, metrics.IterationCounts);       // primary+escalation per instance
        Assert.Equal(1, metrics.OutstandingCriteriaFrequency["acceptance_criteria"]);
    }

    [Fact]
    public void Compute_IgnoresNonTerminalInstances()
    {
        var instances = new[]
        {
            Terminal(DorOutcome.Passed, SlaTier.Primary, 0, 0),
            new DorWorkflowInstance { RunId = "x", TicketKey = "T", State = DorState.AwaitingResponse, StartedAt = Start },
        };

        Assert.Equal(1, DorMetrics.Compute(instances).ReviewedTotal);
    }
}
