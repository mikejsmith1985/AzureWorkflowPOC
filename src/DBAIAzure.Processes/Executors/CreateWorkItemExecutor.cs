// MAF executor that writes the board work item(s) on an approved decision (spec-019 T027) — the GA
// replacement for the SK CreateWorkItemStep. It receives the reviewer-decided state from the approval
// RequestPort and delegates to the shared PhaseWorkItemWriter, so the board write is identical to the SK
// path (parity — FR-015). This is the terminal executor of the phase-handler workflow: it yields the run's
// final state (Completed / Rejected / Unsupported / Failed).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Terminal executor of the phase-handler workflow: on the reviewer's approved <see cref="ApprovalDecision"/>
/// (carried on the resumed <see cref="PhaseHandlerState"/>) it writes/upserts the board work item(s) via the
/// shared <see cref="PhaseWorkItemWriter"/>; a rejected or unsupported run yields without writing (FR-006).
/// Mirrors the retired <c>CreateWorkItemStep</c>.
/// </summary>
[YieldsOutput(typeof(PhaseHandlerState))]
public sealed class CreateWorkItemExecutor : Executor<PhaseHandlerState>
{
    private readonly PhaseWorkItemWriterDeps _deps;
    private readonly IPhaseProgressSink? _progressSink;

    /// <summary>Creates the executor over the resolved board-write dependencies and an optional progress sink.</summary>
    public CreateWorkItemExecutor(PhaseWorkItemWriterDeps deps, IPhaseProgressSink? progressSink = null)
        : base(MafExecutorIds.CreateWorkItem)
    {
        _deps = deps;
        _progressSink = progressSink;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(PhaseHandlerState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var result = await PhaseWorkItemWriter.WriteAsync(state, _deps);
        _progressSink?.Report(result);
        await context.YieldOutputAsync(result, cancellationToken);
    }
}
