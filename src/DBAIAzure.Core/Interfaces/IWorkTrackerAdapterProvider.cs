// Resolves the active work-tracker adapter — the routing seam (spec-018, FR-005).
using DBAIAzure.Core.Models.WorkTracker;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Returns the work-tracker adapter to use. v1 resolves a single active tracker per application instance
/// from configuration; the optional routing context is reserved so per-project / per-workflow routing can
/// be added later as an additive change, without modifying the core layers (FR-005).
/// </summary>
public interface IWorkTrackerAdapterProvider
{
    IWorkTrackerAdapter GetAdapter(WorkRoutingContext? routingContext = null);
}
