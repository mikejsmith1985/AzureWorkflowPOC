// Aggregating Definition of Ready validator — evaluates all enabled IWorkflowReadinessRule impls (FR-24).

using DBAIAzure.Core.Configuration;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.Options;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Resolves all registered <see cref="IWorkflowReadinessRule"/> implementations from the DI container,
/// skips rules named in <c>DorRuleSettings.DisabledRuleNames</c>, evaluates the rest in parallel,
/// and returns results with failing rules sorted to the top so the UI renders blockers first.
/// </summary>
public sealed class WorkflowPreRunValidator : IWorkflowPreRunValidator
{
    private readonly IEnumerable<IWorkflowReadinessRule> _rules;
    private readonly DorRuleSettings _settings;
    private readonly IConnectorConfigRepository _connectorRepo;
    private readonly ILogger<WorkflowPreRunValidator> _logger;

    public WorkflowPreRunValidator(
        IEnumerable<IWorkflowReadinessRule> rules,
        IOptions<DorRuleSettings> settings,
        IConnectorConfigRepository connectorRepo,
        ILogger<WorkflowPreRunValidator> logger)
    {
        _rules         = rules;
        _settings      = settings.Value;
        _connectorRepo = connectorRepo;
        _logger        = logger;
    }

   /// <inheritdoc />
    public async Task<IReadOnlyList<DorRuleResult>> ValidateAsync(
        WorkflowDefinition workflow,
        CancellationToken ct = default)
    {
        var disabledSet = _settings.DisabledRuleNames.ToHashSet(StringComparer.Ordinal);
        var enabledRules = _rules.Where(r => !disabledSet.Contains(r.RuleName)).ToList();

        // Snapshot connector health once so all rules see a consistent state.
        var connectors = await LoadConnectorsAsync(ct);

        var tasks = enabledRules.Select(rule => SafeEvaluateAsync(rule, workflow, connectors, ct));
        var results = await Task.WhenAll(tasks);

        // Failing rules first so the UI shows blockers without re-sorting.
        return results
            .OrderBy(r => r.Passed ? 1 : 0)
            .ToList()
            .AsReadOnly();
    }

    private async Task<DorRuleResult> SafeEvaluateAsync(
        IWorkflowReadinessRule rule,
        WorkflowDefinition workflow,
        IReadOnlyList<ConnectorConfig> connectors,
        CancellationToken ct)
    {
        try
        {
            return await rule.CheckAsync(workflow, connectors, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DoR rule {RuleName} threw — treating as failed", rule.RuleName);
            return new DorRuleResult(Passed: false, RuleName: rule.RuleName,
                FailureReason: "Rule evaluation failed unexpectedly. Check application logs.");
        }
    }

    // Loads all connector config records from storage (no live check — uses last known health state).
    private async Task<IReadOnlyList<ConnectorConfig>> LoadConnectorsAsync(CancellationToken ct)
    {
        try
        {
            return await _connectorRepo.GetAllAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load connector configs for DoR validation");
            return Array.Empty<ConnectorConfig>();
        }
    }
}
