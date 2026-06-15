// Shared webhook secret-validation logic used by all inbound webhook controllers.
namespace DBAIAzure.Web.Controllers;

/// <summary>
/// Validates the shared secret header that guards every inbound webhook endpoint. Centralised here
/// so the identical guard does not live independently in each controller (DRY, easier to audit).
/// </summary>
internal static class WebhookSecretValidator
{
    /// <summary>
    /// Returns true when the incoming request carries the expected secret in <paramref name="headerName"/>.
    /// Rejects when the secret is not configured — an unconfigured endpoint is treated as locked-out
    /// rather than open, which is the safe default for an external-facing receiver.
    /// </summary>
    internal static bool Validate(
        IConfiguration config,
        IHeaderDictionary headers,
        ILogger logger,
        string configKey,
        string headerName)
    {
        var expectedSecret = config[configKey];
        if (string.IsNullOrWhiteSpace(expectedSecret))
        {
            logger.LogWarning(
                "Webhook secret not configured at {ConfigKey} — rejecting all requests", configKey);
            return false;
        }

        if (!headers.TryGetValue(headerName, out var providedSecret) ||
            !string.Equals(expectedSecret, providedSecret.ToString(), StringComparison.Ordinal))
        {
            logger.LogWarning("Invalid or missing {HeaderName} header", headerName);
            return false;
        }

        return true;
    }
}
