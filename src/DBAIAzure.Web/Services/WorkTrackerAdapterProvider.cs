// Resolves the single active work-tracker adapter from configuration (spec-018, FR-005).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.WorkTracker;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Selects the active adapter by its <see cref="IWorkTrackerAdapter.TrackerKey"/> from the
/// <c>WorkTracker:Active</c> configuration value (default <c>AzureDevOps</c>) — a single active tracker per
/// instance. The <see cref="WorkRoutingContext"/> argument is accepted but unused in v1; it is the seam
/// where per-project / per-workflow routing slots in later without changing callers.
/// </summary>
public sealed class WorkTrackerAdapterProvider : IWorkTrackerAdapterProvider
{
    private const string DefaultTrackerKey = "AzureDevOps";

    private readonly IReadOnlyList<IWorkTrackerAdapter> _adapters;
    private readonly string _activeKey;

    public WorkTrackerAdapterProvider(IEnumerable<IWorkTrackerAdapter> adapters, IConfiguration configuration)
    {
        _adapters = adapters.ToList();
        _activeKey = configuration["WorkTracker:Active"] is { Length: > 0 } configured
            ? configured
            : DefaultTrackerKey;
    }

    /// <inheritdoc/>
    public IWorkTrackerAdapter GetAdapter(WorkRoutingContext? routingContext = null) =>
        _adapters.FirstOrDefault(a => string.Equals(a.TrackerKey, _activeKey, StringComparison.OrdinalIgnoreCase))
            ?? _adapters.FirstOrDefault()
            ?? throw new InvalidOperationException("No work-tracker adapter is registered.");
}
