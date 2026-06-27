// Realized configuration for a FunctionData node: the read/write operation and its data binding.

namespace DBAIAzure.Core.Models.NodeConfig;

/// <summary>Whether a data step reads from or writes to its bound external store.</summary>
public enum DataOperation
{
    /// <summary>Read from the external store and surface the result in the workflow state.</summary>
    Read,

    /// <summary>Write workflow state to the external store.</summary>
    Write,
}

/// <summary>
/// The executable configuration for a data-access step. Binds the step to a configured connector and
/// describes the operation plus how workflow data maps in and out — never any secret.
/// </summary>
public sealed record DataNodeConfig
{
    /// <summary>The connector backing this data step (e.g. ServiceNow, AzureDevOps).</summary>
    public required ConnectorType Connector { get; init; }

    /// <summary>Whether this step reads or writes.</summary>
    public required DataOperation Operation { get; init; }

    /// <summary>How upstream workflow data is bound to the operation's inputs.</summary>
    public required string InputMap { get; init; }

    /// <summary>How the operation's result is surfaced back into the workflow state.</summary>
    public required string OutputMap { get; init; }
}
