// MAF executor that reads a feature phase's artifacts (spec-019 T017) — the GA replacement for the SK
// ReadArtifactsStep. Same missing-artifacts failure and Plan-phase planned-item parsing (parity — FR-015).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// First executor of the phase handler: reads the feature directory's artifacts via
/// <see cref="IArtifactReader"/>. On success it forwards the state (with artifacts, and for the Plan
/// phase the parsed planned items) to validation; when no artifacts exist it yields a failed terminal
/// state — the system never fabricates content. Mirrors the retired <c>ReadArtifactsStep</c>.
/// </summary>
[SendsMessage(typeof(PhaseHandlerState))]
[YieldsOutput(typeof(PhaseHandlerState))]
public sealed class ReadArtifactsExecutor : Executor<PhaseHandlerState>
{
    private readonly IArtifactReader _artifactReader;
    private readonly IPhaseProgressSink? _progressSink;

    /// <summary>Creates the executor over the artifact reader and an optional progress sink.</summary>
    public ReadArtifactsExecutor(IArtifactReader artifactReader, IPhaseProgressSink? progressSink = null)
        : base(MafExecutorIds.ReadArtifacts)
    {
        _artifactReader = artifactReader;
        _progressSink = progressSink;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(PhaseHandlerState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var artifacts = await _artifactReader.ReadArtifactsAsync(state.FeatureDirectory);

        if (artifacts.Count == 0)
        {
            var failed = state with
            {
                Status = PhaseRunStatus.Failed,
                FailureReason = $"No artifacts found for feature directory '{state.FeatureDirectory}'.",
            };
            _progressSink?.Report(failed);
            await context.YieldOutputAsync(failed, cancellationToken);
            return;
        }

        // For the Plan phase, parse the planned units of work now so they survive into the paused state
        // and are available when the approved run resumes to create one Task each (parity with the SK step).
        var plannedItems = state.Phase == SpecKitPhase.Plan
            ? PlanArtifactParser.Parse(artifacts)
            : state.PlannedItems;

        var withArtifacts = state with
        {
            Artifacts = artifacts,
            PlannedItems = plannedItems,
            Status = PhaseRunStatus.ArtifactsRead,
        };
        _progressSink?.Report(withArtifacts);
        await context.SendMessageAsync(withArtifacts, cancellationToken);
    }
}
