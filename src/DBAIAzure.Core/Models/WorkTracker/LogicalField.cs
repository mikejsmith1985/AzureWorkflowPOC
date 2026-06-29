// Tracker-neutral logical field names — the cost/telemetry layers use these instead of Custom.* / customfield_*.
namespace DBAIAzure.Core.Models.WorkTracker;

/// <summary>
/// The telemetry + cost fields, named tracker-neutrally. Each adapter resolves these to its native field
/// reference (Azure DevOps <c>Custom.&lt;name&gt;</c>; Jira <c>customfield_*</c> resolved by display name).
/// </summary>
public static class LogicalField
{
    public const string AISessionID = "AISessionID";
    public const string AIModelUsed = "AIModelUsed";
    public const string AITriggeredBy = "AITriggeredBy";
    public const string CostBindingKey = "CostBindingKey";
    public const string AIRuntimeCostUSD = "AIRuntimeCostUSD";
    public const string AIDevCostUSD = "AIDevCostUSD";
    public const string AIInputTokens = "AIInputTokens";
    public const string AIOutputTokens = "AIOutputTokens";
    public const string AICacheTokens = "AICacheTokens";
    public const string AIEstimatedCostUSD = "AIEstimatedCostUSD";
    public const string AISessionDurationSec = "AISessionDurationSec";
    public const string AIToolCalls = "AIToolCalls";
    public const string AIToolAcceptRatePct = "AIToolAcceptRatePct";
    public const string AIAPIErrors = "AIAPIErrors";
    public const string AICacheHitRatePct = "AICacheHitRatePct";
    public const string SpeckitPhase = "SpeckitPhase";
}
