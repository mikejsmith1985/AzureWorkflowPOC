// Non-secret configuration fields for the Azure DevOps connector.
namespace DBAIAzure.Core.Models;

/// <summary>
/// Non-secret fields for the Azure DevOps Boards connector, serialized to/from
/// <c>ConnectorConfig.NonSecretConfig</c>. The PAT is stored in the encrypted secrets blob.
/// </summary>
public record AzureDevOpsConnectorConfig(
    /// <summary>ADO organization URL (e.g., <c>https://dev.azure.com/myorg</c>).</summary>
    string OrganizationUrl,

    /// <summary>Target project name within the organization.</summary>
    string ProjectName);
