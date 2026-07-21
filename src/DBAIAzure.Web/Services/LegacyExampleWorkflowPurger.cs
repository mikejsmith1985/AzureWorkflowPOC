// Removes the pre-spec-021 "Support Request Flow" example workflow at startup so it can never resurrect as the
// builder's most-recently-edited workflow. Spec-021 replaced that demo with the DoR Validation Workflow as the
// sole starter; a saved copy left over from earlier exploration would otherwise keep auto-loading.
using DBAIAzure.Core.Interfaces;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Deletes any saved copy of the legacy "Example: Support Request Flow" workflow for the demo owner. Runs once at
/// startup (idempotent — a no-op when none exist) so the Workflow Builder resumes a real workflow instead of the
/// removed example. Matches on the legacy name prefix to also catch the <c>MakeNameUnique</c> variants
/// (e.g. "… (2)").
/// </summary>
public sealed class LegacyExampleWorkflowPurger
{
    // The exact display name the pre-spec-021 builder gave its example; MakeNameUnique may append " (2)", etc.
    private const string LegacyExampleNamePrefix = "Example: Support Request Flow";

    // The single-tenant demo owner the builder uses throughout.
    private const string DemoOwnerId = "demo";

    private readonly IWorkflowRepository _repository;
    private readonly ILogger<LegacyExampleWorkflowPurger> _logger;

    public LegacyExampleWorkflowPurger(IWorkflowRepository repository, ILogger<LegacyExampleWorkflowPurger> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Deletes every stored legacy example workflow; returns how many were removed.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var workflows = await _repository.ListByOwnerAsync(DemoOwnerId, cancellationToken);
        var legacyExamples = workflows
            .Where(workflow => workflow.Name.StartsWith(LegacyExampleNamePrefix, StringComparison.Ordinal))
            .ToList();

        foreach (var legacyExample in legacyExamples)
            await _repository.DeleteAsync(legacyExample.Id, DemoOwnerId, cancellationToken);

        if (legacyExamples.Count > 0)
            _logger.LogInformation(
                "Removed {Count} legacy example workflow(s); the DoR Validation Workflow is the starter now.",
                legacyExamples.Count);

        return legacyExamples.Count;
    }
}
