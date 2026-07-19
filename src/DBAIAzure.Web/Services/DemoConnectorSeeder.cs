// Boot-time seeding of the demo's back-office connectors from environment configuration. This is the
// .NET analogue of the reference app's startup_configure.py: on each (ephemeral) cold start it
// re-populates the ServiceNow, Azure DevOps, and Messaging connectors from vault-injected env vars so
// the demo works out of the box — while deliberately never seeding the LLM connector, which each
// visitor configures with their own key (FR-004 / SC-006).
using System.Text.Json;
using DBAIAzure.Core.Configuration;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.Options;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Seeds the demo's back-office connectors (ServiceNow, Azure DevOps, Messaging) from
/// <see cref="ConnectorSeedOptions"/> at startup, writing rows through the same
/// <see cref="IConnectorConfigRepository"/> the Settings UI uses (so seeded rows are indistinguishable
/// from UI-configured ones and secrets are encrypted at rest). The LLM connector is never seeded —
/// it is the single credential each visitor supplies themselves.
/// </summary>
public sealed class DemoConnectorSeeder
{
    private readonly IConnectorConfigRepository _repository;
    private readonly ConnectorSeedOptions _options;
    private readonly ILogger<DemoConnectorSeeder> _logger;

    /// <summary>Creates the seeder. Dependencies are the existing connector repository and the bound seed options.</summary>
    public DemoConnectorSeeder(
        IConnectorConfigRepository repository,
        IOptions<ConnectorSeedOptions> options,
        ILogger<DemoConnectorSeeder> logger)
    {
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Seeds the three back-office connectors from configuration. A connector whose required values
    /// are missing is left unconfigured (logged, never half-written); a connector already configured
    /// in this container lifetime is left untouched so a visitor's runtime repoint is never clobbered.
    /// Never seeds the LLM connector. Secret values are never written to the log.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedServiceNowAsync(cancellationToken);
        await SeedWorkTrackerAsync(cancellationToken);
        await SeedMessagingAsync(cancellationToken);
        // The LLM connector is intentionally never seeded (FR-004 / SC-006).
    }

    /// <summary>
    /// Seeds the generic Work Tracking System connector (spec-020). Prefers the <c>WorkTracker</c> seed
    /// section; when it is absent, falls back to the legacy <c>AzureDevOps</c> section (mapped to
    /// provider = Azure DevOps). Writes the discriminated non-secret JSON + the provider's secret. Skips
    /// when already configured unless the seed sets <c>Overwrite</c> (used to re-point in a live test).
    /// </summary>
    private async Task SeedWorkTrackerAsync(CancellationToken cancellationToken)
    {
        var seed = _options.WorkTracker;
        var provider = (seed.Provider ?? string.Empty).Trim();

        // Resolve the (nonSecret, secret) pair for whichever provider is configured.
        (string? nonSecret, string? secret) = provider.Equals("Jira", StringComparison.OrdinalIgnoreCase)
            ? BuildJiraSeed(seed)
            : BuildAzureDevOpsSeed(seed);

        if (nonSecret is null || secret is null)
        {
            LogSkipped(ConnectorType.WorkTracker);
            return;
        }

        if (!seed.Overwrite && await IsAlreadyConfiguredAsync(ConnectorType.WorkTracker, cancellationToken))
            return;

        await _repository.SaveAsync(ConnectorType.WorkTracker, nonSecret, secret, cancellationToken);
        LogSeeded(ConnectorType.WorkTracker);
    }

    /// <summary>Jira non-secret/secret pair, or (null,null) when required Jira fields are missing.</summary>
    private static (string?, string?) BuildJiraSeed(WorkTrackerSeed seed)
    {
        if (IsBlank(seed.SiteUrl) || IsBlank(seed.Email) || IsBlank(seed.ApiToken))
            return (null, null);
        var nonSecret = JsonSerializer.Serialize(new
        {
            provider = "Jira", siteUrl = seed.SiteUrl, email = seed.Email, projectKey = seed.ProjectKey ?? string.Empty,
        });
        var secret = JsonSerializer.Serialize(new { apiToken = seed.ApiToken });
        return (nonSecret, secret);
    }

    /// <summary>Azure DevOps non-secret/secret pair from the WorkTracker seed, or the legacy AzureDevOps
    /// section, or (null,null) when required ADO fields are missing.</summary>
    private (string?, string?) BuildAzureDevOpsSeed(WorkTrackerSeed seed)
    {
        var orgUrl  = IsBlank(seed.OrganizationUrl) ? _options.AzureDevOps.OrganizationUrl : seed.OrganizationUrl;
        var project = IsBlank(seed.ProjectName) ? _options.AzureDevOps.ProjectName : seed.ProjectName;
        var pat     = IsBlank(seed.PersonalAccessToken) ? _options.AzureDevOps.PersonalAccessToken : seed.PersonalAccessToken;

        if (IsBlank(orgUrl) || IsBlank(project) || IsBlank(pat))
            return (null, null);

        var nonSecret = JsonSerializer.Serialize(new { provider = "AzureDevOps", organizationUrl = orgUrl, projectName = project });
        var secret = JsonSerializer.Serialize(new { personalAccessToken = pat });
        return (nonSecret, secret);
    }

    private async Task SeedServiceNowAsync(CancellationToken cancellationToken)
    {
        var seed = _options.ServiceNow;
        if (IsBlank(seed.InstanceUrl) || IsBlank(seed.Username) || IsBlank(seed.Password))
        {
            LogSkipped(ConnectorType.ServiceNow);
            return;
        }

        if (await IsAlreadyConfiguredAsync(ConnectorType.ServiceNow, cancellationToken))
            return;

        var nonSecret = JsonSerializer.Serialize(new { instanceUrl = seed.InstanceUrl, username = seed.Username });
        var secret = JsonSerializer.Serialize(new { password = seed.Password });
        await _repository.SaveAsync(ConnectorType.ServiceNow, nonSecret, secret, cancellationToken);
        LogSeeded(ConnectorType.ServiceNow);
    }

    private async Task SeedMessagingAsync(CancellationToken cancellationToken)
    {
        var seed = _options.Messaging;

        // Messaging needs a platform plus at least one delivery path (a webhook URL or an MCP server).
        var hasDeliveryPath = !IsBlank(seed.WebhookUrl) || !IsBlank(seed.McpServerUrl);
        if (IsBlank(seed.Platform) || !hasDeliveryPath || !TryNormalizePlatform(seed.Platform!, out var platform))
        {
            LogSkipped(ConnectorType.Messaging);
            return;
        }

        if (await IsAlreadyConfiguredAsync(ConnectorType.Messaging, cancellationToken))
            return;

        var nonSecret = JsonSerializer.Serialize(new
        {
            platform,
            mcpServerUrl = NullIfBlank(seed.McpServerUrl),
            mcpToolName = NullIfBlank(seed.McpToolName),
            mcpArgumentTemplate = NullIfBlank(seed.McpArgumentTemplate),
            target = NullIfBlank(seed.Target),
        });

        var secret = SerializeMessagingSecret(seed);
        await _repository.SaveAsync(ConnectorType.Messaging, nonSecret, secret, cancellationToken);
        LogSeeded(ConnectorType.Messaging);
    }

    /// <summary>Builds the Messaging secret JSON (webhook URL + MCP token), or null when neither is present.</summary>
    private static string? SerializeMessagingSecret(MessagingSeed seed)
    {
        var hasWebhook = !IsBlank(seed.WebhookUrl);
        var hasToken = !IsBlank(seed.McpAuthToken);
        if (!hasWebhook && !hasToken)
            return null;

        return JsonSerializer.Serialize(new
        {
            webhookUrl = hasWebhook ? seed.WebhookUrl : null,
            mcpAuthToken = hasToken ? seed.McpAuthToken : null,
        });
    }

    /// <summary>Returns true when the connector already carries configuration in this lifetime (do not clobber a visitor repoint).</summary>
    private async Task<bool> IsAlreadyConfiguredAsync(ConnectorType type, CancellationToken cancellationToken)
    {
        var existing = await _repository.GetAsync(type, cancellationToken);
        if (existing?.IsConfigured == true)
        {
            _logger.LogInformation("{Connector} connector already configured — leaving it untouched.", type);
            return true;
        }
        return false;
    }

    /// <summary>Normalizes a platform name to the canonical <see cref="MessagingPlatform"/> spelling, or fails for unknown values.</summary>
    private static bool TryNormalizePlatform(string value, out string canonical)
    {
        if (Enum.TryParse<MessagingPlatform>(value, ignoreCase: true, out var parsed))
        {
            canonical = parsed.ToString();
            return true;
        }
        canonical = string.Empty;
        return false;
    }

    private void LogSeeded(ConnectorType type) =>
        _logger.LogInformation("Seeded {Connector} connector from environment configuration.", type);

    private void LogSkipped(ConnectorType type) =>
        _logger.LogInformation("{Connector} connector has no seed configuration — left unconfigured.", type);

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
