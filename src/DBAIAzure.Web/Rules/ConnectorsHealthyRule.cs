// DoR rule: all connectors bound to realized nodes must be healthy (FR-24.2).

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Rules;

/// <summary>
/// Blocks a run when any connector that a realized node depends on is not healthy.
/// Prevents runs from reaching a connector step only to fail immediately.
/// </summary>
public sealed class ConnectorsHealthyRule : IWorkflowReadinessRule
{
    public string RuleName    => "connectors-healthy";
    public string Description => "All connectors bound to realized nodes must pass their health check.";

    public Task<DorRuleResult> CheckAsync(
        WorkflowDefinition workflow,
        IReadOnlyList<ConnectorConfig> connectors,
        CancellationToken ct = default)
    {
        // Find any connector that is configured but whose last health check failed.
        var unhealthy = connectors
            .Where(c => c.IsConfigured && c.LastTestResult is { IsSuccess: false })
            .Select(c => c.Type.ToString())
            .ToList();

        if (unhealthy.Count == 0)
            return Task.FromResult(new DorRuleResult(Passed: true, RuleName: RuleName, FailureReason: null));

        var names = string.Join(", ", unhealthy);
        return Task.FromResult(new DorRuleResult(Passed: false, RuleName: RuleName,
            FailureReason: $"The following connectors are unhealthy: {names}. Run a health check from Settings → Connectors."));
    }
}
