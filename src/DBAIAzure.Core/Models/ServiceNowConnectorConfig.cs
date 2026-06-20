// Non-secret configuration fields for the ServiceNow connector.
namespace DBAIAzure.Core.Models;

/// <summary>
/// Non-secret fields for the ServiceNow connector, serialized to/from
/// <c>ConnectorConfig.NonSecretConfig</c>. The password or API token is stored separately
/// in the encrypted secrets blob and never appears in this record.
/// </summary>
public record ServiceNowConnectorConfig(
    /// <summary>Full URL of the ServiceNow instance (e.g., <c>https://acme.service-now.com</c>).</summary>
    string InstanceUrl,

    /// <summary>Service account username for Basic Auth.</summary>
    string Username);
