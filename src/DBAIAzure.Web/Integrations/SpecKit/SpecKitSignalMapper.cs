// Maps the inbound phase-complete signal into the SK process's initial PhaseHandlerState.
using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Integrations.SpecKit;

/// <summary>
/// Translates a validated <see cref="PhaseSignalPayload"/> into the initial
/// <see cref="PhaseHandlerState"/>. Centralising the mapping keeps the controller thin and the SK
/// process insulated from the wire shape (mirrors ServiceNowMapper in the ticket pipeline).
/// </summary>
public static class SpecKitSignalMapper
{
    /// <summary>
    /// Builds the initial state for a run. The feature directory falls back to the feature key
    /// resolved under the specs root when the signal omits it.
    /// </summary>
    public static PhaseHandlerState ToInitialState(
        PhaseSignalPayload payload, string runId, string specsRoot, string costBindingKey)
    {
        var featureKey = payload.FeatureKey!.Trim();
        var featureDirectory = string.IsNullOrWhiteSpace(payload.FeatureDirectory)
            ? $"{specsRoot.TrimEnd('/')}/{featureKey}"
            : payload.FeatureDirectory!.Trim();

        return new PhaseHandlerState
        {
            RunId = runId,
            FeatureKey = featureKey,
            FeatureDirectory = featureDirectory,
            Phase = PhaseWorkItemMap.Parse(payload.Phase),
            Status = PhaseRunStatus.Received,
            CostBindingKey = costBindingKey,   // minted at intake (spec-017)
            TriggeredBy = string.IsNullOrWhiteSpace(payload.TriggeredBy) ? null : payload.TriggeredBy!.Trim(),
        };
    }
}
