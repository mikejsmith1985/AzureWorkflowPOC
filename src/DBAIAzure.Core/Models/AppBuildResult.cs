// Outcome of a repo-app build performed in a throwaway container (feature 013).
namespace DBAIAzure.Core.Models;

/// <summary>
/// The recorded outcome of building a <see cref="MonitoredApp"/> in a throwaway container.
/// <see cref="Logs"/> are secret-redacted before being persisted or displayed (FR-009).
/// </summary>
public record AppBuildResult(
    /// <summary>True when the build command completed successfully.</summary>
    bool Succeeded,

    /// <summary>One-line, human-readable result (e.g. "Build complete" or "npm ci failed").</summary>
    string Summary,

    /// <summary>Full captured build logs, with any secret values redacted.</summary>
    string Logs,

    /// <summary>UTC instant the build finished.</summary>
    DateTimeOffset At);
