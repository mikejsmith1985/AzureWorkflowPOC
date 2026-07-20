// Functional health check for the DoR Validation Workflow connector (spec-021 US6). Verifies the workflow is
// configured, its settings are internally valid, and the DoR document actually loads — the DoR-specific health
// that the per-connector Jira/Messaging/LLM testers don't cover.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Connectors;

/// <summary>
/// Tests the DoR workflow connector: it must be configured, pass configuration validation (source-type,
/// business-hours, transition, projects), and its DoR document must load. Reports an actionable pass/fail on the
/// existing <see cref="IConnectorHealthChecker"/> seam.
/// </summary>
public sealed class DorWorkflowTester
{
    private readonly IDorConfigResolver _configResolver;
    private readonly IDorDocumentSource _documentSource;

    public DorWorkflowTester(IDorConfigResolver configResolver, IDorDocumentSource documentSource)
    {
        _configResolver = configResolver;
        _documentSource = documentSource;
    }

    /// <summary>Runs the DoR workflow health check and returns an actionable pass/fail result.</summary>
    public async Task<ConnectorTestResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        ConnectorTestResult Fail(string message) =>
            new(ConnectorType.DorWorkflow, false, message, DateTimeOffset.UtcNow);

        var config = await _configResolver.ResolveActiveAsync(cancellationToken);
        if (!config.IsConfigured)
            return Fail("DoR workflow is not configured — enter its settings and save.");

        var issues = DorConfigValidation.Validate(config);
        if (issues.Count > 0)
            return Fail("Configuration issues: " + string.Join("; ", issues));

        try
        {
            var document = await _documentSource.LoadAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(document.Text))
                return Fail("The DoR document loaded but is empty — check the DoR source.");
        }
        catch (Exception ex)
        {
            return Fail($"The DoR document could not be loaded: {ex.Message}");
        }

        return new ConnectorTestResult(
            ConnectorType.DorWorkflow, true,
            "DoR workflow configuration is valid and the DoR document loads.",
            DateTimeOffset.UtcNow);
    }
}
