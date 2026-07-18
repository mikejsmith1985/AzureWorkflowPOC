// Reads the active Work Tracking System connector (provider + credentials) from the store per run (spec-020, D3).
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Resolves the active <see cref="ConnectorType.WorkTracker"/> connector on every call by reading the
/// connector store, decrypting its secret, and parsing the <c>provider</c> discriminator from the non-secret
/// JSON. When no configured row exists it falls back to the <c>WorkTracker:Active</c> seed value so the
/// pipeline still resolves a default provider (Azure DevOps) for a fresh install. Best-effort — any store or
/// crypto error yields an unconfigured result rather than throwing into the pipeline (FR-012).
/// </summary>
public sealed class WorkTrackerConfigResolver : IWorkTrackerConfigResolver
{
    /// <summary>Config key naming the seed provider used before a WorkTracker connector row is saved.</summary>
    private const string SeedProviderConfigKey = "WorkTracker:Active";

    /// <summary>Name of the provider discriminator field inside the connector's non-secret JSON.</summary>
    private const string ProviderFieldName = "provider";

    private readonly IConnectorConfigRepository _configRepo;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WorkTrackerConfigResolver> _logger;

    public WorkTrackerConfigResolver(
        IConnectorConfigRepository configRepo,
        IConfiguration configuration,
        ILogger<WorkTrackerConfigResolver> logger)
    {
        _configRepo = configRepo;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ResolvedWorkTrackerConfig> ResolveActiveAsync(CancellationToken ct = default)
    {
        try
        {
            var connector = await _configRepo.GetAsync(ConnectorType.WorkTracker, ct);

            // A configured connector with a readable provider discriminator is the authoritative source.
            if (connector is { IsConfigured: true, NonSecretConfig: { } nonSecretJson }
                && TryReadProvider(nonSecretJson, out var provider))
            {
                var secret = await _configRepo.GetDecryptedSecretsAsync(ConnectorType.WorkTracker, ct);
                return new ResolvedWorkTrackerConfig(provider, nonSecretJson, secret, IsConfigured: true);
            }
        }
        catch (Exception ex)
        {
            // Store/crypto failure must never break a run — resolve to the seed default, logged for diagnosis.
            _logger.LogWarning(ex, "Resolving the active work tracker failed; using the seed default.");
        }

        // No configured connector yet — pick the seed provider so a default adapter can still be selected.
        return new ResolvedWorkTrackerConfig(ResolveSeedProvider(), NonSecretJson: null, DecryptedSecret: null, IsConfigured: false);
    }

    /// <summary>Reads the <c>provider</c> discriminator from the connector's non-secret JSON, if present.</summary>
    private static bool TryReadProvider(string nonSecretJson, out WorkTrackerProvider provider)
    {
        provider = WorkTrackerProvider.AzureDevOps;
        try
        {
            using var document = JsonDocument.Parse(nonSecretJson);
            if (document.RootElement.TryGetProperty(ProviderFieldName, out var providerElement)
                && providerElement.GetString() is { Length: > 0 } providerName)
            {
                return Enum.TryParse(providerName, ignoreCase: true, out provider);
            }
        }
        catch (JsonException)
        {
            // Malformed JSON is treated as "no provider" — caller falls back to the seed default.
        }
        return false;
    }

    /// <summary>Resolves the seed provider from configuration, defaulting to Azure DevOps.</summary>
    private WorkTrackerProvider ResolveSeedProvider()
    {
        var seed = _configuration[SeedProviderConfigKey];
        return Enum.TryParse<WorkTrackerProvider>(seed, ignoreCase: true, out var provider)
            ? provider
            : WorkTrackerProvider.AzureDevOps;
    }
}
