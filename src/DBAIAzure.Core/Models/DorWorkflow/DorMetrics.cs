// Derives the operational DoR metrics (spec-021 FR-024) from the append-only instance records. The metrics are
// computed, not stored — proving the recorded data is sufficient for reporting (the dashboard itself is a
// fast-follow). AI latency/cost is derived separately from the shared cost ledger (keyed by run id).
namespace DBAIAzure.Core.Models.DorWorkflow;

/// <summary>The operational metrics derivable from the recorded DoR workflow instances.</summary>
public sealed record DorMetricsSnapshot(
    int ReviewedTotal,
    int PassedCount,
    int ResolvedAutoCount,
    int ManualRequiredCount,
    int EscalatedCount,
    double AutoResolutionRate,
    double ManualExitRate,
    double EscalationRate,
    double MeanResolutionMinutes,
    IReadOnlyList<int> IterationCounts,
    IReadOnlyDictionary<string, int> OutstandingCriteriaFrequency);

/// <summary>Computes <see cref="DorMetricsSnapshot"/> from a set of workflow instances (terminal ones count).</summary>
public static class DorMetrics
{
    /// <summary>Derives the metrics from the given instances; only terminal (Done) instances contribute.</summary>
    public static DorMetricsSnapshot Compute(IReadOnlyList<DorWorkflowInstance> instances)
    {
        var terminal = instances.Where(i => i.State == DorState.Done).ToList();

        var reviewed = terminal.Count;
        var passed = terminal.Count(i => i.Outcome == DorOutcome.Passed);
        var resolvedAuto = terminal.Count(i => i.Outcome == DorOutcome.ResolvedAuto);
        var manual = terminal.Count(i => i.Outcome == DorOutcome.ManualRequired);
        var escalated = terminal.Count(i => i.SlaTier == SlaTier.Escalation);

        var notReady = resolvedAuto + manual; // the tickets that entered the conversation
        var autoResolutionRate = notReady == 0 ? 0 : (double)resolvedAuto / notReady;
        var manualExitRate = reviewed == 0 ? 0 : (double)manual / reviewed;
        var escalationRate = reviewed == 0 ? 0 : (double)escalated / reviewed;

        var completed = terminal.Where(i => i.CompletedAt is not null).ToList();
        var meanResolutionMinutes = completed.Count == 0
            ? 0
            : completed.Average(i => (i.CompletedAt!.Value - i.StartedAt).TotalMinutes);

        var iterationCounts = terminal.Select(i => i.PrimaryIterations + i.EscalationIterations).ToList();

        // Which DoR criteria remained unresolved at terminal — a proxy for the "most-frequently-failing" report.
        var outstandingCriteriaFrequency = terminal
            .SelectMany(i => i.OutstandingGaps)
            .GroupBy(gap => gap)
            .ToDictionary(group => group.Key, group => group.Count());

        return new DorMetricsSnapshot(
            reviewed, passed, resolvedAuto, manual, escalated,
            autoResolutionRate, manualExitRate, escalationRate,
            meanResolutionMinutes, iterationCounts, outstandingCriteriaFrequency);
    }
}
