// SK step: reads the feature's artifact files via IArtifactReader, or records a failure if none exist.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes; // PlanArtifactParser lives in the parent namespace.
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// First step of the phase handler: resolves the feature directory's artifacts. Emits
/// <see cref="PhaseHandlerEvents.ArtifactsRead"/> on success, or
/// <see cref="PhaseHandlerEvents.Failed"/> with a reason when the directory is missing or empty —
/// the system never fabricates content (FR-003, "missing artifacts" edge case).
/// </summary>
public sealed class ReadArtifactsStep : KernelProcessStep
{
    /// <summary>Reads artifacts for the run's feature directory and routes the next event.</summary>
    [KernelFunction]
    public async Task ReadAsync(KernelProcessStepContext ctx, PhaseHandlerState state, Kernel kernel)
    {
        var artifactReader = kernel.GetRequiredService<IArtifactReader>();
        var sink = kernel.Services.GetService<IPhaseProgressSink>();
        var artifacts = await artifactReader.ReadArtifactsAsync(state.FeatureDirectory);

        if (artifacts.Count == 0)
        {
            var failed = state with
            {
                Status = PhaseRunStatus.Failed,
                FailureReason = $"No artifacts found for feature directory '{state.FeatureDirectory}'.",
            };
            sink?.Report(failed);
            await ctx.EmitEventAsync(new() { Id = PhaseHandlerEvents.Failed, Data = failed });
            return;
        }

        // For the Plan phase, parse the planned units of work now so they survive into the paused
        // state and are available when the approved run resumes to create one Task each (FR-008).
        var plannedItems = state.Phase == SpecKitPhase.Plan
            ? PlanArtifactParser.Parse(artifacts)
            : state.PlannedItems;

        var withArtifacts = state with
        {
            Artifacts = artifacts,
            PlannedItems = plannedItems,
            Status = PhaseRunStatus.ArtifactsRead,
        };
        sink?.Report(withArtifacts);
        await ctx.EmitEventAsync(new() { Id = PhaseHandlerEvents.ArtifactsRead, Data = withArtifacts });
    }
}
