// Domain model: a target repository registered for build/run/monitoring (feature 013).
namespace DBAIAzure.Core.Models;

/// <summary>
/// A target repository registered as a monitored "app": this project builds and runs the repo's
/// application in a throwaway container and (optionally) links a saved workflow to monitor it.
/// Owner-scoped like saved workflows; <see cref="Name"/> is unique per owner. Mirrors the reference
/// application's registered-app concept.
/// </summary>
public record MonitoredApp
{
    /// <summary>Stable unique identifier (GUID string).</summary>
    public string AppId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>Human-chosen name; unique per owner; also the artifact identity.</summary>
    public required string Name { get; init; }

    /// <summary>Owner the app belongs to (matches the workflow-ownership convention).</summary>
    public required string OwnerId { get; init; }

    /// <summary>Local filesystem path of the target repository (the build source).</summary>
    public required string RepoLocalPath { get; init; }

    /// <summary>Optional git ref to check out for the build; null/empty uses the working tree as-is.</summary>
    public string? Branch { get; init; }

    /// <summary>Optional build command; null/empty triggers ecosystem auto-detection.</summary>
    public string? BuildCommand { get; init; }

    /// <summary>Command that runs the built application. Required.</summary>
    public required string RunCommand { get; init; }

    /// <summary>Current lifecycle state.</summary>
    public AppStatus Status { get; init; } = AppStatus.Registered;

    /// <summary>Outcome of the most recent build, or null if never built.</summary>
    public AppBuildResult? LastBuildResult { get; init; }

    /// <summary>Outcome of the most recent run, or null if never run.</summary>
    public AppRunResult? LastRunResult { get; init; }

    /// <summary>Identifier of the saved workflow linked as the monitor, or null when not monitored.</summary>
    public string? LinkedWorkflowId { get; init; }

    /// <summary>UTC instant of the most recent build, or null.</summary>
    public DateTimeOffset? LastBuiltAt { get; init; }

    /// <summary>UTC instant of the most recent run, or null.</summary>
    public DateTimeOffset? LastRunAt { get; init; }

    /// <summary>UTC instant the app was registered.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>UTC instant the app row was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
