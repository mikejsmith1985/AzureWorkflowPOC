// Returned by IAdoTelemetryPreflightService.RunPreflightAsync — wraps outcome and manifest.
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// The outcome of a single preflight run. A successful run always provides a manifest;
/// a failed run provides only an error message (see service contract for failure scenarios).
/// </summary>
public sealed record PreflightResult
{
    /// <summary>
    /// True when preflight completed (Bootstrap or Adaptive). False only when the service
    /// detected a blocking condition (missing config, unreachable ADO, unsupported process).
    /// Partial-success (some fields failed after retries) is still IsSuccess=true.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>Diagnostic message describing why preflight halted. Null when <see cref="IsSuccess"/> is true.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>The manifest written to disk. Null when <see cref="IsSuccess"/> is false.</summary>
    public PreflightManifestBase? Manifest { get; init; }

    /// <summary>Convenience factory for a failed result.</summary>
    public static PreflightResult Fail(string errorMessage) =>
        new() { IsSuccess = false, ErrorMessage = errorMessage };

    /// <summary>Convenience factory for a successful result with its manifest.</summary>
    public static PreflightResult Succeed(PreflightManifestBase manifest) =>
        new() { IsSuccess = true, Manifest = manifest };
}
