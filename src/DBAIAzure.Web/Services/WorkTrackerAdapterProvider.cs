// Resolves the single active work-tracker adapter from the connector store, per run (spec-018 FR-005; spec-020 D3).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.WorkTracker;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Selects the active adapter by matching its <see cref="IWorkTrackerAdapter.TrackerKey"/> to the provider
/// resolved from the Work Tracking System connector on <b>each call</b> (spec-020) — so an operator switching
/// providers in the UI takes effect on the next resolution without an application restart. A single tracker is
/// active per instance; the <see cref="WorkRoutingContext"/> argument is accepted but unused in v1 (the seam
/// where per-project routing slots in later without changing callers).
/// </summary>
public sealed class WorkTrackerAdapterProvider : IWorkTrackerAdapterProvider
{
    private readonly IReadOnlyList<IWorkTrackerAdapter> _adapters;
    private readonly IWorkTrackerConfigResolver _resolver;

    public WorkTrackerAdapterProvider(IEnumerable<IWorkTrackerAdapter> adapters, IWorkTrackerConfigResolver resolver)
    {
        _adapters = adapters.ToList();
        _resolver = resolver;
    }

    /// <inheritdoc/>
    public IWorkTrackerAdapter GetAdapter(WorkRoutingContext? routingContext = null)
    {
        // Blocking resolve mirrors the established LLM connector hot-reload pattern (Program.cs ResolveActiveConfig).
        var resolved = _resolver.ResolveActiveAsync().GetAwaiter().GetResult();
        return Select(_adapters, resolved.Provider);
    }

    /// <summary>Matches the resolved provider to a registered adapter by its <c>TrackerKey</c>; first as a fallback.</summary>
    internal static IWorkTrackerAdapter Select(IReadOnlyList<IWorkTrackerAdapter> adapters, Core.Models.WorkTrackerProvider provider) =>
        adapters.FirstOrDefault(a => string.Equals(a.TrackerKey, provider.ToString(), StringComparison.OrdinalIgnoreCase))
            ?? adapters.FirstOrDefault()
            ?? throw new InvalidOperationException("No work-tracker adapter is registered.");
}
