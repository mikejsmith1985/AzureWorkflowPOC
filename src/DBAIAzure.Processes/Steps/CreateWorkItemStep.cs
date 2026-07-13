// SK step: creates the board work item(s) — but only on an approved decision (FR-006), recording
// a board-write failure rather than discarding the approval (FR-015). The board-write logic itself lives
// in the framework-neutral PhaseWorkItemWriter so the SK step and the MAF CreateWorkItemExecutor write the
// board identically (spec-019); this step resolves the writer's dependencies from the kernel and maps the
// resulting state onto the SK process event.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Writes the tracking work item(s) to the board, gated on an approved <see cref="ApprovalDecision"/>.
/// Delegates the actual create/upsert + cost/telemetry write-back to <see cref="PhaseWorkItemWriter"/>
/// (shared with the MAF runtime), then reports the resulting state and emits the matching process event.
/// </summary>
public sealed class CreateWorkItemStep : KernelProcessStep
{
    /// <summary>Creates or upserts the work item(s) if approved; rejects, fails, or no-ops otherwise.</summary>
    [KernelFunction]
    public async Task CreateAsync(KernelProcessStepContext ctx, PhaseHandlerState state, Kernel kernel)
    {
        var sink = kernel.Services.GetService<IPhaseProgressSink>();

        // Resolve the board-write dependencies from the kernel's container. The tracker is only required
        // when an approved, supported phase actually writes — the writer guards those cases itself, so a
        // missing tracker on a rejected/unsupported run is fine (GetService, not GetRequiredService).
        var deps = new PhaseWorkItemWriterDeps(
            Tracker:         kernel.Services.GetService<IWorkTrackerAdapter>()!,
            Repository:      kernel.Services.GetService<IPhaseRunRepository>(),
            BindingMap:      kernel.Services.GetService<IBindingWorkItemMap>(),
            Ledger:          kernel.Services.GetService<ICostLedger>(),
            TelemetrySource: kernel.Services.GetService<IRunTelemetrySource>(),
            Projection:      kernel.Services.GetService<ICostProjection>(),
            WriteBack:       kernel.Services.GetService<ITelemetryWriteBack>());

        var result = await PhaseWorkItemWriter.WriteAsync(state, deps);

        sink?.Report(result);
        await ctx.EmitEventAsync(new() { Id = MapEvent(result.Status), Data = result });
    }

    /// <summary>Maps the writer's terminal status onto the phase-handler process event id.</summary>
    private static string MapEvent(PhaseRunStatus status) => status switch
    {
        PhaseRunStatus.Unsupported => PhaseHandlerEvents.Unsupported,
        PhaseRunStatus.Failed      => PhaseHandlerEvents.Failed,
        // Completed (created) and Rejected (no write) both leave the write stage the same way.
        _                          => PhaseHandlerEvents.WorkItemWritten,
    };
}
