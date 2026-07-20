// The DoR review executor (spec-021): runs the AI review against the DoR document and sets the routing flag the
// graph uses to branch to the pass or fail path.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>
/// Evaluates the hydrated ticket against the DoR document via <see cref="IDorReviewService"/> and records the
/// verdict on the payload (<see cref="DorRunState.ReviewPassed"/> + outstanding gaps). A malformed model result
/// is retried once before failing. Also used on the reply-evaluation loop-back in later increments.
/// </summary>
[SendsMessage(typeof(DorRunState))]
public sealed class DorReviewExecutor : Executor<DorRunState>
{
    private readonly IDorReviewService _reviewService;
    private readonly IDorConfigResolver _configResolver;
    private readonly IDorWorkflowInstanceStore _instanceStore;

    public DorReviewExecutor(
        IDorReviewService reviewService, IDorConfigResolver configResolver, IDorWorkflowInstanceStore instanceStore)
        : base(MafExecutorIds.DorReview)
    {
        _reviewService = reviewService;
        _configResolver = configResolver;
        _instanceStore = instanceStore;
    }

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(DorRunState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = await _configResolver.ResolveActiveAsync(cancellationToken);

        DorReviewResult result;
        try
        {
            result = await _reviewService.ReviewAsync(state.Fields, state.DorDocumentText, config.Ai, cancellationToken);
        }
        catch
        {
            // One bounded corrective retry before giving up (edge case: malformed model output — FR-030).
            result = await _reviewService.ReviewAsync(state.Fields, state.DorDocumentText, config.Ai, cancellationToken);
        }

        var gaps = result.MissingFields.Count > 0
            ? result.MissingFields
            : result.Criteria
                .Where(c => !string.Equals(c.Status, "PASS", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.Name)
                .ToList();

        var next = state with
        {
            ReviewPassed = result.IsPass,
            OutstandingGaps = result.IsPass ? Array.Empty<string>() : gaps,
            State = result.IsPass ? DorState.Passed : DorState.Failed,
        };

        await _instanceStore.UpdateAsync(DorInstanceMapper.ToInstance(next), cancellationToken);
        await context.SendMessageAsync(next, cancellationToken);
    }
}
