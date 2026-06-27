// Inputs handed to an IAppExecutor for a build or run (feature 013).
namespace DBAIAzure.Core.Models;

/// <summary>Whether an <see cref="AppExecutionRequest"/> describes a build or a run.</summary>
public enum ExecutionMode
{
    /// <summary>Build the app's artifact from the repo.</summary>
    Build = 0,

    /// <summary>Run the built artifact.</summary>
    Run = 1
}

/// <summary>
/// The inputs an <c>IAppExecutor</c> receives to build or run an app — the .NET analogue of the
/// reference application's <c>build_app</c>/<c>run_app</c> environment block. Not persisted.
/// </summary>
public record AppExecutionRequest(
    /// <summary>Identity of the app (used for artifact folder / container labelling).</summary>
    string AppId,

    /// <summary>App name (artifact identity).</summary>
    string Name,

    /// <summary>Local repository path bind-mounted into the build container.</summary>
    string RepoLocalPath,

    /// <summary>Optional branch to check out for the build.</summary>
    string? Branch,

    /// <summary>The resolved build or run command to execute.</summary>
    string Command,

    /// <summary>Whether this is a build or a run.</summary>
    ExecutionMode Mode,

    /// <summary>Hard timeout in seconds; the container is stopped if it exceeds this.</summary>
    int TimeoutSeconds,

    /// <summary>
    /// Transient access token used only to obtain a (future remote) repo; never persisted and
    /// redacted from logs (Article IX). Unused for local-path sources.
    /// </summary>
    string? AccessToken = null);
