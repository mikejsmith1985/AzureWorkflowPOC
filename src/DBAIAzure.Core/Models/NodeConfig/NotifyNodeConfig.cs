// Realized configuration for a FunctionNotify node: which messaging connector to use and what to send.

namespace DBAIAzure.Core.Models.NodeConfig;

/// <summary>
/// The executable configuration for a notification step. Binds the step to a configured messaging
/// connector and describes the recipient and message — never any secret, which the connector
/// resolves at execution time (Article IX).
/// </summary>
public sealed record NotifyNodeConfig
{
    /// <summary>The messaging connector this step sends through (e.g. <see cref="ConnectorType.Teams"/>).</summary>
    public required ConnectorType Connector { get; init; }

    /// <summary>How the recipient is derived from upstream output. Contains no secret values.</summary>
    public required string RecipientMap { get; init; }

    /// <summary>The message body, referencing upstream fields. Contains no secret values.</summary>
    public required string MessageTemplate { get; init; }
}
