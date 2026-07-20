// Reads the active DoR Validation Workflow configuration (six namespaces + secrets) from the connector store on
// every call (spec-021), so operator changes take effect without a restart — mirroring WorkTrackerConfigResolver.
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow.Config;

namespace DBAIAzure.Web.Services.Dor;

/// <summary>
/// Resolves the <see cref="ConnectorType.DorWorkflow"/> connector row per run: parses the non-secret JSON into a
/// <see cref="DorWorkflowConfig"/> and decrypts the referenced secrets into a <see cref="DorWorkflowSecrets"/>.
/// The connector JSON uses snake_case keys (matching the configuration contract); a snake-case naming policy
/// maps them onto the PascalCase records. Best-effort — any store or parse error resolves to unconfigured/empty
/// rather than throwing into the workflow (FR-025/FR-026, Article IX).
/// </summary>
public sealed class DorConfigResolver : IDorConfigResolver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IConnectorConfigRepository _configRepo;
    private readonly ILogger<DorConfigResolver> _logger;

    public DorConfigResolver(IConnectorConfigRepository configRepo, ILogger<DorConfigResolver> logger)
    {
        _configRepo = configRepo;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<DorWorkflowConfig> ResolveActiveAsync(CancellationToken ct = default)
    {
        try
        {
            var connector = await _configRepo.GetAsync(ConnectorType.DorWorkflow, ct);
            if (connector is { IsConfigured: true, NonSecretConfig: { } json }
                && JsonSerializer.Deserialize<DorWorkflowConfig>(json, JsonOptions) is { } parsed)
            {
                return parsed with { IsConfigured = true };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resolving the active DoR workflow configuration failed; treating as unconfigured.");
        }

        return DorWorkflowConfig.Unconfigured;
    }

    /// <inheritdoc/>
    public async Task<DorWorkflowSecrets> ResolveSecretsAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.DorWorkflow, ct);
            if (json is not null
                && JsonSerializer.Deserialize<DorWorkflowSecrets>(json, JsonOptions) is { } secrets)
            {
                return secrets;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resolving DoR workflow secrets failed; treating as none stored.");
        }

        return new DorWorkflowSecrets(null, null, null, null);
    }
}
