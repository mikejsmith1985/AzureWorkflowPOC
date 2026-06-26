// Outcome of a repo-app run performed in a throwaway container (feature 013).
namespace DBAIAzure.Core.Models;

/// <summary>How a run concluded. <see cref="TimedOut"/> is distinguished so the UI can explain a hang.</summary>
public enum RunOutcome
{
    /// <summary>The run command exited successfully.</summary>
    Succeeded = 0,

    /// <summary>The run command exited with a failure, or the container failed to start.</summary>
    Failed = 1,

    /// <summary>The run exceeded its time limit and was stopped.</summary>
    TimedOut = 2
}

/// <summary>
/// The recorded outcome of running a built <see cref="MonitoredApp"/> in a throwaway container.
/// <see cref="Logs"/> are secret-redacted before being persisted or displayed (FR-009).
/// </summary>
public record AppRunResult(
    /// <summary>How the run concluded.</summary>
    RunOutcome Outcome,

    /// <summary>One-line, human-readable result.</summary>
    string Summary,

    /// <summary>Full captured run logs, with any secret values redacted.</summary>
    string Logs,

    /// <summary>UTC instant the run finished.</summary>
    DateTimeOffset At);
