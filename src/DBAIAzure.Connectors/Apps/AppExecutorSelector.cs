// Chooses the active executor: real Docker when reachable, otherwise the simulated one (feature 013, US4).
using Docker.DotNet;
using DBAIAzure.Core.Interfaces;

namespace DBAIAzure.Connectors.Apps;

/// <summary>
/// Resolves which <see cref="IAppExecutor"/> is active. The simulated executor is used in demo mode or
/// whenever no Docker engine is reachable, so the full register → build → run → monitor flow is
/// demonstrable everywhere with identical surfaces (FR-015); otherwise the real Docker executor runs.
/// </summary>
public static class AppExecutorSelector
{
    /// <summary>
    /// Pure selection rule (unit-testable): returns <paramref name="sim"/> when demo mode is on or no
    /// Docker engine is available; otherwise <paramref name="docker"/>.
    /// </summary>
    public static IAppExecutor Select(bool dockerAvailable, bool demoMode, IAppExecutor docker, IAppExecutor sim)
        => (demoMode || !dockerAvailable) ? sim : docker;

    /// <summary>
    /// Probes for a reachable Docker engine using the default local endpoint, returning the connected
    /// client on success. Never throws — any failure means "not available" and the caller falls back
    /// to the simulated executor.
    /// </summary>
    public static bool TryConnectDocker(out IDockerClient? client)
    {
        client = null;
        try
        {
            var candidate = new DockerClientConfiguration().CreateClient();

            // Docker.DotNet's ping can block at the OS connect layer (for example a missing Windows
            // named pipe when no engine is installed) WITHOUT honoring a CancellationToken, which
            // previously stalled the first /apps render for ~20s. Run the ping on a worker task and
            // enforce a hard wall-clock cap with Wait: if it has not completed successfully in time we
            // abandon it (it is GC-reclaimed when its native call returns) and treat Docker as
            // unavailable, falling back to the simulated executor.
            var ping = Task.Run(() => candidate.System.PingAsync());
            if (!ping.Wait(ProbeTimeout) || !ping.IsCompletedSuccessfully)
            {
                return false;
            }

            client = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Hard upper bound on the Docker reachability probe so it never blocks page rendering.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
}
