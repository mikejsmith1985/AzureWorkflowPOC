// spec-017 T018 (US2, FR-008): a run contributes exactly ONE runtime cost entry, on the anchor work item —
// a Plan run that creates ten Tasks must not bill the same tokens ten times.

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Tests.Fakes;
using Xunit;

namespace DBAIAzure.Tests.CostTracking;

/// <summary>
/// Proves the runtime cost of a run is recorded once, against the anchor, no matter how many work items the
/// run created. The tokens belong to the run, not to each child it produced, so per-child appends would
/// multiply a single spend by the number of planned tasks and corrupt every rollup above it.
/// </summary>
public sealed class RuntimeCostSingleEntryTests
{
    private const string BindingKey = "BIND-7K3QF2AB";

    /// <summary>Records every append so a test can count them and inspect what they were charged to.</summary>
    private sealed class RecordingLedger : ICostLedger
    {
        public List<CostLedgerEntry> Entries { get; } = [];

        public Task AppendAsync(CostLedgerEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<CostTotals> GetTotalsAsync(string bindingKey, CancellationToken ct = default)
            => Task.FromResult(new CostTotals(0, 0));
    }

    /// <summary>Records which work item the cumulative cost fields were projected onto.</summary>
    private sealed class RecordingProjection : ICostProjection
    {
        public List<WorkItemRef> Projected { get; } = [];

        public Task ProjectAsync(string bindingKey, WorkItemRef workItem, CancellationToken ct = default)
        {
            Projected.Add(workItem);
            return Task.CompletedTask;
        }
    }

    /// <summary>Returns a fixed, non-zero token aggregate so the estimated cost is deterministic.</summary>
    private sealed class StubTelemetrySource : IRunTelemetrySource
    {
        public Task<RunTelemetryAggregate> GetAggregateAsync(string runId, CancellationToken ct = default)
            => Task.FromResult(RunTelemetryAggregate.Empty(runId) with
            {
                ModelName    = "claude-opus-5",
                InputTokens  = 10_000,
                OutputTokens = 2_000,
            });
    }

    private static PhaseHandlerState PlanState(int plannedTaskCount) => new()
    {
        RunId            = "run-0002",
        FeatureKey       = "021-feature",
        FeatureDirectory = "specs/021-feature",
        Phase            = SpecKitPhase.Plan,
        CostBindingKey   = BindingKey,
        Decision         = new ApprovalDecision { IsApproved = true, DecidedBy = "reviewer" },
        PlannedItems     = Enumerable.Range(1, plannedTaskCount)
            .Select(i => new PlannedWorkItem { Title = $"Task {i}", Description = $"Unit of work {i}" })
            .ToList(),
    };

    private static (PhaseWorkItemWriterDeps Deps, RecordingLedger Ledger, RecordingProjection Projection) BuildDeps()
    {
        var ledger     = new RecordingLedger();
        var projection = new RecordingProjection();
        var deps = new PhaseWorkItemWriterDeps(
            Tracker:         new FakeWorkTrackerAdapter(),
            Ledger:          ledger,
            TelemetrySource: new StubTelemetrySource(),
            Projection:      projection);
        return (deps, ledger, projection);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public async Task PlanRun_AppendsExactlyOneRuntimeEntry_RegardlessOfTaskCount(int plannedTaskCount)
    {
        var (deps, ledger, _) = BuildDeps();

        var result = await PhaseWorkItemWriter.WriteAsync(PlanState(plannedTaskCount), deps);

        Assert.Equal(PhaseRunStatus.Completed, result.Status);
        Assert.True(result.CreatedWorkItems.Count >= plannedTaskCount,
            "the run should have created at least one work item per planned task");

        // One run, one charge — the count must not track the number of children created.
        var runtimeEntries = ledger.Entries.Where(e => e.Dimension == CostDimension.Runtime).ToList();
        Assert.Single(runtimeEntries);
    }

    [Fact]
    public async Task RuntimeEntry_CarriesTheRunsBindingKeyAndSourceId()
    {
        var (deps, ledger, _) = BuildDeps();
        var state = PlanState(plannedTaskCount: 3);

        await PhaseWorkItemWriter.WriteAsync(state, deps);

        var entry = Assert.Single(ledger.Entries.Where(e => e.Dimension == CostDimension.Runtime));
        Assert.Equal(BindingKey, entry.BindingKey);
        Assert.Equal(state.RunId, entry.SourceId);
        Assert.Equal("claude-opus-5", entry.ModelName);
        Assert.Equal(10_000, entry.InputTokens);
    }

    [Fact]
    public async Task RuntimeEntry_AndProjection_LandOnTheSameSingleAnchor()
    {
        // The projected rollup must target the very item the ledger charged, or the two disagree.
        var (deps, ledger, projection) = BuildDeps();

        await PhaseWorkItemWriter.WriteAsync(PlanState(plannedTaskCount: 4), deps);

        var entry = Assert.Single(ledger.Entries.Where(e => e.Dimension == CostDimension.Runtime));
        var projected = Assert.Single(projection.Projected);
        Assert.Equal(projected.Value, entry.WorkItemId);
    }

    [Fact]
    public async Task RunWithoutABindingKey_AppendsNothing()
    {
        // No binding key means the spend cannot be attributed; recording it anywhere would be a guess.
        var (deps, ledger, projection) = BuildDeps();
        var state = PlanState(plannedTaskCount: 2) with { CostBindingKey = null };

        var result = await PhaseWorkItemWriter.WriteAsync(state, deps);

        Assert.Equal(PhaseRunStatus.Completed, result.Status);
        Assert.Empty(ledger.Entries);
        Assert.Empty(projection.Projected);
    }
}
