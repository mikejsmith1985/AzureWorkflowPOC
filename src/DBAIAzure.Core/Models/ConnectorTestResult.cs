// Result of one functional test against an external connector (FR-008 through FR-011).
namespace DBAIAzure.Core.Models;

/// <summary>
/// Outcome of a live functional test against one connector. Returned by
/// <c>IConnectorHealthChecker.TestAsync</c> and each element of <c>CheckAllAsync</c>.
/// </summary>
public record ConnectorTestResult(
    /// <summary>Which connector was tested.</summary>
    ConnectorType Type,

    /// <summary>True when the live round-trip authenticated and exercised the remote resource successfully.</summary>
    bool IsSuccess,

    /// <summary>
    /// Human-readable result: what was confirmed on success, or the specific failure cause on failure.
    /// Never contains raw exception traces — server-side details are logged separately (FR-012).
    /// </summary>
    string Message,

    /// <summary>UTC timestamp of when this test executed.</summary>
    DateTimeOffset TestedAt);
